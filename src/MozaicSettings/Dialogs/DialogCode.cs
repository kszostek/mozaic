using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Mozaic.Core.Commands;
using Mozaic.Core.Config;
using Mozaic.Settings.Pages;

namespace Mozaic.Settings.Dialogs;

public partial class BindingDialog : Window
{
    private static readonly string[] Suggestions =
    {
        "exec $term", "kill", "focus left", "focus right", "focus up", "focus down", "focus parent", "focus child",
        "focus mode_toggle", "focus last", "move left", "move right", "move up", "move down", "split h", "split v",
        "split toggle", "layout tabbed", "layout stacking", "layout toggle split", "layout toggle all", "fullscreen toggle",
        "floating toggle", "sticky toggle", "workspace number 1", "workspace next_on_output", "workspace prev_on_output",
        "workspace back_and_forth", "move container to workspace number 1", "move workspace to output right",
        "move workspace to output left", "scratchpad show", "move scratchpad", "resize grow width 10 px or 5 ppt",
        "resize shrink width 10 px or 5 ppt", "resize grow height 10 px or 5 ppt", "resize shrink height 10 px or 5 ppt",
        "mode \"resize\"", "mode \"default\"", "launcher", "launcher windows", "launcher commands", "balance", "settings",
        "gaps inner current plus 2", "gaps inner current minus 2", "bar mode toggle", "pause", "reload", "restart", "exit",
        "exec explorer.exe", "exec notepad.exe",
    };

    private readonly SettingsModel _model;
    private readonly BindingItem? _item;

