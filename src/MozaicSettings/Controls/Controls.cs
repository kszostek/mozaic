using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Mozaic.Core.Config;

namespace Mozaic.Settings;

/// <summary>A settings card: icon, title, description on the left and any control on the right.</summary>
public sealed class SettingRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(SettingRow), new PropertyMetadata(""));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}

/// <summary>Color swatch + hex value; click opens the Windows color dialog. Color is 0xRRGGBB.</summary>
public sealed class ColorBox : Button
{
    public static readonly DependencyProperty ColorProperty = DependencyProperty.Register(nameof(Color), typeof(uint), typeof(ColorBox),
        new FrameworkPropertyMetadata(0u, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ColorBox)d).Update()));

    private readonly Border _swatch;
    private readonly TextBlock _text;

    public ColorBox()
    {
        SetResourceReference(StyleProperty, typeof(Button));
        _swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray };
        _text = new TextBlock { FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { _swatch, _text } };
        Padding = new Thickness(8, 4, 10, 4);
        MinWidth = 108;
        HorizontalContentAlignment = HorizontalAlignment.Left;
        Update();
    }

    public uint Color { get => (uint)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    private void Update()
    {
        uint c = Color;
        _swatch.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c));
        _text.Text = SettingsModel.Hex(c);
        ToolTip = L.T("Click to choose a color");
    }

    protected override void OnClick()
    {
        base.OnClick();
        var owner = Window.GetWindow(this);
        var hwnd = owner != null ? new WindowInteropHelper(owner).Handle : IntPtr.Zero;
        var picked = Native.PickColor(hwnd, Color);
        if (picked is uint p) Color = p;
    }
}

