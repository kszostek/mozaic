using System.Windows.Media;

namespace Mozaic;

/// <summary>WPF colors for the shared keyboard categories (Shared/KeyboardModel.cs).</summary>
public static partial class KeyboardLabels
{
    public static Color ColorOf(KeyCategory c)
    {
        uint v = Rgb(c);
        return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static Brush BrushOf(KeyCategory c)
    {
        var b = new SolidColorBrush(ColorOf(c));
        b.Freeze();
        return b;
    }
}
