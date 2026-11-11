using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>
/// Colored frame drawn around the focused tiled window ("default_border pixel N"), with rounded corners
/// (border_radius). Four click-through layered strips (top, bottom, left, right) with smooth edges, so even a
/// big window costs only a few thin buffers.
/// </summary>
internal sealed class FocusFrame
{
    private readonly FrameStrip[] _strips = { new(), new(), new(), new() };
    private (Rect Outer, int Thickness, int Radius, uint Color) _last;
    private bool _visible;

    public void ShowAround(Rect outer, int thickness, int radius, uint color)
    {
        var key = (outer, thickness, radius, color);
        if (_visible && key == _last)
        {
            foreach (var s in _strips) s.Raise();
            return;
        }
        _last = key;
        _visible = true;
        radius = Math.Clamp(radius, 0, Math.Min(outer.Width, outer.Height) / 2);
        var inner = outer.Inset(thickness);
        int innerRadius = Math.Max(0, radius - thickness);
        // Top and bottom strips are tall enough to contain the rounded corners.
        int band = Math.Min(Math.Max(thickness, radius), outer.Height / 2);
        var parts = new[]
        {
            new Rect(outer.Left, outer.Top, outer.Right, outer.Top + band),
            new Rect(outer.Left, outer.Bottom - band, outer.Right, outer.Bottom),
            new Rect(outer.Left, outer.Top + band, outer.Left + thickness, outer.Bottom - band),
            new Rect(outer.Right - thickness, outer.Top + band, outer.Right, outer.Bottom - band),
        };
        for (int i = 0; i < 4; i++) _strips[i].Show(parts[i], outer, radius, inner, innerRadius, color);
    }

    public void Hide()
    {
        if (!_visible) return;
        _visible = false;
        foreach (var s in _strips) s.HideStrip();
    }

    public void Destroy()
    {
        foreach (var s in _strips) s.Destroy();
    }

    private sealed unsafe class FrameStrip : Win32Window
    {
        private readonly LayeredSurface _surface = new();
        private bool _shown;

        public FrameStrip() => Create(C.WS_EX_LAYERED | C.WS_EX_TRANSPARENT | C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE,
            C.WS_POPUP, "Mozaic frame", new Rect(0, 0, 1, 1));

        public void Show(Rect part, Rect outer, int radius, Rect inner, int innerRadius, uint color)
        {
            if (part.Width <= 0 || part.Height <= 0)
            {
                HideStrip();
                return;
            }
            _surface.Resize(part.Width, part.Height);
            double cr = (color >> 16) & 0xFF, cg = (color >> 8) & 0xFF, cb = color & 0xFF;
            for (int y = 0; y < part.Height; y++)
            {
                uint* row = _surface.Bits + y * part.Width;
                double gy = part.Top + y + 0.5;
                for (int x = 0; x < part.Width; x++)
                {
                    double gx = part.Left + x + 0.5;
                    double a = ShapeMath.Coverage(ShapeMath.RoundRectDistance(gx, gy, outer, radius)) *
                               (1 - ShapeMath.Coverage(ShapeMath.RoundRectDistance(gx, gy, inner, innerRadius)));
                    row[x] = ((uint)(a * 255 + 0.5) << 24) | ((uint)(cr * a + 0.5) << 16) | ((uint)(cg * a + 0.5) << 8) | (uint)(cb * a + 0.5);
                }
            }
            _surface.Present(Handle, part.Left, part.Top);
            _shown = true;
            Raise();
        }

        public void Raise() =>
            User32.SetWindowPos(Handle, C.HWND_TOP, 0, 0, 0, 0, C.SWP_NOMOVE | C.SWP_NOSIZE | C.SWP_NOACTIVATE | (_shown ? C.SWP_SHOWWINDOW : 0));

        public void HideStrip()
        {
            if (!_shown) return;
            _shown = false;
            User32.ShowWindow(Handle, C.SW_HIDE);
        }

        protected override void OnDestroyed() => _surface.Dispose();

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
