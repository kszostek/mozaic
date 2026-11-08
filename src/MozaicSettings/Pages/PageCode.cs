using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Mozaic.Core.Config;
using Mozaic.Settings.Dialogs;

namespace Mozaic.Settings.Pages;

public partial class GeneralPage : UserControl
{
    public GeneralPage() => InitializeComponent();
}

public partial class AppearancePage : UserControl
{
    public AppearancePage() => InitializeComponent();
}

public partial class BarPage : UserControl
{
    public BarPage() => InitializeComponent();
}

public partial class WorkspacesPage : UserControl
{
    public WorkspacesPage() => InitializeComponent();
}

public partial class KeysPage : UserControl
{
    private readonly SettingsModel _model;

    public KeysPage(SettingsModel model)
    {
        InitializeComponent();
        _model = model;
    }

    private BindingItem? ItemFrom(object sender) => (sender as FrameworkElement)?.Tag as BindingItem ?? List.SelectedItem as BindingItem;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new BindingDialog(_model, null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _model.AddBinding(dlg.Mode, dlg.Keys, dlg.Command, dlg.Release);
    }

    private void Edit(BindingItem? item)
    {
        if (item == null) return;
        var dlg = new BindingDialog(_model, item) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true) return;
        if (dlg.Mode != item.Mode)
        {
            _model.DeleteLine(item.Line);
            _model.AddBinding(dlg.Mode, dlg.Keys, dlg.Command, dlg.Release);
        }
        else
        {
            _model.UpdateBinding(item, dlg.Keys, dlg.Command, dlg.Release);
        }
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => Edit(ItemFrom(sender));

    private void List_DoubleClick(object sender, MouseButtonEventArgs e) => Edit(List.SelectedItem as BindingItem);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ItemFrom(sender) is { } item) _model.DeleteLine(item.Line);
    }
}

public partial class RulesPage : UserControl
{
    private readonly SettingsModel _model;

    public RulesPage(SettingsModel model)
    {
        InitializeComponent();
        _model = model;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddRule(_model, Window.GetWindow(this), null);

    public static void AddRule(SettingsModel model, Window? owner, CandidateItem? window)
    {
        var dlg = new RuleDialog(null, window) { Owner = owner };
        if (dlg.ShowDialog() == true) model.AddRule(dlg.Kind, dlg.Criteria, dlg.Action);
    }

    private void Edit(RuleItem? item)
    {
        if (item == null) return;
        var dlg = new RuleDialog(item, null) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _model.UpdateRule(item, dlg.Kind, dlg.Criteria, dlg.Action);
    }

    private void Edit_Click(object sender, RoutedEventArgs e) => Edit((sender as FrameworkElement)?.Tag as RuleItem);

    private void List_DoubleClick(object sender, MouseButtonEventArgs e) => Edit(List.SelectedItem as RuleItem);

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is RuleItem item) _model.DeleteLine(item.Line);
    }
}

public partial class StartupPage : UserControl
{
    private readonly SettingsModel _model;

    public StartupPage(SettingsModel model)
    {
        InitializeComponent();
        _model = model;
    }

    private void Add_Click(object sender, RoutedEventArgs e) => _model.AddExec(false, "notepad.exe");

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExecItem item) _model.DeleteLine(item.Line);
    }
}

public partial class EditorPage : UserControl, IPage
{
    private readonly SettingsModel _model;
    private readonly DispatcherTimer _debounce;
    private bool _updating;