    public BindingDialog(SettingsModel model, BindingItem? item)
    {
        InitializeComponent();
        _model = model;
        _item = item;
        var mod = model.Doc.Resolve(model.Doc.GetVariable("$mod") ?? "Mod1");
        Capture.ModMask = KeyNames.TryParseModifier(mod, out var mm) ? mm : Modifiers.Alt;
        Capture.Pretty = model.PrettyKeys;
        Capture.Keys = item?.Keys ?? "";
        CommandBox.ItemsSource = Suggestions;
        CommandBox.Text = item?.Command ?? "";
        ModeBox.ItemsSource = model.Modes.ToList();
        ModeBox.SelectedItem = item?.Mode ?? model.SelectedMode;
        ReleaseBox.IsChecked = item?.Release ?? false;
        CommandBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => UpdateHelp()));
        CommandBox.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(UpdateHelp);
        UpdateHelp();
        Loaded += (_, _) =>
        {
            if (item == null) Capture.Focus();
        };
    }

    public string Mode => ModeBox.SelectedItem as string ?? "default";
    public string Keys => Capture.Keys;
    public string Command => CommandBox.Text.Trim();
    public bool Release => ReleaseBox.IsChecked == true;

    private void UpdateHelp()
    {
        var (label, cat) = KeyboardLabels.Describe(_model.Doc.Resolve(Command));
        CommandHelp.Text = label.Length == 0 ? "" : KeyboardLabels.NameOf(cat) + ": " + label;
        CategoryDot.Background = label.Length == 0 ? null : KeyboardLabels.BrushOf(cat);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        if (string.IsNullOrWhiteSpace(Keys)) { ErrorText.Text = L.T("Press the keys first."); return; }
        var combo = _model.ComboOf(Keys, _item?.IsCode ?? false);
        if (combo == null) { ErrorText.Text = L.T("These keys are not valid."); return; }
        if (combo.Value.Modifiers == Modifiers.Win && combo.Value.Vk == 'L') { ErrorText.Text = L.T("Win+L is reserved by Windows and cannot be used."); return; }
        if (Command.Length == 0) { ErrorText.Text = L.T("Enter a command."); return; }
        try
        {
            CommandParser.Parse(_model.Doc.Resolve(Command));
        }
        catch (CommandParseException ex)
        {
            ErrorText.Text = L.T("The command is not valid: ") + ex.Message;
            return;
        }
        var other = _model.Doc.Bindings().FirstOrDefault(b => b.Mode == Mode && b.Line != (_item?.Line ?? -1) && _model.ComboOf(b.Keys, b.IsCode) == combo);
        if (other != null)
        {
            var r = MessageBox.Show(this, L.F("{0} is already used by: {1}\n\nUse it anyway?", _model.PrettyKeys(Keys), other.Command),
                L.T("Shortcut"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        DialogResult = true;
    }
}

public partial class RuleDialog : Window
{
    private static readonly Regex CriterionRx = new("""(\w+)\s*=\s*(?:"((?:[^"\\]|\\.)*)"|'([^']*)'|(\S+))|(\w+)""", RegexOptions.Compiled);
    private string _extra = "";
    private bool _loading;

    public RuleDialog(RuleItem? rule, CandidateItem? window)
    {
        InitializeComponent();
        _loading = true;
        if (rule != null) LoadRule(rule);
        else if (window != null) Fill(window);
        _loading = false;
        Update();
        Loaded += async (_, _) => await LoadWindowsAsync();
    }

    public string Kind { get; private set; } = "for_window";
    public string Criteria { get; private set; } = "";
    public string Action { get; private set; } = "";

    private async Task LoadWindowsAsync()
    {
        var r = await MozaicClient.QueryAsync("get_candidates");
        var list = new List<CandidateItem>();
        if (r is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
            foreach (var e in arr.EnumerateArray())
            {
                var c = CandidateItem.From(e);
                if (c.Title.Length > 0 && !c.Process.Equals("MozaicSettings.exe", StringComparison.OrdinalIgnoreCase)) list.Add(c);
            }
        WindowPicker.ItemsSource = list.OrderBy(c => c.Process, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) WindowPicker.ItemsSource = new[] { new CandidateItem(0, L.T("(Mozaic is not running — type the details below)"), "", "", "", "", null) };
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => _ = LoadWindowsAsync();

    private void WindowPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WindowPicker.SelectedItem is CandidateItem { Window: not 0 } c) Fill(c);
    }

    private void Fill(CandidateItem w)
    {
        _loading = true;
        ProcessBox.Text = Path.GetFileNameWithoutExtension(w.Process);
        ClassBox.Text = w.Class;
        TitleBox.Text = w.Title;
        UseProcess.IsChecked = ProcessBox.Text.Length > 0;
        UseClass.IsChecked = ProcessBox.Text.Length == 0;
        UseTitle.IsChecked = false;
        _loading = false;
        Update();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Exact(string s) => "^" + Regex.Escape(s) + "$";

    private static string FromExact(string s)
    {
        var m = Regex.Match(s, @"^\^(.*)\$$");
        if (!m.Success) return s;
        try { return Regex.Unescape(m.Groups[1].Value); }
        catch (ArgumentException) { return s; }
    }

    private void LoadRule(RuleItem rule)
    {
        var extra = new List<string>();
        foreach (Match m in CriterionRx.Matches(rule.Criteria))
        {
            string key = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[5].Value;
            string value = m.Groups[2].Success ? m.Groups[2].Value.Replace("\\\"", "\"") : m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value;
            switch (key)
            {
                case "process" or "exe" or "instance" or "app" or "app_id":
                    ProcessBox.Text = FromExact(value);
                    UseProcess.IsChecked = true;
                    break;
                case "class":
                    ClassBox.Text = FromExact(value);
                    UseClass.IsChecked = true;
                    break;
                case "title":
                    TitleBox.Text = Regex.Unescape(value);
                    UseTitle.IsChecked = true;
                    break;
                default:
                    extra.Add(m.Value);
                    break;
            }
        }
        _extra = string.Join(' ', extra);
        string action = rule.Action.Trim();
        switch (rule.Kind)
        {
            case "assign":
                ActAssign.IsChecked = true;
                WorkspaceBox.Text = Regex.Replace(action, @"^(→\s*)?(workspace\s+)?(number\s+)?", "").Trim('"', ' ');
                break;
            case "ignore": ActIgnore.IsChecked = true; break;
            case "no_focus": ActNoFocus.IsChecked = true; break;
            default:
                if (action == "floating enable") ActFloat.IsChecked = true;
                else if (action is "floating enable, sticky enable" or "floating enable,sticky enable") ActSticky.IsChecked = true;
                else
                {
                    ActCustom.IsChecked = true;
                    CustomBox.Text = action;
                }
                break;
        }
    }

    private void Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading && IsInitialized) Update();
    }

    private void Update()
    {
        if (Preview == null) return;
        var parts = new List<string>();
        if (UseProcess.IsChecked == true && ProcessBox.Text.Trim().Length > 0)
        {
            var p = ProcessBox.Text.Trim();
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) p = p[..^4];
            parts.Add("process=" + Quote(Exact(p)));
        }
        if (UseClass.IsChecked == true && ClassBox.Text.Trim().Length > 0) parts.Add("class=" + Quote(Exact(ClassBox.Text.Trim())));
        if (UseTitle.IsChecked == true && TitleBox.Text.Length > 0) parts.Add("title=" + Quote(Regex.Escape(TitleBox.Text)));
        if (_extra.Length > 0) parts.Add(_extra);
        Criteria = string.Join(' ', parts);

        if (ActAssign.IsChecked == true)
        {
            Kind = "assign";
            var ws = WorkspaceBox.Text.Trim();
            Action = int.TryParse(ws, out _) ? "workspace number " + ws : "workspace " + ConfigDocument.QuoteIfNeeded(ws);
        }
        else if (ActIgnore.IsChecked == true) { Kind = "ignore"; Action = ""; }
        else if (ActNoFocus.IsChecked == true) { Kind = "no_focus"; Action = ""; }
        else if (ActSticky.IsChecked == true) { Kind = "for_window"; Action = "floating enable, sticky enable"; }
        else if (ActCustom.IsChecked == true) { Kind = "for_window"; Action = CustomBox.Text.Trim(); }
        else { Kind = "for_window"; Action = "floating enable"; }

        Preview.Text = ConfigDocument.FormatRule(Kind, Criteria, Action);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Update();
        ErrorText.Text = "";
        if (Criteria.Length == 0) { ErrorText.Text = L.T("Choose at least one way to recognise the window."); return; }
        var cfg = ConfigParser.Parse(Preview.Text);
        if (cfg.Errors.Count > 0) { ErrorText.Text = cfg.Errors[0].Message; return; }
        DialogResult = true;
    }
}
