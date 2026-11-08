using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mozaic.Core.Commands;
using Mozaic.Core.Config;

namespace Mozaic.Settings;

public enum KeyCategory { Focus, Move, Workspace, MoveToWorkspace, Layout, Window, Launch, Mode, System, Other }

/// <summary>Short, human labels for commands, as printed on the keyboard picture.</summary>
public static class KeyboardLabels
{
    private static string FirstCommand(string command)
    {
        bool quote = false;
        for (int i = 0; i < command.Length; i++)
        {
            if (command[i] == '"') quote = !quote;
            else if (!quote && command[i] is ',' or ';') return command[..i];
        }
        return command;
    }

    private static string Arrow(string dir) => dir switch
    {
        "left" => "←",
        "right" => "→",
        "up" => "↑",
        "down" => "↓",
        _ => dir,
    };

    public static (string Label, KeyCategory Category) Describe(string command)
    {
        var first = FirstCommand(command.Trim());
        if (first.StartsWith('['))
        {
            int close = first.IndexOf(']');
            if (close > 0) first = first[(close + 1)..].Trim();
        }
        List<string> t;
        try { t = CommandParser.Tokenize(first).Select(x => x.ToLowerInvariant()).ToList(); }
        catch (Exception) { t = new List<string>(); }
        if (t.Count == 0) return ("", KeyCategory.Other);
        string a1 = t.Count > 1 ? t[1] : "", a2 = t.Count > 2 ? t[2] : "";
        string Last() => t[^1];

        switch (t[0])
        {
            case "focus":
                return a1 switch
                {
                    "left" or "right" or "up" or "down" => (L.T("focus") + " " + Arrow(a1), KeyCategory.Focus),
                    "parent" => (L.T("parent"), KeyCategory.Focus),
                    "child" => (L.T("child"), KeyCategory.Focus),
                    "mode_toggle" => (L.T("tiled ↔ floating"), KeyCategory.Focus),
                    "last" => (L.T("last window"), KeyCategory.Focus),
                    "output" => (L.T("monitor") + " " + Arrow(a2), KeyCategory.Focus),
                    "" => (L.T("focus"), KeyCategory.Focus),
                    _ => (L.T("focus") + " " + a1, KeyCategory.Focus),
                };
            case "move":
            {
                var rest = t.Skip(1).Where(x => x is not ("container" or "window" or "to" or "--no-auto-back-and-forth")).ToList();
                if (rest.Count == 0) return (L.T("move"), KeyCategory.Move);
                if (rest[0] is "left" or "right" or "up" or "down") return (L.T("move") + " " + Arrow(rest[0]), KeyCategory.Move);
                if (rest[0] == "workspace" && rest.Count > 2 && rest[1] == "to") return (L.T("ws to") + " " + Arrow(Last()), KeyCategory.Workspace);
                if (rest[0] == "workspace" && rest.Count > 1 && rest[1] == "output") return (L.T("ws to") + " " + Arrow(Last()), KeyCategory.Workspace);
                if (rest[0] == "workspace") return ("→ " + L.T("ws") + " " + WorkspaceArg(rest.Skip(1).ToList()), KeyCategory.MoveToWorkspace);
                if (rest[0] == "output") return ("→ " + L.T("monitor") + " " + Arrow(Last()), KeyCategory.MoveToWorkspace);
                if (rest[0] == "scratchpad") return ("→ " + L.T("scratchpad"), KeyCategory.Window);
                if (rest[0] is "position" or "absolute") return (L.T("center"), KeyCategory.Window);
                return (L.T("move"), KeyCategory.Move);
            }
            case "workspace":
                return (WorkspaceArg(t.Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList()) switch
                {
                    "next_on_output" or "next" => L.T("next ws"),
                    "prev_on_output" or "prev" => L.T("prev ws"),
                    "back_and_forth" => L.T("ws ⇄"),
                    var w => L.T("ws") + " " + w,
                }, KeyCategory.Workspace);
            case "split":
                return (a1 is "h" or "horizontal" ? L.T("split ⇆") : a1 is "v" or "vertical" ? L.T("split ⇅") : L.T("split"), KeyCategory.Layout);
            case "layout":
                return (a1 switch
                {
                    "stacking" or "stacked" => L.T("stacked"),
                    "tabbed" => L.T("tabbed"),
                    "toggle" => a2 == "all" ? L.T("next layout") : L.T("toggle split"),
                    "splith" => L.T("split ⇆"),
                    "splitv" => L.T("split ⇅"),
                    _ => L.T("layout"),
                }, KeyCategory.Layout);
            case "balance": return (L.T("balance"), KeyCategory.Layout);
            case "fullscreen": return (L.T("fullscreen"), KeyCategory.Window);
            case "floating": return (L.T("floating"), KeyCategory.Window);
            case "sticky": return (L.T("sticky"), KeyCategory.Window);
            case "kill": return (L.T("close"), KeyCategory.Window);
            case "scratchpad": return (L.T("scratchpad"), KeyCategory.Window);
            case "resize":
                return ((a1, a2) switch
                {
                    ("grow", "width") => L.T("wider"),
                    ("shrink", "width") => L.T("narrower"),
                    ("grow", "height") => L.T("taller"),
                    ("shrink", "height") => L.T("shorter"),
                    _ => L.T("resize"),
                }, KeyCategory.Layout);
            case "gaps":
                return (t.Contains("plus") ? L.T("gaps +") : t.Contains("minus") ? L.T("gaps −") : L.T("gaps"), KeyCategory.Layout);
            case "exec":
            {
                var raw = first.Trim();
                raw = raw.Length > 4 ? raw[4..].Trim() : "";
                while (raw.StartsWith("--", StringComparison.Ordinal))
                {
                    int sp = raw.IndexOf(' ');
                    raw = sp < 0 ? "" : raw[(sp + 1)..].TrimStart();
                }
                var (file, _) = CommandParser.SplitCommandLine(raw.Trim('"'));
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                return (name switch
                {
                    "wt" or "cmd" or "powershell" or "pwsh" or "alacritty" or "wezterm" or "conhost" => L.T("terminal"),
                    "explorer" => L.T("files"),
                    "" => L.T("run"),
                    _ => name,
                }, KeyCategory.Launch);
            }
            case "launcher":
                return (a1 switch
                {
                    "windows" or "window" => L.T("windows"),
                    "commands" or "command" or "keys" => L.T("shortcuts"),
                    _ => L.T("launcher"),
                }, KeyCategory.Launch);
            case "mode":
            {
                var m = t.Skip(1).FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? "";
                return (m == "default" ? L.T("exit mode") : m + " " + L.T("mode"), KeyCategory.Mode);
            }
            case "reload": return (L.T("reload"), KeyCategory.System);
            case "restart": return (L.T("restart"), KeyCategory.System);
            case "exit": return (L.T("exit"), KeyCategory.System);
            case "confirm": return (t.Contains("exit") ? L.T("exit") : L.T("confirm"), KeyCategory.System);
            case "settings": return (L.T("settings"), KeyCategory.System);
            case "pause": return (L.T("pause"), KeyCategory.System);
            default: return (t[0], KeyCategory.Other);
        }
    }

