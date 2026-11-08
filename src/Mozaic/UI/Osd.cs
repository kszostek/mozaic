using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>On-screen display: briefly shows the workspace name / binding mode in the middle of the output.</summary>
internal sealed class Osd : Win32Window
{
    private readonly WindowManager _wm;
    private string _text = "";
    private int _dpi = 96;
    private nuint _timer;
    private bool _visible;

    public Osd(WindowManager wm)
    {
        _wm = wm;
        Create(C.WS_EX_LAYERED | C.WS_EX_TRANSPARENT | C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE | C.WS_EX_TOPMOST,
            C.WS_POPUP, "Mozaic OSD", new Rect(0, 0, 1, 1));
        User32.SetLayeredWindowAttributes(Handle, 0, 230, C.LWA_ALPHA);
        WindowOps.SetCornerPreference(Handle, "round");
    }

    private double FontSize => Math.Max(20, _wm.Config.Font.Size * 2.6);

    /// <summary>Shows text for a moment; <paramref name="sticky"/> keeps it until hidden (binding modes).</summary>
    public void Show(string text, Node output, bool sticky = false)
    {
        _text = text;
        _dpi = output.Dpi;
        var font = Fonts.Get(_wm.Config.Font.Family, FontSize, _dpi, bold: true);
        int textWidth = Canvas.MeasureOnScreen(font, text);
        int pad = (int)Math.Round(28 * _dpi / 96.0);
        int h = Fonts.LineHeight(FontSize, _dpi) + pad;
        int w = Math.Max(h * 2, textWidth + pad * 2);
        var area = output.LayoutArea;
        User32.SetWindowPos(Handle, C.HWND_TOPMOST, area.CenterX - w / 2, area.CenterY - h / 2, w, h,
            C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
        _visible = true;
        Invalidate();
        Dispatcher.ClearTimer(ref _timer);
        if (!sticky) _timer = Dispatcher.SetTimeout(700, Hide);
    }

    public void Hide()
    {
        Dispatcher.ClearTimer(ref _timer);
        if (!_visible) return;
        _visible = false;
        User32.ShowWindow(Handle, C.SW_HIDE);
    }

    protected override void OnPaint(Canvas c)
    {
        var cfg = _wm.Config;
        var full = new Rect(0, 0, c.Width, c.Height);
        c.Fill(full, 0x1E1E1E);
        c.Frame(full, cfg.Focused.Indicator, Math.Max(2, (int)Math.Round(2 * _dpi / 96.0)));
        c.SetFont(Fonts.Get(cfg.Font.Family, FontSize, _dpi, bold: true));
        c.Text(_text, full, 0xFFFFFF, C.DT_CENTER);
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
