using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>Colored frame drawn around the focused tiled window ("default_border pixel N"). Click-through.</summary>
internal sealed class FocusFrame : Win32Window
{
    private const uint KeyColor = 0xFF00FF;
    private int _thickness;
    private uint _color;
    private Rect _rect;
    private bool _visible;

    public FocusFrame()
    {
        Create(C.WS_EX_LAYERED | C.WS_EX_TRANSPARENT | C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE, C.WS_POPUP, "Mozaic frame", new Rect(0, 0, 1, 1));
        User32.SetLayeredWindowAttributes(Handle, Canvas.ToColorRef(KeyColor), 255, C.LWA_COLORKEY);
    }

    public void ShowAround(Rect outer, int thickness, uint color, IntPtr target)
    {
        bool changed = outer != _rect || thickness != _thickness || color != _color || !_visible;
        _rect = outer;
        _thickness = thickness;
        _color = color;
        _visible = true;
        User32.SetWindowPos(Handle, C.HWND_TOP, outer.Left, outer.Top, outer.Width, outer.Height,
            C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
        if (changed) Invalidate();
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        User32.ShowWindow(Handle, C.SW_HIDE);
    }

    protected override void OnPaint(Canvas c)
    {
        c.Fill(new Rect(0, 0, c.Width, c.Height), KeyColor);
        c.Frame(new Rect(0, 0, c.Width, c.Height), _color, _thickness);
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

/// <summary>Translucent rectangle that shows where a dragged tiled window will land.</summary>
internal sealed class DropPreview : Win32Window
{
    private uint _color = 0x2E9EF4;
    private bool _visible;
    private Rect _rect;

    public DropPreview()
    {
        Create(C.WS_EX_LAYERED | C.WS_EX_TRANSPARENT | C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE | C.WS_EX_TOPMOST,
            C.WS_POPUP, "Mozaic drop", new Rect(0, 0, 1, 1));
        User32.SetLayeredWindowAttributes(Handle, 0, 90, C.LWA_ALPHA);
    }

    public void Show(Rect r, uint color)
    {
        if (_visible && r == _rect && color == _color) return;
        _rect = r;
        _color = color;
        _visible = true;
        User32.SetWindowPos(Handle, C.HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
        Invalidate();
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        User32.ShowWindow(Handle, C.SW_HIDE);
    }

    protected override void OnPaint(Canvas c)
    {
        c.Fill(new Rect(0, 0, c.Width, c.Height), _color);
        c.Frame(new Rect(0, 0, c.Width, c.Height), 0xFFFFFF, 3);
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

/// <summary>
/// Invisible window that takes the keyboard focus when an empty workspace is focused, so that
/// typing does not go to a window on another workspace (or to the desktop, where Delete deletes files).
/// </summary>
internal sealed class FocusSink : Win32Window
{
    public FocusSink()
    {
        Create(C.WS_EX_TOOLWINDOW | C.WS_EX_LAYERED, C.WS_POPUP, "Mozaic", new Rect(0, 0, 1, 1));
        User32.SetLayeredWindowAttributes(Handle, 0, 0, C.LWA_ALPHA);
    }

    public void Activate(Rect area)
    {
        User32.SetWindowPos(Handle, C.HWND_BOTTOM, area.Left, area.Top, 1, 1, C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
        WindowOps.Focus(Handle);
    }

    protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = 0;
        // Alt+F4 on an empty workspace must not destroy the sink.
        return msg == C.WM_CLOSE;
    }
}
