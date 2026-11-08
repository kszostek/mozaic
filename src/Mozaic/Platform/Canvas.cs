using Mozaic.Core;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>Double-buffered GDI drawing surface. Colors are 0xRRGGBB.</summary>
internal sealed unsafe class Canvas : IDisposable
{
    private readonly IntPtr _target;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _oldBitmap;
    private IntPtr _oldFont;

    public Canvas(IntPtr targetDc, int width, int height)
    {
        _target = targetDc;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Hdc = Gdi32.CreateCompatibleDC(targetDc);
        _bitmap = Gdi32.CreateCompatibleBitmap(targetDc, Width, Height);
        _oldBitmap = Gdi32.SelectObject(Hdc, _bitmap);
        Gdi32.SetBkMode(Hdc, C.TRANSPARENT);
    }

    public IntPtr Hdc { get; }
    public int Width { get; }
    public int Height { get; }

    public static uint ToColorRef(uint rgb) => ((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF);

    public void Fill(Rect r, uint rgb)
    {
        var rc = RECT.From(r);
        var brush = Gdi32.CreateSolidBrush(ToColorRef(rgb));
        User32.FillRect(Hdc, &rc, brush);
        Gdi32.DeleteObject(brush);
    }

    public void Frame(Rect r, uint rgb, int width = 1)
    {
        if (width <= 0) return;
        Fill(new Rect(r.Left, r.Top, r.Right, r.Top + width), rgb);
        Fill(new Rect(r.Left, r.Bottom - width, r.Right, r.Bottom), rgb);
        Fill(new Rect(r.Left, r.Top, r.Left + width, r.Bottom), rgb);
        Fill(new Rect(r.Right - width, r.Top, r.Right, r.Bottom), rgb);
    }

    public void SetFont(IntPtr font)
    {
        var old = Gdi32.SelectObject(Hdc, font);
        if (_oldFont == 0) _oldFont = old;
    }

    public int MeasureWidth(string text)
    {
        if (text.Length == 0) return 0;
        fixed (char* p = text)
        {
            Gdi32.GetTextExtentPoint32W(Hdc, p, text.Length, out var size);
            return size.cx;
        }
    }

    public void Text(string text, Rect r, uint rgb, uint align = C.DT_LEFT)
    {
        if (text.Length == 0 || r.IsEmpty) return;
        Gdi32.SetTextColor(Hdc, ToColorRef(rgb));
        var rc = RECT.From(r);
        fixed (char* p = text)
        {
            User32.DrawTextW(Hdc, p, text.Length, &rc, align | C.DT_SINGLELINE | C.DT_VCENTER | C.DT_NOPREFIX | C.DT_END_ELLIPSIS);
        }
    }

    /// <summary>Measures text with a font without a window to paint into.</summary>
    public static int MeasureOnScreen(IntPtr font, string text)
    {
        if (text.Length == 0) return 0;
        var dc = User32.GetDC(0);
        var old = Gdi32.SelectObject(dc, font);
        int w;
        fixed (char* p = text)
        {
            Gdi32.GetTextExtentPoint32W(dc, p, text.Length, out var size);
            w = size.cx;
        }
        Gdi32.SelectObject(dc, old);
        User32.ReleaseDC(0, dc);
        return w;
    }

    public void Dispose()
    {
        Gdi32.BitBlt(_target, 0, 0, Width, Height, Hdc, 0, 0, C.SRCCOPY);
        if (_oldFont != 0) Gdi32.SelectObject(Hdc, _oldFont);
        Gdi32.SelectObject(Hdc, _oldBitmap);
        Gdi32.DeleteObject(_bitmap);
        Gdi32.DeleteDC(Hdc);
    }
}

/// <summary>Caches GDI fonts per family/size/DPI.</summary>
internal static class Fonts
{
    private static readonly Dictionary<(string, double, int, bool), IntPtr> s_cache = new();

    public static IntPtr Get(string family, double points, int dpi, bool bold = false)
    {
        var key = (family, points, dpi, bold);
        if (s_cache.TryGetValue(key, out var f)) return f;
        int height = -(int)Math.Round(points * dpi / 72.0);
        f = Gdi32.CreateFontW(height, 0, 0, 0, bold ? C.FW_BOLD : C.FW_NORMAL, 0, 0, 0, C.DEFAULT_CHARSET,
            C.OUT_DEFAULT_PRECIS, C.CLIP_DEFAULT_PRECIS, C.CLEARTYPE_QUALITY, C.DEFAULT_PITCH, family);
        s_cache[key] = f;
        return f;
    }

    /// <summary>Pixel height of a line of text in the given font.</summary>
    public static int LineHeight(double points, int dpi) => (int)Math.Ceiling(points * dpi / 72.0 * 1.33);
}
