using Microsoft.Win32;
using Mozaic.Core;
using Mozaic.Core.Config;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>
/// The keyboard cheat sheet: the picture from the Guide page of the settings app, floating on top of
/// everything while its shortcut is held ($mod+` / $mod+Shift+`). Pressing or releasing Shift while it is
/// shown switches between the $mod and $mod+Shift layers.
/// </summary>
internal sealed class Cheatsheet : Win32Window
{
    private const uint LayerAccent = 0x2E9EF4;

    private readonly WindowManager _wm;
    private bool _visible;
    private int _triggerVk;        // key that shows the sheet while held; 0 = shown by a command
    private Modifiers _holdMods;   // modifiers that must stay down
    private Modifiers _layer;
    private string _mode = "default";
    private int _dpi = 96;
    private double _unit;
    private int _pad, _titleH, _legendH;
    private bool _dark = true;
    private nuint _poll, _timeout;

    public Cheatsheet(WindowManager wm)
    {
        _wm = wm;
        Create(C.WS_EX_LAYERED | C.WS_EX_TRANSPARENT | C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE | C.WS_EX_TOPMOST,
            C.WS_POPUP, "Mozaic cheat sheet", new Rect(0, 0, 1, 1));
        User32.SetLayeredWindowAttributes(Handle, 0, 252, C.LWA_ALPHA);
        WindowOps.SetCornerPreference(Handle, "round");
    }

    public bool IsVisible => _visible;

    /// <summary>Shows the sheet for as long as <paramref name="vk"/> and the binding's modifiers stay pressed.</summary>
    public void ShowHeld(int vk, Modifiers bindingMods)
    {
        if (_visible && _triggerVk == vk) return; // auto-repeat of the held key
        Dispatcher.ClearTimer(ref _timeout);
        _triggerVk = vk;
        _holdMods = bindingMods & ~Modifiers.Shift;
        Show(LayerNow(), _wm.Mode);
        Dispatcher.ClearTimer(ref _poll);
        _poll = Dispatcher.SetInterval(30, Poll);
    }

    /// <summary>Shows the $mod layer (from a command, not a held key) until hidden or for 15 seconds.</summary>
    public void ShowFor(int ms = 15000)
    {
        Dispatcher.ClearTimer(ref _poll);
        Dispatcher.ClearTimer(ref _timeout);
        _triggerVk = 0;
        Show(ModLayer(), _wm.Mode);
        _timeout = Dispatcher.SetTimeout(ms, Hide);
    }

    public void Hide()
    {
        Dispatcher.ClearTimer(ref _poll);
        Dispatcher.ClearTimer(ref _timeout);
        _triggerVk = 0;
        if (!_visible) return;
        _visible = false;
        User32.ShowWindow(Handle, C.SW_HIDE);
    }

    private Modifiers ModLayer() =>
        _wm.Config.Variables.TryGetValue("$mod", out var m) && KeyNames.TryParseModifier(m, out var mods) ? mods : Modifiers.Alt;

    private Modifiers LayerNow()
    {
        var mods = InputHooks.PhysicalModifiers();
        return mods == Modifiers.None && _wm.Mode == "default" ? ModLayer() : mods;
    }

    private void Poll()
    {
        bool held = InputHooks.IsPhysicallyDown(_triggerVk) && (InputHooks.PhysicalModifiers() & _holdMods) == _holdMods;
        if (!held)
        {
            Hide();
            return;
        }
        var layer = LayerNow();
        if (layer == _layer && _wm.Mode == _mode) return;
        _layer = layer;
        _mode = _wm.Mode;
        Invalidate();
    }