    private static string WorkspaceArg(List<string> args)
    {
        if (args.Count == 0) return "";
        if (args[0] == "number" && args.Count > 1) return args[1];
        return string.Join(' ', args);
    }

    public static string NameOf(KeyCategory c) => L.T(c switch
    {
        KeyCategory.Focus => "Focus",
        KeyCategory.Move => "Move window",
        KeyCategory.Workspace => "Workspaces",
        KeyCategory.MoveToWorkspace => "Move to workspace",
        KeyCategory.Layout => "Layout & size",
        KeyCategory.Window => "Window",
        KeyCategory.Launch => "Launch",
        KeyCategory.Mode => "Modes",
        KeyCategory.System => "Mozaic",
        _ => "Other",
    });

    public static Color ColorOf(KeyCategory c) => c switch
    {
        KeyCategory.Focus => Color.FromRgb(0x3B, 0x82, 0xF6),
        KeyCategory.Move => Color.FromRgb(0xF5, 0x9E, 0x0B),
        KeyCategory.Workspace => Color.FromRgb(0x22, 0xC5, 0x5E),
        KeyCategory.MoveToWorkspace => Color.FromRgb(0x14, 0xB8, 0xA6),
        KeyCategory.Layout => Color.FromRgb(0xA8, 0x55, 0xF7),
        KeyCategory.Window => Color.FromRgb(0xEF, 0x44, 0x44),
        KeyCategory.Launch => Color.FromRgb(0xEC, 0x48, 0x99),
        KeyCategory.Mode => Color.FromRgb(0xEA, 0xB3, 0x08),
        KeyCategory.System => Color.FromRgb(0x64, 0x74, 0x8B),
        _ => Color.FromRgb(0x94, 0xA3, 0xB8),
    };

