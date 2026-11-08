using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Mozaic.Core.Commands;
using Mozaic.Core.Config;

namespace Mozaic.Settings.Pages;

public partial class GuidePage : UserControl, IPage
{
    private sealed record Layer(string Label, Modifiers Mods, string Mode, string Hint)
    {
        public override string ToString() => Label;
    }

    private static readonly (string Title, string Glyph, string Text)[] BasicsText =
    {
        ("Tiling", "",
            "New windows open next to the focused one and share the space. {split h} and {split v} decide whether the next window opens beside or below. {focus left} {focus down} {focus up} {focus right} move the focus; add Shift to move the window itself."),
        ("Workspaces", "",
            "{workspace number 1} … {workspace number 10} switch between workspaces, {move container to workspace number 1} … moves the window there. Windows on hidden workspaces leave the taskbar until you come back."),
        ("Layouts", "",
            "{layout tabbed} shows windows as tabs, {layout stacking} stacks their titles, {layout toggle split} goes back to side by side. {focus parent} selects the whole container, so you can move several windows at once."),
        ("Floating windows", "",
            "{floating toggle} lets a window float above the others and {focus mode_toggle} switches between floating and tiled windows. Dialogs float automatically. {move scratchpad} hides a window in the scratchpad, {scratchpad show} brings it back."),
        ("Mouse", "",
            "Drag a window by its title bar onto another window to place it there; a blue preview shows where it will land. Drag an edge to resize. Hold $mod and drag with the left button to move a window, with the right button to resize it."),
        ("Launcher", "",
            "{launcher} starts any application, {launcher windows} jumps to any window, {launcher commands} lists every shortcut. {exec $term} opens a terminal and {kill} closes the focused window."),
        ("Cheat sheet", "",
            "Hold {cheatsheet} anywhere to see this keyboard on top of your windows; press Shift as well for the $mod + Shift keys. It disappears as soon as you let go."),
        ("Groups and focus", "",
            "Lines in the gaps join the windows of one tile (the part of the screen you split with {split v} or {split h}), and a current runs along them when the focus moves or while $mod is held. Windows you haven't used for a while fade a little, so the active one stands out."),
    };

    private readonly SettingsModel _model;
    private readonly MainWindow _main;
    private readonly DispatcherTimer _refresh;

    public GuidePage(SettingsModel model, MainWindow main)
    {
        InitializeComponent();
        _model = model;
        _main = main;
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _refresh.Tick += (_, _) =>
        {
            _refresh.Stop();
            Build();
        };
        model.Changed += () =>
        {
            _refresh.Stop();
            _refresh.Start();
        };
        main.StatusChanged += () => NotRunning.Visibility = main.MozaicRunning ? Visibility.Collapsed : Visibility.Visible;
        Build();
    }

    public void OnShown() => Build();

    private Modifiers ModMask =>
        _model.Parsed.Variables.TryGetValue("$mod", out var m) && KeyNames.TryParseModifier(m, out var mm) ? mm : Modifiers.Alt;