/// <summary>
/// Records a shortcut: focus it and press the keys. While it has focus Mozaic's hotkeys are paused
/// (otherwise Mozaic would swallow the combination you are trying to record).
/// </summary>
public sealed class KeyCaptureBox : TextBox
{
    public static readonly DependencyProperty KeysProperty = DependencyProperty.Register(nameof(Keys), typeof(string), typeof(KeyCaptureBox),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((KeyCaptureBox)d).ShowKeys()));

    public KeyCaptureBox()
    {
        SetResourceReference(StyleProperty, typeof(TextBox));
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Hand;
    }

    /// <summary>Config notation, e.g. "$mod+Shift+q".</summary>
    public string Keys { get => (string)GetValue(KeysProperty); set => SetValue(KeysProperty, value); }

    /// <summary>The modifier $mod stands for, so recorded combos can use "$mod".</summary>
    public Modifiers ModMask { get; set; } = Modifiers.Alt;

    public Func<string, string>? Pretty { get; set; }

    private void ShowKeys() => Text = string.IsNullOrEmpty(Keys) ? L.T("Click here and press the shortcut…") : Pretty?.Invoke(Keys) ?? Keys;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        MozaicClient.Post("hotkeys off");
        Text = L.T("Press the keys…");
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        MozaicClient.Post("hotkeys on");
        ShowKeys();
    }

    private static Modifiers Current()
    {
        var m = Modifiers.None;
        var k = Keyboard.Modifiers;
        if (k.HasFlag(ModifierKeys.Shift)) m |= Modifiers.Shift;
        if (k.HasFlag(ModifierKeys.Control)) m |= Modifiers.Ctrl;
        if (k.HasFlag(ModifierKeys.Alt)) m |= Modifiers.Alt;
        if (k.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) m |= Modifiers.Win;
        return m;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var mods = Current();
        if (key is Key.LeftAlt or Key.RightAlt or Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            Text = Format(mods, null) + "…";
            return;
        }
        int vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0) return;
        Keys = Format(mods, KeyName(vk));
        // Move focus away so the next key press is not recorded by accident.
        MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e) => e.Handled = true;

    private string Format(Modifiers mods, string? key)
    {
        var parts = new List<string>();
        if (ModMask != Modifiers.None && (mods & ModMask) == ModMask)
        {
            parts.Add("$mod");
            mods &= ~ModMask;
        }
        if (mods.HasFlag(Modifiers.Win)) parts.Add("Mod4");
        if (mods.HasFlag(Modifiers.Ctrl)) parts.Add("Control");
        if (mods.HasFlag(Modifiers.Alt)) parts.Add("Mod1");
        if (mods.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (key != null) parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>Virtual key → i3/X11 key name ("q", "Return", "minus", "F5").</summary>
    public static string KeyName(int vk)
    {
        if (vk is >= 'A' and <= 'Z') return ((char)(vk + 32)).ToString();
        return KeyNames.NameOf(vk);
    }
}

/// <summary>Live miniature of the desktop with the current gaps, colors, borders and bar.</summary>
public sealed class LayoutPreview : FrameworkElement
{
    private SettingsModel? _model;

    public LayoutPreview()
    {
        DataContextChanged += (_, e) =>
        {
            if (_model != null) _model.Changed -= InvalidateVisual;
            _model = e.NewValue as SettingsModel;
            if (_model != null) _model.Changed += InvalidateVisual;
            InvalidateVisual();
        };
    }

    private static Brush B(uint rgb) => new SolidColorBrush(Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 640 : available.Width;
        return new Size(w, Math.Min(360, w * 9 / 16));
    }

    protected override void OnRender(DrawingContext dc)
    {
        var m = _model;
        if (m == null) return;
        var cfg = m.Parsed;
        double h = ActualHeight, w = Math.Min(ActualWidth, h * 16 / 9);
        double ox = (ActualWidth - w) / 2;
        double s = w / 1920.0; // a 1920x1080 desktop
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var screen = new Rect(ox, 0, w, h);
        var wall = new LinearGradientBrush(Color.FromRgb(0x1F, 0x3B, 0x5C), Color.FromRgb(0x4C, 0x2A, 0x5E), 45);
        dc.DrawRoundedRectangle(wall, null, screen, 6, 6);
        dc.PushClip(new RectangleGeometry(screen, 6, 6));

        // Taskbar
        double tb = 48 * s;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xE6, 0x20, 0x20, 0x20)), null, new Rect(ox, h - tb, w, tb));
        var area = new Rect(ox, 0, w, h - tb);

        // Bar
        if (m.BarVisible && cfg.Bar != null)
        {
            double bh = (cfg.Bar.Height > 0 ? cfg.Bar.Height : 24) * s * 1.1;
            var barRect = cfg.Bar.Position == "bottom" ? new Rect(ox, area.Bottom - bh, w, bh) : new Rect(ox, 0, w, bh);
            var bc = cfg.Bar.Colors;
            dc.DrawRectangle(B(bc.Background), null, barRect);
            double bx = barRect.X + 3 * s;
            foreach (var (label, wc) in new[] { ("1", bc.FocusedWorkspace), ("2", bc.InactiveWorkspace), ("3", bc.InactiveWorkspace) })
            {
                var r = new Rect(bx, barRect.Y + 2 * s, 30 * s, bh - 4 * s);
                dc.DrawRectangle(B(wc.Background), new Pen(B(wc.Border), 1), r);
                var ft = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(6, bh * 0.5), B(wc.Text), dpi);
                dc.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
                bx += 32 * s;
            }
            var status = new FormattedText("CPU 4%  |  12:34", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(6, bh * 0.5), B(bc.Statusline), dpi);
            dc.DrawText(status, new Point(barRect.Right - status.Width - 8 * s, barRect.Y + (bh - status.Height) / 2));
            area = cfg.Bar.Position == "bottom" ? new Rect(area.X, area.Y, area.Width, area.Height - bh) : new Rect(area.X, area.Y + bh, area.Width, area.Height - bh);
        }

        // Tiles: one big focused window on the left, two stacked on the right (i3-gaps semantics).
        double inner = cfg.GapsInner * s, outer = cfg.GapsTop * s;
        double a = inner / 2, b = inner - a;
        var work = new Rect(area.X + outer + b, area.Y + outer + b, Math.Max(10, area.Width - 2 * outer - inner), Math.Max(10, area.Height - 2 * outer - inner));
        double leftW = work.Width * 0.55;
        var boxes = new[]
        {
            new Rect(work.X, work.Y, leftW, work.Height),
            new Rect(work.X + leftW, work.Y, work.Width - leftW, work.Height / 2),
            new Rect(work.X + leftW, work.Y + work.Height / 2, work.Width - leftW, work.Height / 2),
        };
        double radius = m.CornerStyle switch { "square" => 0, "small" => 4 * s, _ => 8 * s };
        // The two windows on the right share a tile: the group line runs in the gap between them.
        if (cfg.GroupLines != "off" && inner >= 1)
        {
            uint lc = cfg.GroupLinesColor ?? cfg.Focused.Indicator;
            double gy = boxes[1].Bottom;
            dc.DrawRectangle(B(lc), null, new Rect(boxes[1].X + a + 2 * s, gy - Math.Max(1, inner * 0.18), boxes[1].Width - inner - 4 * s, Math.Max(1.5, inner * 0.36)));
        }
        double titleH = 24 * s, titleGap = 2 * s;
        for (int i = 0; i < boxes.Length; i++)
        {
            var bx = boxes[i];
            var r = new Rect(bx.X + a, bx.Y + a, Math.Max(4, bx.Width - inner), Math.Max(4, bx.Height - inner));
            bool focused = i == 0;
            if (cfg.TitleBars && r.Height > titleH * 3)
            {
                var header = new Rect(r.X, r.Y, r.Width, titleH);
                var cc = focused ? cfg.Focused : cfg.Unfocused;
                double tr = Math.Min(cfg.TitleBarRadius * s, titleH / 2);
                dc.DrawRoundedRectangle(B(cc.Background), new Pen(B(cc.Border), 1), header, tr, tr);
                double hx = header.X + 7 * s;
                if (cfg.TitleBarIcons)
                {
                    var icon = new Rect(hx, header.Y + (titleH - 13 * s) / 2, 13 * s, 13 * s);
                    dc.DrawRoundedRectangle(B(i == 0 ? 0x3B82F6u : i == 1 ? 0xF59E0Bu : 0x22C55Eu), null, icon, 3 * s, 3 * s);
                    hx = icon.Right + 6 * s;
                }
                var title = new FormattedText(L.T("Window") + " " + (i + 1), CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), Math.Max(6, 11 * s), B(cc.Text), dpi);
                dc.DrawText(title, new Point(hx, header.Y + (titleH - title.Height) / 2));
                double bxr = header.Right - 6 * s;
                var pen = new Pen(B(cc.Text), Math.Max(0.8, 1.1 * s));
                for (int k = 0; k < cfg.TitleBarButtons.Count; k++)
                {
                    double g = 9 * s;
                    var gr = new Rect(bxr - g, header.Y + (titleH - g) / 2, g, g);
                    if (cfg.TitleBarButtons[cfg.TitleBarButtons.Count - 1 - k] == "close")
                    {
                        dc.DrawLine(pen, gr.TopLeft, gr.BottomRight);
                        dc.DrawLine(pen, gr.TopRight, gr.BottomLeft);
                    }
                    else
                    {
                        dc.DrawRectangle(null, pen, gr);
                    }
                    bxr -= g + 9 * s;
                }
                r = new Rect(r.X, header.Bottom + titleGap, r.Width, Math.Max(4, r.Height - titleH - titleGap));
            }
            uint border = focused ? cfg.Focused.ChildBorder : cfg.Unfocused.ChildBorder;
            double bw = cfg.BorderWidth > 0 && focused ? Math.Max(1.5, cfg.BorderWidth * s * 1.5) : 1.2;
            if (cfg.BorderWidth > 0 && focused)
            {
                double fr = cfg.BorderRadius * s * 1.5;
                dc.DrawRoundedRectangle(B(border), null, new Rect(r.X - bw, r.Y - bw, r.Width + 2 * bw, r.Height + 2 * bw), fr, fr);
            }
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(0xF7, 0xF7, 0xF8)), m.WindowBorderColors ? new Pen(B(border), focused ? 2 : 1) : null, r, radius, radius);
            dc.DrawRoundedRectangle(new SolidColorBrush(focused ? Color.FromRgb(0xE4, 0xE8, 0xEE) : Color.FromRgb(0xEE, 0xEE, 0xEF)), null,
                new Rect(r.X + 1, r.Y + 1, r.Width - 2, Math.Min(r.Height - 2, 22 * s)), radius, radius);
            // fake content lines
            var line = new SolidColorBrush(Color.FromRgb(0xD9, 0xDD, 0xE3));
            for (int l = 0; l < 4; l++)
            {
                double ly = r.Y + 34 * s + l * 18 * s;
                if (ly + 8 * s > r.Bottom - 6 * s) break;
                dc.DrawRoundedRectangle(line, null, new Rect(r.X + 12 * s, ly, (r.Width - 24 * s) * (l % 2 == 0 ? 0.8 : 0.55), 7 * s), 3 * s, 3 * s);
            }
        }
        dc.Pop();
    }
}
