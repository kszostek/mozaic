using Mozaic.Core;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>
/// A 32-bit premultiplied-alpha pixel buffer that is pushed to a layered window with UpdateLayeredWindow
/// (per-pixel transparency, no WM_PAINT). Pixels are 0xAARRGGBB, top-down.
/// </summary>
internal sealed unsafe class LayeredSurface : IDisposable
{
    private IntPtr _dc, _bitmap, _old;

    public uint* Bits { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>A memory DC with the buffer selected, for drawing with GDI (GDI leaves alpha undefined: see <see cref="ApplyRoundRect"/>).</summary>
    public IntPtr Hdc => _dc;

    public void Clear()
    {
        if (Bits != null) new Span<uint>(Bits, Width * Height).Clear();
    }

    /// <summary>
    /// Turns what GDI drew inside <paramref name="shape"/> into an anti-aliased rounded rectangle: pixels outside
    /// become transparent, the edge gets a <paramref name="borderWidth"/> outline in <paramref name="border"/>.
    /// </summary>
    public void ApplyRoundRect(Rect shape, double radius, uint? border = null, double borderWidth = 0)
    {
        int x0 = Math.Max(0, shape.Left), x1 = Math.Min(Width, shape.Right);
        int y0 = Math.Max(0, shape.Top), y1 = Math.Min(Height, shape.Bottom);
        double br = 0, bg = 0, bb = 0;
        bool hasBorder = border is uint bc && borderWidth > 0;
        if (hasBorder)
        {
            br = (border!.Value >> 16) & 0xFF;
            bg = (border.Value >> 8) & 0xFF;
            bb = border.Value & 0xFF;
        }
        for (int y = y0; y < y1; y++)
        {
            uint* row = Bits + y * Width;
            for (int x = x0; x < x1; x++)
            {
                double d = ShapeMath.RoundRectDistance(x + 0.5, y + 0.5, shape, radius);
                double outer = ShapeMath.Coverage(d);
                uint p = row[x];
                double r = (p >> 16) & 0xFF, g = (p >> 8) & 0xFF, b = p & 0xFF;
                if (hasBorder)
                {
                    double inner = ShapeMath.Coverage(d + borderWidth);
                    r = r * inner + br * (1 - inner);
                    g = g * inner + bg * (1 - inner);
                    b = b * inner + bb * (1 - inner);
                }
                row[x] = ((uint)(outer * 255 + 0.5) << 24) | ((uint)(r * outer + 0.5) << 16) | ((uint)(g * outer + 0.5) << 8) | (uint)(b * outer + 0.5);
            }
        }
    }

    public void Resize(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (width == Width && height == Height && _bitmap != 0) return;
        Free();
        var screen = User32.GetDC(0);
        _dc = Gdi32.CreateCompatibleDC(screen);
        User32.ReleaseDC(0, screen);
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32,
            biCompression = C.BI_RGB,
        };
        _bitmap = Gdi32.CreateDIBSection(_dc, &bi, C.DIB_RGB_COLORS, out var bits, 0, 0);
        if (_bitmap == 0)
        {
            Gdi32.DeleteDC(_dc);
            _dc = 0;
            throw new InvalidOperationException("CreateDIBSection failed");
        }
        _old = Gdi32.SelectObject(_dc, _bitmap);
        Bits = (uint*)bits;
        Width = width;
        Height = height;
    }

    /// <summary>Shows the buffer in <paramref name="hwnd"/> at screen position (x, y), resizing the window to the buffer.</summary>
    public bool Present(IntPtr hwnd, int x, int y)
    {
        if (_bitmap == 0) return false;
        var dst = new POINT { X = x, Y = y };
        var src = new POINT();
        var size = new SIZE { cx = Width, cy = Height };
        var blend = new BLENDFUNCTION { BlendOp = C.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = C.AC_SRC_ALPHA };
        return User32.UpdateLayeredWindow(hwnd, 0, &dst, &size, _dc, &src, 0, &blend, C.ULW_ALPHA);
    }

    private void Free()
    {
        if (_dc != 0)
        {
            Gdi32.SelectObject(_dc, _old);
            Gdi32.DeleteObject(_bitmap);
            Gdi32.DeleteDC(_dc);
        }
        _dc = _bitmap = _old = 0;
        Bits = null;
        Width = Height = 0;
    }

    public void Dispose() => Free();
}

/// <summary>Anti-aliasing helpers for rounded shapes.</summary>
internal static class ShapeMath
{
    /// <summary>Signed distance from a point to a rounded rectangle (negative inside).</summary>
    public static double RoundRectDistance(double px, double py, Rect r, double radius)
    {
        double hw = r.Width / 2.0, hh = r.Height / 2.0;
        radius = Math.Max(0, Math.Min(radius, Math.Min(hw, hh)));
        double qx = Math.Abs(px - (r.Left + hw)) - (hw - radius), qy = Math.Abs(py - (r.Top + hh)) - (hh - radius);
        double ox = Math.Max(qx, 0), oy = Math.Max(qy, 0);
        return Math.Sqrt(ox * ox + oy * oy) + Math.Min(Math.Max(qx, qy), 0) - radius;
    }

    /// <summary>How much of a pixel is covered, from its center's signed distance to the edge.</summary>
    public static double Coverage(double distance) => Math.Clamp(0.5 - distance, 0, 1);
}