    private static string ModsText(Modifiers m)
    {
        var parts = new List<string>();
        if (m.HasFlag(Modifiers.Win)) parts.Add("Win");
        if (m.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (m.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        return string.Join(" + ", parts);
    }

    private void Build()
    {
        var cfg = _model.Parsed;
        var mod = ModMask;

        // Layers: modifier combinations used by default-mode bindings ($mod first), then binding modes.
        var layers = cfg.Bindings.Where(b => b.Mode == "default" && b.Combo.Modifiers != Modifiers.None)
            .GroupBy(b => b.Combo.Modifiers)
            .OrderBy(g => g.Key == mod ? 0 : g.Key == (mod | Modifiers.Shift) ? 1 : 2).ThenByDescending(g => g.Count())
            .Select(g => new Layer(ModsText(g.Key), g.Key, "default", L.F("Hold {0} and press a key:", ModsText(g.Key))))
            .ToList();
        foreach (var mode in cfg.Modes.Where(m => m != "default"))
        {
            var enter = cfg.Bindings.FirstOrDefault(b => b.Mode == "default" && Normalize(b.Command) == Normalize($"mode \"{mode}\""));
            string how = enter != null ? enter.Combo.ToString() : "mode \"" + mode + "\"";
            var mods = cfg.Bindings.Where(b => b.Mode == mode).GroupBy(b => b.Combo.Modifiers).OrderByDescending(g => g.Count()).FirstOrDefault()?.Key ?? Modifiers.None;
            layers.Add(new Layer(L.F("{0} mode", mode), mods, mode, L.F("After {0} the keys below do this (Esc leaves the mode):", how)));
        }

        var selected = Layers.SelectedItem as Layer;
        Layers.ItemsSource = layers;
        Layers.SelectedItem = layers.FirstOrDefault(l => l == selected) ?? layers.FirstOrDefault();
        ShowLayer();
        BuildBasics();
    }

    private void Layers_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowLayer();

    private void ShowLayer()
    {
        if (Layers.SelectedItem is not Layer layer) return;
        Keyboard.Config = null;
        Keyboard.Modifiers = layer.Mods;
        Keyboard.Mode = layer.Mode;
        Keyboard.Dark = Native.SystemUsesDarkTheme();
        Keyboard.Config = _model.Parsed;
        LayerHint.Text = layer.Hint + "  " + L.T("Hover a key to see the exact command.");

        Legend.Children.Clear();
        var cats = _model.Parsed.Bindings.Where(b => b.Mode == layer.Mode && b.Combo.Modifiers == layer.Mods)
            .Select(b => KeyboardLabels.Describe(b.Command).Category).Distinct().OrderBy(c => c);
        foreach (var cat in cats)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 22, 6) };
            panel.Children.Add(new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = KeyboardLabels.BrushOf(cat), VerticalAlignment = VerticalAlignment.Center });
            var t = new TextBlock { Text = KeyboardLabels.NameOf(cat), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            panel.Children.Add(t);
            Legend.Children.Add(panel);
        }
    }

    private static string Normalize(string command)
    {
        try { return string.Join(' ', CommandParser.Tokenize(command)).ToLowerInvariant(); }
        catch (Exception) { return command.Trim().ToLowerInvariant(); }
    }

    private string? KeysFor(string command)
    {
        var want = Normalize(_model.Doc.Resolve(command));
        var b = _model.Parsed.Bindings.FirstOrDefault(x => x.Mode == "default" && Normalize(x.Command) == want);
        return b?.Combo.ToString().Replace("+", " + ").Replace("grave", "`");
    }

    private void BuildBasics()
    {
        Basics.Children.Clear();
        string modName = ModsText(ModMask);
        foreach (var (title, glyph, text) in BasicsText)
        {
            var card = new Border { Style = (Style)FindResource("Card"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(18, 14, 18, 16) };
            var stack = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var icon = new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), FontSize = 18 };
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
            head.Children.Add(icon);
            var ht = new TextBlock { Text = L.T(title), FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0) };
            ht.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
            head.Children.Add(ht);
            stack.Children.Add(head);

            var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21 };
            body.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
            var localized = L.T(text).Replace("$mod", modName);
            int pos = 0;
            foreach (Match m in Regex.Matches(localized, @"\{([^}]+)\}"))
            {
                if (m.Index > pos) body.Inlines.Add(new Run(localized[pos..m.Index]));
                var keys = KeysFor(m.Groups[1].Value);
                var run = new Run(" " + (keys ?? m.Groups[1].Value) + " ") { FontWeight = FontWeights.SemiBold };
                if (keys == null) run.FontFamily = (FontFamily)FindResource("MonoFont");
                run.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorPrimaryBrush");
                run.SetResourceReference(TextElement.BackgroundProperty, "SubtleFillColorSecondaryBrush");
                body.Inlines.Add(run);
                pos = m.Index + m.Length;
            }
            if (pos < localized.Length) body.Inlines.Add(new Run(localized[pos..]));
            stack.Children.Add(body);
            card.Child = stack;
            Basics.Children.Add(card);
        }
    }

    private void Start_Click(object sender, RoutedEventArgs e) => _main.StartMozaic();

    private void Shortcuts_Click(object sender, RoutedEventArgs e) => _main.Navigate("keys");

    private static void Open(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    private void GitHub_Click(object sender, RoutedEventArgs e) => Open("https://github.com/kszostek/mozaic#readme");

    private void I3_Click(object sender, RoutedEventArgs e) => Open("https://i3wm.org/docs/userguide.html");
}