    public static Brush BrushOf(KeyCategory c)
    {
        var b = new SolidColorBrush(ColorOf(c));
        b.Freeze();
        return b;
    }
}

/// <summary>
/// A keyboard with every key of one modifier "layer" labelled and colored by what it does —
/// like the keyboard pictures in the i3 user's guide, but generated from the live config.
/// </summary>
public sealed class KeyboardView : FrameworkElement
{
    private sealed record Cap(string Label, int Vk, double X, double Y, double W);

    private const double UnitsW = 18.75, UnitsH = 6.5;
    private static readonly List<Cap> Caps = BuildLayout();

    public static readonly DependencyProperty ConfigProperty = DependencyProperty.Register(nameof(Config), typeof(MozaicConfig),
        typeof(KeyboardView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ModifiersProperty = DependencyProperty.Register(nameof(Modifiers), typeof(Modifiers),
        typeof(KeyboardView), new FrameworkPropertyMetadata(Modifiers.Alt, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(nameof(Mode), typeof(string),
        typeof(KeyboardView), new FrameworkPropertyMetadata("default", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DarkProperty = DependencyProperty.Register(nameof(Dark), typeof(bool),
        typeof(KeyboardView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public MozaicConfig? Config { get => (MozaicConfig?)GetValue(ConfigProperty); set => SetValue(ConfigProperty, value); }
    public Modifiers Modifiers { get => (Modifiers)GetValue(ModifiersProperty); set => SetValue(ModifiersProperty, value); }
    public string Mode { get => (string)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public bool Dark { get => (bool)GetValue(DarkProperty); set => SetValue(DarkProperty, value); }

    /// <summary>Export mode: draws a title, an opaque background and the legend (for README images).</summary>
    public string? Title { get; set; }

    private double TitleHeight => Title != null ? 56 : 0;
    private double LegendHeight => Title != null ? 52 : 0;

    private static List<Cap> BuildLayout()
    {
        var caps = new List<Cap>();
        void Row(double y, double x, IEnumerable<(string L, int Vk, double W)> keys)
        {
            foreach (var (label, vk, w) in keys)
            {
                caps.Add(new Cap(label, vk, x, y, w));
                x += w;
            }
        }
        IEnumerable<(string, int, double)> Chars(string s, int[] vks) => s.Select((c, i) => (c.ToString(), vks[i], 1.0));

        caps.Add(new Cap("Esc", 0x1B, 0, 0, 1));
        for (int i = 0; i < 12; i++) caps.Add(new Cap("F" + (i + 1), 0x70 + i, 2 + i + (i / 4) * 0.5, 0, 1));
        Row(1.25, 0, Chars("`1234567890-=", new[] { 0xC0, '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 0xBD, 0xBB })
            .Append(("Backspace", 0x08, 2.0)));
        Row(2.25, 0, new[] { ("Tab", 0x09, 1.5) }.Concat(Chars("QWERTYUIOP[]", new[] { 'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P', 0xDB, 0xDD }))
            .Append(("\\", 0xDC, 1.5)));
        Row(3.25, 0, new[] { ("Caps", 0x14, 1.75) }.Concat(Chars("ASDFGHJKL;'", new[] { 'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L', 0xBA, 0xDE }))
            .Append(("Enter", 0x0D, 2.25)));
        Row(4.25, 0, new[] { ("Shift", 0xA0, 2.25) }.Concat(Chars("ZXCVBNM,./", new[] { 'Z', 'X', 'C', 'V', 'B', 'N', 'M', 0xBC, 0xBE, 0xBF }))
            .Append(("Shift", 0xA1, 2.75)));
        Row(5.25, 0, new[]
        {
            ("Ctrl", 0xA2, 1.25), ("Win", 0x5B, 1.25), ("Alt", 0xA4, 1.25), ("", 0x20, 6.25),
            ("AltGr", 0xA5, 1.25), ("Win", 0x5C, 1.25), ("Menu", 0x5D, 1.25), ("Ctrl", 0xA3, 1.25),
        });
        Row(1.25, 15.75, new[] { ("Ins", 0x2D, 1.0), ("Home", 0x24, 1.0), ("PgUp", 0x21, 1.0) });
        Row(2.25, 15.75, new[] { ("Del", 0x2E, 1.0), ("End", 0x23, 1.0), ("PgDn", 0x22, 1.0) });
        caps.Add(new Cap("↑", 0x26, 16.75, 4.25, 1));
        Row(5.25, 15.75, new[] { ("←", 0x25, 1.0), ("↓", 0x28, 1.0), ("→", 0x27, 1.0) });
        return caps;
    }

    private Dictionary<int, Binding> BindingsForLayer()
    {
        var map = new Dictionary<int, Binding>();
        var cfg = Config;
        if (cfg == null) return map;
        foreach (var b in cfg.Bindings)
            if (b.Mode == Mode && b.Combo.Modifiers == Modifiers) map.TryAdd(b.Combo.Vk, b);
        return map;
    }

    private bool IsLayerModifier(int vk) => vk switch
    {
        0xA0 or 0xA1 => Modifiers.HasFlag(Modifiers.Shift),
        0xA2 or 0xA3 => Modifiers.HasFlag(Modifiers.Ctrl),
        0xA4 => Modifiers.HasFlag(Modifiers.Alt),
        0x5B or 0x5C => Modifiers.HasFlag(Modifiers.Win),
        _ => false,
    };

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) || available.Width <= 0 ? 1000 : available.Width;
        return new Size(w, w * UnitsH / UnitsW + TitleHeight + LegendHeight);
    }

    private Rect CapRect(Cap c, double u)
    {
        double pad = u * 0.05;
        return new Rect(c.X * u + pad, TitleHeight + c.Y * u + pad, c.W * u - 2 * pad, u - 2 * pad);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double u = ActualWidth / UnitsW;
        if (u <= 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        bool dark = Dark;
        var map = BindingsForLayer();

        var bgPage = new SolidColorBrush(dark ? Color.FromRgb(0x20, 0x20, 0x20) : Colors.White);
        var keyFill = new SolidColorBrush(dark ? Color.FromRgb(0x2D, 0x2D, 0x2D) : Color.FromRgb(0xF3, 0xF4, 0xF6));
        var keyStroke = new Pen(new SolidColorBrush(dark ? Color.FromRgb(0x45, 0x45, 0x45) : Color.FromRgb(0xD1, 0xD5, 0xDB)), 1);
        var dim = new SolidColorBrush(dark ? Color.FromRgb(0x9C, 0xA3, 0xAF) : Color.FromRgb(0x6B, 0x72, 0x80));
        var text = new SolidColorBrush(dark ? Colors.White : Color.FromRgb(0x11, 0x18, 0x27));
        var accent = SystemParameters.WindowGlassColor;
        var regular = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        if (Title != null)
        {
            dc.DrawRectangle(bgPage, null, new Rect(0, 0, ActualWidth, ActualHeight));
            var ft = new FormattedText(Title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, bold, 26, text, dpi);
            dc.DrawText(ft, new Point(u * 0.1, 10));
        }

        foreach (var cap in Caps)
        {
            var r = CapRect(cap, u);
            double radius = u * 0.12;
            map.TryGetValue(cap.Vk, out var binding);
            if (binding != null)
            {
                var (label, cat) = KeyboardLabels.Describe(binding.Command);
                var col = KeyboardLabels.ColorOf(cat);
                var fill = new SolidColorBrush(dark ? Color.FromArgb(0x55, col.R, col.G, col.B) : Color.FromArgb(0x33, col.R, col.G, col.B));
                dc.DrawRoundedRectangle(fill, new Pen(new SolidColorBrush(col), 1.5), r, radius, radius);
                dc.DrawRoundedRectangle(new SolidColorBrush(col), null, new Rect(r.X, r.Y, r.Width, Math.Max(3, u * 0.07)), radius / 2, radius / 2);
                DrawText(dc, cap.Label, regular, u * 0.17, dim, new Rect(r.X + u * 0.08, r.Y + u * 0.08, r.Width, u * 0.25), TextAlignment.Left, 1, dpi);
                DrawText(dc, label, bold, u * 0.19, text, new Rect(r.X + u * 0.05, r.Y + u * 0.33, r.Width - u * 0.1, r.Height - u * 0.36), TextAlignment.Center, 2, dpi);
            }
            else if (IsLayerModifier(cap.Vk))
            {
                var acc = new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0xF4));
                dc.DrawRoundedRectangle(acc, null, r, radius, radius);
                DrawText(dc, cap.Label, bold, u * 0.22, Brushes.White, new Rect(r.X, r.Y + r.Height / 2 - u * 0.17, r.Width, u * 0.34), TextAlignment.Center, 1, dpi);
            }
            else
            {
                dc.DrawRoundedRectangle(keyFill, keyStroke, r, radius, radius);
                DrawText(dc, cap.Label, regular, u * 0.17, dim, new Rect(r.X + u * 0.08, r.Y + u * 0.08, r.Width, u * 0.25), TextAlignment.Left, 1, dpi);
            }
        }
        _ = accent;

        if (Title != null)
        {
            // Legend
            var cats = map.Values.Select(b => KeyboardLabels.Describe(b.Command).Category).Distinct().OrderBy(c => c).ToList();
            double x = u * 0.1, y = ActualHeight - LegendHeight + 16;
            foreach (var cat in cats)
            {
                dc.DrawRoundedRectangle(KeyboardLabels.BrushOf(cat), null, new Rect(x, y + 3, 16, 16), 3, 3);
                var ft = new FormattedText(KeyboardLabels.NameOf(cat), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, regular, 15, text, dpi);
                dc.DrawText(ft, new Point(x + 22, y));
                x += 22 + ft.Width + 26;
            }
        }
    }

    private static void DrawText(DrawingContext dc, string s, Typeface face, double size, Brush brush, Rect r, TextAlignment align, int lines, double dpi)
    {
        if (string.IsNullOrEmpty(s) || r.Width <= 2) return;
        // Shrink so that the longest word fits instead of being cut off ("fullscre…").
        double longest = s.Split(' ').Max(w => new FormattedText(w, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, size, brush, dpi).Width);
        if (longest > r.Width) size = Math.Max(size * 0.6, size * r.Width / longest);
        var ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, Math.Max(6, size), brush, dpi)
        {
            MaxTextWidth = r.Width,
            MaxLineCount = lines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = align,
        };
        double y = lines > 1 ? r.Y + Math.Max(0, (r.Height - ft.Height) / 2) : r.Y;
        dc.DrawText(ft, new Point(r.X, y));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        double u = ActualWidth / UnitsW;
        var p = e.GetPosition(this);
        var map = BindingsForLayer();
        foreach (var cap in Caps)
        {
            if (!CapRect(cap, u).Contains(p)) continue;
            if (map.TryGetValue(cap.Vk, out var b))
            {
                var tip = b.Combo + "\n" + b.Command;
                if (!Equals(ToolTip, tip)) ToolTip = tip;
                return;
            }
            break;
        }
        ToolTip = null;
    }

    /// <summary>Renders the view to a PNG (used for the README images).</summary>
    public void SavePng(string path, double width)
    {
        Measure(new Size(width, double.PositiveInfinity));
        Arrange(new Rect(DesiredSize));
        UpdateLayout();
        var bmp = new RenderTargetBitmap((int)(DesiredSize.Width * 2), (int)(DesiredSize.Height * 2), 192, 192, PixelFormats.Pbgra32);
        bmp.Render(this);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(path);
        enc.Save(fs);
    }
}

/// <summary>--render-guide: writes keyboard pictures for the documentation.</summary>
public static class GuideRenderer
{
    public static void RenderAll(string configPath, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var text = File.Exists(configPath) ? File.ReadAllText(configPath) : DefaultConfig.Text;
        var cfg = ConfigParser.Parse(text, configPath);
        var mod = cfg.Variables.TryGetValue("$mod", out var m) && KeyNames.TryParseModifier(m, out var mm) ? mm : Modifiers.Alt;
        string modName = mod == Modifiers.Win ? "Win" : "Alt";
        var layers = new (Modifiers Mods, string Mode, string Title, string File)[]
        {
            (mod, "default", $"$mod ({modName}) + …", "keyboard-mod.png"),
            (mod | Modifiers.Shift, "default", $"$mod ({modName}) + Shift + …", "keyboard-mod-shift.png"),
            (Modifiers.None, "resize", L.T("Resize mode") + "  ($mod + R)", "keyboard-resize.png"),
        };
        foreach (var (mods, mode, title, file) in layers)
        {
            var view = new KeyboardView { Config = cfg, Modifiers = mods, Mode = mode, Dark = false, Title = title };
            view.SavePng(Path.Combine(outDir, file), 1500);
        }
    }
}
