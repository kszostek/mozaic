using Mozaic.Core;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>GDI drawing surface: double-buffered for WM_PAINT, or direct (e.g. on a <see cref="LayeredSurface"/>). Colors are 0xRRGGBB.</summary>
internal sealed unsafe class Canvas : IDisposable
{
    private readonly IntPtr _target;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _oldBitmap;
    private readonly bool _direct;
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

    /// <summary>Draws straight into <paramref name="dc"/> (no buffer, nothing copied on dispose).</summary>
    public static Canvas Direct(IntPtr dc, int width, int height) => new(dc, width, height, direct: true);

    private Canvas(IntPtr dc, int width, int height, bool direct)
    {
        _direct = direct;
        _target = dc;
        Hdc = dc;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Gdi32.SetBkMode(Hdc, C.TRANSPARENT);
    }

    public void Line(int x1, int y1, int x2, int y2, uint rgb, int width = 1)
    {
        var pen = Gdi32.CreatePen(C.PS_SOLID, Math.Max(1, width), ToColorRef(rgb));
        var old = Gdi32.SelectObject(Hdc, pen);
        Gdi32.MoveToEx(Hdc, x1, y1, 0);
        Gdi32.LineTo(Hdc, x2, y2);
        Gdi32.SelectObject(Hdc, old);
        Gdi32.DeleteObject(pen);
    }

    /// <summary>Outline of a rounded rectangle (nothing filled).</summary>
    public void RoundFrame(Rect r, int radius, uint rgb, int width = 1)
    {
        var pen = Gdi32.CreatePen(C.PS_SOLID, Math.Max(1, width), ToColorRef(rgb));
        var oldPen = Gdi32.SelectObject(Hdc, pen);
        var oldBrush = Gdi32.SelectObject(Hdc, Gdi32.GetStockObject(C.NULL_BRUSH));
        Gdi32.RoundRect(Hdc, r.Left, r.Top, r.Right, r.Bottom, radius * 2, radius * 2);
        Gdi32.SelectObject(Hdc, oldBrush);
        Gdi32.SelectObject(Hdc, oldPen);
        Gdi32.DeleteObject(pen);
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

    /// <summary>Rounded rectangle; <paramref name="border"/> null = no outline.</summary>
    public void RoundRect(Rect r, int radius, uint fill, uint? border = null, int borderWidth = 1)
    {
        var brush = Gdi32.CreateSolidBrush(ToColorRef(fill));
        var pen = border is uint b ? Gdi32.CreatePen(C.PS_SOLID, Math.Max(1, borderWidth), ToColorRef(b)) : Gdi32.GetStockObject(C.NULL_PEN);
        var oldBrush = Gdi32.SelectObject(Hdc, brush);
        var oldPen = Gdi32.SelectObject(Hdc, pen);
        // GDI's RoundRect excludes the right/bottom edge of a pen-less shape; +1 keeps sizes consistent.
        int extra = border == null ? 1 : 0;
        Gdi32.RoundRect(Hdc, r.Left, r.Top, r.Right + extra, r.Bottom + extra, radius * 2, radius * 2);
        Gdi32.SelectObject(Hdc, oldBrush);
        Gdi32.SelectObject(Hdc, oldPen);
        Gdi32.DeleteObject(brush);
        if (border != null) Gdi32.DeleteObject(pen);
    }

    /// <summary>Word-wrapped text, centered vertically in <paramref name="r"/> (lines that don't fit are cut).</summary>
    public void TextBlock(string text, Rect r, uint rgb, uint align = C.DT_CENTER)
    {
        if (text.Length == 0 || r.IsEmpty) return;
        Gdi32.SetTextColor(Hdc, ToColorRef(rgb));
        var calc = new RECT(r.Left, r.Top, r.Right, r.Top);
        fixed (char* p = text)
        {
            User32.DrawTextW(Hdc, p, text.Length, &calc, align | C.DT_WORDBREAK | C.DT_NOPREFIX | C.DT_CALCRECT);
            int h = Math.Min(calc.Bottom - calc.Top, r.Height);
            int top = r.Top + (r.Height - h) / 2;
            var rc = new RECT(r.Left, top, r.Right, top + h);
            User32.DrawTextW(Hdc, p, text.Length, &rc, align | C.DT_WORDBREAK | C.DT_NOPREFIX | C.DT_END_ELLIPSIS);
        }
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
        if (_direct)
        {
            if (_oldFont != 0) Gdi32.SelectObject(Hdc, _oldFont);
            return;
        }
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
    private static readonly Dictionary<(string, double, int, int), IntPtr> s_cache = new();

    public static IntPtr Get(string family, double points, int dpi, bool bold = false) =>
        Get(family, points, dpi, bold ? C.FW_BOLD : C.FW_NORMAL);

    public static IntPtr Get(string family, double points, int dpi, int weight)
    {
        points = Math.Round(points * 2) / 2; // half-point steps keep the cache small
        var key = (family, points, dpi, weight);
        if (s_cache.TryGetValue(key, out var f)) return f;
        int height = -(int)Math.Round(points * dpi / 72.0);
        f = Gdi32.CreateFontW(height, 0, 0, 0, weight, 0, 0, 0, C.DEFAULT_CHARSET,
            C.OUT_DEFAULT_PRECIS, C.CLIP_DEFAULT_PRECIS, C.CLEARTYPE_QUALITY, C.DEFAULT_PITCH, family);
        s_cache[key] = f;
        return f;
    }

    /// <summary>Pixel height of a line of text in the given font.</summary>
    public static int LineHeight(double points, int dpi) => (int)Math.Ceiling(points * dpi / 72.0 * 1.33);
}