    public EditorPage(SettingsModel model)
    {
        InitializeComponent();
        _model = model;
        PathText.Text = model.Path;
        Editor.SyntaxHighlighting = LoadHighlighting(Native.SystemUsesDarkTheme());
        Editor.SetResourceReference(ForegroundProperty, "TextFillColorPrimaryBrush");
        Editor.TextArea.SelectionCornerRadius = 2;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.HighlightCurrentLine = true;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            _updating = true;
            try { _model.ReplaceText(Editor.Text); }
            finally { _updating = false; }
            UpdateErrors();
        };
        Editor.TextChanged += (_, _) =>
        {
            if (_updating) return;
            _debounce.Stop();
            _debounce.Start();
        };
        model.Changed += () =>
        {
            if (!_updating && IsVisible) LoadText();
        };
        LoadText();
    }

    public void OnShown() => LoadText();

    private void LoadText()
    {
        if (Editor.Text == _model.Doc.Text) { UpdateErrors(); return; }
        _updating = true;
        try
        {
            int caret = Math.Min(Editor.CaretOffset, _model.Doc.Text.Length);
            Editor.Document.Text = _model.Doc.Text;
            Editor.CaretOffset = caret;
        }
        finally
        {
            _updating = false;
        }
        UpdateErrors();
    }

    private void UpdateErrors() => ErrorPanel.Visibility = _model.HasErrors ? Visibility.Visible : Visibility.Collapsed;

    private void ErrorList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ErrorList.SelectedItem is not ConfigError err || err.Line <= 0 || err.Line > Editor.Document.LineCount) return;
        var line = Editor.Document.GetLineByNumber(err.Line);
        Editor.Select(line.Offset, line.Length);
        Editor.ScrollToLine(err.Line);
        Editor.Focus();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + _model.Path + "\"") { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private void Defaults_Click(object sender, RoutedEventArgs e)
    {
        var r = MessageBox.Show(Window.GetWindow(this), L.T("Replace the whole configuration with the defaults? You can still discard the change before saving."),
            L.T("Mozaic Settings"), MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (r == MessageBoxResult.OK) _model.ReplaceText(DefaultConfig.Text.Replace("\n", Environment.NewLine));
        LoadText();
    }

    /// <summary>Syntax highlighting for i3 config files.</summary>
    private static IHighlightingDefinition LoadHighlighting(bool dark)
    {
        string comment = dark ? "#6A9955" : "#008000", keyword = dark ? "#569CD6" : "#0000C0", variable = dark ? "#C586C0" : "#8B008B",
            str = dark ? "#CE9178" : "#A31515", color = dark ? "#4EC9B0" : "#00808A", criteria = dark ? "#DCDCAA" : "#795E26",
            command = dark ? "#9CDCFE" : "#1F5FAF";
        var xshd = $"""
<SyntaxDefinition name="i3" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
  <Color name="Comment" foreground="{comment}" fontStyle="italic" />
  <Color name="Keyword" foreground="{keyword}" fontWeight="bold" />
  <Color name="Variable" foreground="{variable}" />
  <Color name="String" foreground="{str}" />
  <Color name="ColorValue" foreground="{color}" />
  <Color name="Criteria" foreground="{criteria}" />
  <Color name="Command" foreground="{command}" />
  <RuleSet>
    <Span color="Comment" begin="^\s*\#" />
    <Span color="String" begin="&quot;" end="&quot;" />
    <Span color="Criteria" begin="\[" end="\]" />
    <Rule color="Variable">\$[A-Za-z_][A-Za-z0-9_]*</Rule>
    <Rule color="ColorValue">\#[0-9a-fA-F]{"{"}3,8{"}"}\b</Rule>
    <Rule color="Keyword">^\s*(bindsym|bindcode|set|set_from_resource|mode|bar|colors|exec|exec_always|for_window|assign|no_focus|ignore|workspace|font|gaps|smart_gaps|default_border|default_orientation|workspace_layout|floating_modifier|focus_follows_mouse|mouse_warping|focus_wrapping|workspace_auto_back_and_forth|focus_on_window_activation|include|client\.[a-z_]+|window_border_colors|corner_style|floating_always_on_top|flatten_containers|tiling_drag|remember_layout|autotiling|osd|welcome|position|status_command|status_blocks|datetime_format|show_title|workspace_icons|workspace_buttons|binding_mode_indicator|height|strip_workspace_numbers|background|statusline|separator|focused_workspace|active_workspace|inactive_workspace|urgent_workspace|binding_mode)\b</Rule>
    <Rule color="Command">\b(focus|move|split|layout|fullscreen|floating|sticky|kill|resize|scratchpad|launcher|reload|restart|exit|confirm|balance|settings|nop|swap|mark|unmark)\b</Rule>
  </RuleSet>
</SyntaxDefinition>
""";
        using var reader = XmlReader.Create(new StringReader(xshd));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}

public partial class DiagnosticsPage : UserControl, IPage
{
    private readonly SettingsModel _model;
    private readonly MainWindow _main;

    public DiagnosticsPage(SettingsModel model, MainWindow main)
    {
        InitializeComponent();
        _model = model;
        _main = main;
    }

    public async void OnShown() => await RefreshAsync();

    private async Task RefreshAsync()
    {
        var v = await MozaicClient.QueryAsync("get_version");
        if (v is JsonElement ver && ver.ValueKind == JsonValueKind.Object)
        {
            string human = ver.TryGetProperty("human_readable", out var h) ? h.GetString() ?? "" : "";
            string cfg = ver.TryGetProperty("loaded_config_file_name", out var c) ? c.GetString() ?? "" : "";
            int windows = ver.TryGetProperty("windows", out var w) ? w.GetInt32() : 0;
            Info.Text = $"{human}  ·  {L.F("{0} managed windows", windows)}\n{cfg}";
        }
        else
        {
            Info.Text = L.T("Mozaic is not running");
        }

        var list = new List<CandidateItem>();
        var c2 = await MozaicClient.QueryAsync("get_candidates");
        if (c2 is JsonElement arr && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in arr.EnumerateArray())
                list.Add(CandidateItem.From(e));
        }
        WindowList.ItemsSource = list.OrderBy(i => i.Order).ThenBy(i => i.Process, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private async void CommandBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RunAsync();
    }

    private async Task RunAsync()
    {
        var cmd = CommandBox.Text.Trim();
        if (cmd.Length == 0) return;
        var reply = await MozaicClient.SendAsync("command", cmd);
        CommandResult.Text = reply ?? L.T("Mozaic is not running");
        await RefreshAsync();
    }

    private async void Reload_Click(object sender, RoutedEventArgs e)
    {
        await MozaicClient.SendAsync("command", "reload");
        await RefreshAsync();
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        await MozaicClient.SendAsync("command", "restart");
        await Task.Delay(2500);
        await RefreshAsync();
    }

    private void Log_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mozaic");
        if (App.Instance.Length > 0) dir = Path.Combine(dir, "instances", App.Instance);
        var log = Path.Combine(dir, "mozaic.log");
        try { Process.Start(new ProcessStartInfo(File.Exists(log) ? log : dir) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private void CreateRule_Click(object sender, RoutedEventArgs e)
    {
        RulesPage.AddRule(_model, Window.GetWindow(this), WindowList.SelectedItem as CandidateItem);
        _main.Navigate("rules");
    }
}

/// <summary>A window as reported by Mozaic's get_candidates.</summary>
public sealed record CandidateItem(long Window, string Title, string Class, string Process, string Verdict, string Reason, string? Workspace)
{
    public static CandidateItem From(JsonElement e) => new(
        e.GetProperty("window").GetInt64(),
        e.GetProperty("title").GetString() ?? "",
        e.GetProperty("class").GetString() ?? "",
        e.GetProperty("process").GetString() ?? "",
        e.GetProperty("verdict").GetString() ?? "",
        e.GetProperty("reason").GetString() ?? "",
        e.TryGetProperty("workspace", out var w) ? w.GetString() : null);

    public int Order => Verdict switch { "tiled" => 0, "floating" => 1, "minimized" or "scratchpad" => 2, "pending" => 3, _ => 4 };

    public string VerdictLabel => Verdict switch
    {
        "tiled" => L.T("tiled") + (Workspace != null ? " · " + Workspace : ""),
        "floating" => L.T("floating") + (Workspace != null ? " · " + Workspace : ""),
        "minimized" => L.T("minimized"),
        "scratchpad" => L.T("scratchpad"),
        "pending" => L.T("waiting"),
        _ => L.T("not managed"),
    };

    public string ReasonLabel => L.T(Reason);

    public string Display => (Title.Length > 0 ? Title : Class) + "  —  " + Process;
}
