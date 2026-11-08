namespace Mozaic.Core;

/// <summary>Integer rectangle in physical (screen) pixels. Right/Bottom are exclusive.</summary>
public readonly record struct Rect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static Rect FromSize(int x, int y, int width, int height) => new(x, y, x + width, y + height);

    public Rect Inset(int left, int top, int right, int bottom)
    {
        int l = Left + left, t = Top + top, r = Right - right, b = Bottom - bottom;
        if (r < l) { int m = (l + r) / 2; l = m; r = m; }
        if (b < t) { int m = (t + b) / 2; t = m; b = m; }
        return new Rect(l, t, r, b);
    }

    public Rect Inset(int all) => Inset(all, all, all, all);

    public Rect Offset(int dx, int dy) => new(Left + dx, Top + dy, Right + dx, Bottom + dy);

    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public int IntersectionArea(Rect o)
    {
        int w = Math.Min(Right, o.Right) - Math.Max(Left, o.Left);
        int h = Math.Min(Bottom, o.Bottom) - Math.Max(Top, o.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }

    /// <summary>Centers this rectangle's size inside <paramref name="area"/>, shrinking it if it does not fit.</summary>
    public Rect CenteredIn(Rect area)
    {
        int w = Math.Min(Width, area.Width), h = Math.Min(Height, area.Height);
        return FromSize(area.CenterX - w / 2, area.CenterY - h / 2, w, h);
    }

    /// <summary>Moves the rectangle (shrinking if necessary) so that it lies fully inside <paramref name="area"/>.</summary>
    public Rect ClampInto(Rect area)
    {
        int w = Math.Min(Width, area.Width), h = Math.Min(Height, area.Height);
        int x = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - w));
        int y = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - h));
        return FromSize(x, y, w, h);
    }

    public bool NearlyEquals(Rect o, int tolerance) =>
        Math.Abs(Left - o.Left) <= tolerance && Math.Abs(Top - o.Top) <= tolerance &&
        Math.Abs(Right - o.Right) <= tolerance && Math.Abs(Bottom - o.Bottom) <= tolerance;

    public override string ToString() => $"{Left},{Top} {Width}x{Height}";
}
