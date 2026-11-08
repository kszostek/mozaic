using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mozaic.Core.Commands;
using Mozaic.Core.Config;

namespace Mozaic.Settings;

/// <summary>
/// A keyboard with every key of one modifier "layer" labelled and colored by what it does —
/// like the keyboard pictures in the i3 user's guide, but generated from the live config.
/// </summary>
public sealed class KeyboardView : FrameworkElement
{
    private const double UnitsW = KeyboardLayout.UnitsW, UnitsH = KeyboardLayout.UnitsH;
    private static IReadOnlyList<KeyboardLayout.KeyCap> Caps => KeyboardLayout.Caps;

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


    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) || available.Width <= 0 ? 1000 : available.Width;
        return new Size(w, w * UnitsH / UnitsW + TitleHeight + LegendHeight);
    }

    private Rect CapRect(KeyboardLayout.KeyCap c, double u)
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
        var map = KeyboardLayout.BindingsFor(Config, Mode, Modifiers);

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
            else if (KeyboardLayout.IsLayerModifier(cap.Vk, Modifiers))
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
        var map = KeyboardLayout.BindingsFor(Config, Mode, Modifiers);
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