    private void Show(Modifiers layer, string mode)
    {
        _layer = layer;
        _mode = mode;
        _dark = SystemUsesDarkTheme();
        User32.GetCursorPos(out var pt);
        var output = _wm.OutputAt(pt.X, pt.Y) ?? _wm.Tree.FocusedOutput;
        if (output == null) return;
        _dpi = output.Dpi;
        double s = _dpi / 96.0;
        var area = output.LayoutArea.IsEmpty ? output.OutputRect : output.LayoutArea;
        _pad = (int)Math.Round(22 * s);
        _titleH = (int)Math.Round(46 * s);
        _legendH = (int)Math.Round(40 * s);
        _unit = Math.Min(66 * s, (area.Width * 0.92 - 2 * _pad) / KeyboardLayout.UnitsW);
        int w = (int)Math.Round(_unit * KeyboardLayout.UnitsW) + 2 * _pad;
        int h = _titleH + (int)Math.Round(_unit * KeyboardLayout.UnitsH) + _legendH + _pad;
        User32.SetWindowPos(Handle, C.HWND_TOPMOST, area.CenterX - w / 2, area.CenterY - h / 2, w, h,
            C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
        _visible = true;
        Invalidate();
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static uint Blend(uint fg, uint bg, double a)
    {
        uint Ch(int shift) => (uint)Math.Round(((fg >> shift) & 0xFF) * a + ((bg >> shift) & 0xFF) * (1 - a));
        return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    private string Title()
    {
        if (_mode != "default") return L.F("{0} mode", _mode);
        var mod = ModLayer();
        var parts = new List<string>();
        if (mod != Modifiers.None && (_layer & mod) == mod) parts.Add("$mod (" + ModifierName(mod) + ")");
        var rest = mod != Modifiers.None && (_layer & mod) == mod ? _layer & ~mod : _layer;
        foreach (var m in new[] { Modifiers.Ctrl, Modifiers.Alt, Modifiers.Shift, Modifiers.Win })
            if (rest.HasFlag(m)) parts.Add(ModifierName(m));
        return parts.Count == 0 ? "…" : string.Join(" + ", parts) + " + …";
    }

    private static string ModifierName(Modifiers m) => string.Join("+",
        new[] { (Modifiers.Ctrl, "Ctrl"), (Modifiers.Alt, "Alt"), (Modifiers.Shift, "Shift"), (Modifiers.Win, "Win") }
            .Where(x => m.HasFlag(x.Item1)).Select(x => x.Item2));

    private IntPtr Font(double px, int weight) =>
        Fonts.Get(_wm.Config.Font.Family, Math.Max(5, px * 72.0 / _dpi), _dpi, weight);

    /// <summary>A font for <paramref name="text"/> that fits <paramref name="width"/> (shrinking long words, like the Guide).</summary>
    private IntPtr FittingFont(Canvas c, string text, double px, int weight, int width)
    {
        var font = Font(px, weight);
        c.SetFont(font);
        int longest = text.Split(' ').Max(c.MeasureWidth);
        if (longest <= width || longest == 0) return font;
        return Font(Math.Max(px * 0.6, px * width / longest), weight);
    }

    protected override void OnPaint(Canvas c)
    {
        var cfg = _wm.Config;
        double u = _unit, s = _dpi / 96.0;
        if (u <= 0) return;
        uint bg = _dark ? 0x202020u : 0xF3F3F3u;
        uint keyFill = _dark ? 0x2D2D2Du : 0xFFFFFFu;
        uint keyBorder = _dark ? 0x454545u : 0xD1D5DBu;
        uint dim = _dark ? 0x9CA3AFu : 0x6B7280u;
        uint text = _dark ? 0xFFFFFFu : 0x111827u;

        var full = new Rect(0, 0, c.Width, c.Height);
        c.Fill(full, bg);

        // Title and hint
        var titleRect = new Rect(_pad, 0, c.Width - _pad, _titleH);
        c.SetFont(Font(19 * s, C.FW_SEMIBOLD));
        c.Text(Title(), titleRect, text);
        if (_triggerVk != 0)
        {
            c.SetFont(Font(13 * s, C.FW_NORMAL));
            c.Text(L.T("release to hide"), titleRect, dim, C.DT_RIGHT);
        }

        // Keys
        var map = KeyboardLayout.BindingsFor(cfg, _mode, _layer);
        var cats = new SortedSet<KeyCategory>();
        int radius = Math.Max(3, (int)Math.Round(u * 0.12));
        int borderW = Math.Max(1, (int)Math.Round(1.5 * s));
        foreach (var cap in KeyboardLayout.Caps)
        {
            double pad = u * 0.05;
            var r = new Rect((int)Math.Round(_pad + cap.X * u + pad), (int)Math.Round(_titleH + cap.Y * u + pad),
                (int)Math.Round(_pad + (cap.X + cap.W) * u - pad), (int)Math.Round(_titleH + (cap.Y + 1) * u - pad));
            var nameRect = new Rect(r.Left + (int)(u * 0.08), r.Top + (int)(u * 0.05), r.Right - (int)(u * 0.05), r.Top + (int)(u * 0.32));
            if (map.TryGetValue(cap.Vk, out var b))
            {
                var (label, cat) = KeyboardLabels.Describe(b.Command);
                cats.Add(cat);
                uint col = KeyboardLabels.Rgb(cat);
                c.RoundRect(r, radius, Blend(col, keyFill, _dark ? 0.33 : 0.2), col, borderW);
                c.RoundRect(new Rect(r.Left, r.Top, r.Right, r.Top + Math.Max(3, (int)Math.Round(u * 0.07))), Math.Max(2, radius / 2), col);
                c.SetFont(Font(u * 0.17, C.FW_NORMAL));
                c.Text(cap.Label, nameRect, dim);
                var labelRect = new Rect(r.Left + (int)(u * 0.05), r.Top + (int)(u * 0.3), r.Right - (int)(u * 0.05), r.Bottom - (int)(u * 0.04));
                c.SetFont(FittingFont(c, label, u * 0.19, C.FW_SEMIBOLD, labelRect.Width));
                c.TextBlock(label, labelRect, text);
            }
            else if (KeyboardLayout.IsLayerModifier(cap.Vk, _layer))
            {
                c.RoundRect(r, radius, LayerAccent);
                c.SetFont(Font(u * 0.22, C.FW_SEMIBOLD));
                c.Text(cap.Label, r, 0xFFFFFF, C.DT_CENTER);
            }
            else
            {
                c.RoundRect(r, radius, keyFill, keyBorder, 1);
                c.SetFont(Font(u * 0.17, C.FW_NORMAL));
                c.Text(cap.Label, nameRect, dim);
            }
        }

        // Legend
        int y = _titleH + (int)Math.Round(u * KeyboardLayout.UnitsH) + (int)Math.Round(8 * s);
        int x = _pad + (int)Math.Round(u * 0.05);
        int sq = (int)Math.Round(14 * s);
        c.SetFont(Font(13 * s, C.FW_NORMAL));
        if (cats.Count == 0) c.Text(L.T("no shortcuts on this layer"), new Rect(x, y, c.Width - _pad, y + _legendH - (int)(8 * s)), dim);
        foreach (var cat in cats)
        {
            int cy = y + (_legendH - (int)(8 * s)) / 2;
            c.RoundRect(new Rect(x, cy - sq / 2, x + sq, cy + sq / 2), Math.Max(2, (int)(3 * s)), KeyboardLabels.Rgb(cat));
            var name = KeyboardLabels.NameOf(cat);
            int tw = c.MeasureWidth(name);
            c.Text(name, new Rect(x + sq + (int)(7 * s), y, x + sq + (int)(7 * s) + tw + 2, y + _legendH - (int)(8 * s)), text);
            x += sq + (int)(7 * s) + tw + (int)(22 * s);
        }
    }

    protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        if (NoActivate(msg, out result)) return true;
        if (msg == C.WM_NCHITTEST)
        {
            result = C.HTTRANSPARENT;
            return true;
        }
        return false;
    }
}
