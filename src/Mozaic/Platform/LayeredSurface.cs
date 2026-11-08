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
