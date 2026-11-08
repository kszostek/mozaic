using System.Globalization;

namespace Mozaic.Core;

public enum NodeKind { Root, Output, Workspace, Container, Window }

public enum LayoutKind { SplitH, SplitV, Stacked, Tabbed }

public enum Direction { Left, Right, Up, Down }

public enum Orientation { None, Horizontal, Vertical }

public static class DirectionExtensions
{
    public static Orientation ToOrientation(this Direction d) =>
        d is Direction.Left or Direction.Right ? Orientation.Horizontal : Orientation.Vertical;

    /// <summary>True for Left/Up (towards the start of a container).</summary>
    public static bool IsBackward(this Direction d) => d is Direction.Left or Direction.Up;

    public static bool TryParse(string s, out Direction d)
    {
        switch (s.ToLowerInvariant())
        {
            case "left": d = Direction.Left; return true;
            case "right": d = Direction.Right; return true;
            case "up": d = Direction.Up; return true;
            case "down": d = Direction.Down; return true;
            default: d = Direction.Left; return false;
        }
    }

    public static LayoutKind ToSplit(this Orientation o) =>
        o == Orientation.Vertical ? LayoutKind.SplitV : LayoutKind.SplitH;
}

/// <summary>
/// A node of the i3-style container tree: root → outputs → workspaces → containers → windows.
/// Floating windows hang off their workspace in <see cref="Floating"/>.
/// </summary>
public sealed class Node
{
    private static int s_nextId;

    public Node(NodeKind kind)
    {
        Kind = kind;
        Id = Interlocked.Increment(ref s_nextId);
    }

    public int Id { get; }
    public NodeKind Kind { get; }
    public LayoutKind Layout { get; set; } = LayoutKind.SplitH;
    public LayoutKind LastSplitLayout { get; set; } = LayoutKind.SplitH;
    public Node? Parent { get; set; }

    /// <summary>Tiling children, in visual order.</summary>
    public List<Node> Children { get; } = new();

    /// <summary>Floating windows (workspaces only).</summary>
    public List<Node> Floating { get; } = new();

    /// <summary>Children (tiling and floating) in most-recently-focused order; index 0 is the most recent.</summary>
    public List<Node> FocusStack { get; } = new();

    /// <summary>Share of the parent's size along the parent's orientation (0..1).</summary>
    public double Percent { get; set; }

    public string Name { get; set; } = "";
    public bool InFloating { get; set; }

    // ---- Window properties
    public IntPtr Handle { get; init; }
    public object? Tag { get; set; }
    public string Title { get; set; } = "";
    public bool Fullscreen { get; set; }
    public bool Sticky { get; set; }
    public bool Urgent { get; set; }
    public bool IsScratchpad { get; set; }
    public Rect? FloatRect { get; set; }
    public HashSet<string> Marks { get; } = new(StringComparer.Ordinal);

    // ---- Workspace properties
    /// <summary>Layout that new windows on an empty workspace get wrapped in (stacked/tabbed); null = plain split.</summary>
    public LayoutKind? WorkspaceLayout { get; set; }

    // ---- Output properties
    public Rect OutputRect { get; set; }
    public Rect WorkArea { get; set; }
    /// <summary>Area available for tiling (work area minus the bar).</summary>
    public Rect LayoutArea { get; set; }
    public int Dpi { get; set; } = 96;
    public bool Primary { get; set; }
    public Node? CurrentWorkspace { get; set; }

    // ---- Computed by the layout engine
    public Rect Rect { get; set; }
    public Rect WindowRect { get; set; }
    public bool Visible { get; set; }

    public int Number => WorkspaceNames.ParseNumber(Name);
    public bool IsWindow => Kind == NodeKind.Window;
    public bool IsSplit => Layout is LayoutKind.SplitH or LayoutKind.SplitV;

    public Orientation Orientation => Kind is NodeKind.Container or NodeKind.Workspace
        ? Layout switch
        {
            LayoutKind.SplitH or LayoutKind.Tabbed => Orientation.Horizontal,
            _ => Orientation.Vertical,
        }
        : Orientation.None;

    public override string ToString() => Kind switch
    {
        NodeKind.Window => $"Window#{Id}({Title})",
        NodeKind.Workspace => $"Workspace({Name})",
        NodeKind.Output => $"Output({Name})",
        NodeKind.Container => $"Con#{Id}[{Layout}]",
        _ => "Root",
    };
}

public static class WorkspaceNames
{
    /// <summary>Parses the leading number of a workspace name ("3", "3:web") or returns -1.</summary>
    public static int ParseNumber(string name)
    {
        int i = 0;
        while (i < name.Length && char.IsAsciiDigit(name[i])) i++;
        if (i == 0 || i > 9) return -1;
        if (i < name.Length && name[i] != ':' && name[i] != ' ') return -1;
        return int.Parse(name.AsSpan(0, i), NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>i3 ordering: numbered workspaces ascending, then named ones in creation order.</summary>
    public static int Compare(Node a, Node b)
    {
        int na = a.Number, nb = b.Number;
        if (na >= 0 && nb >= 0) return na != nb ? na.CompareTo(nb) : string.CompareOrdinal(a.Name, b.Name);
        if (na >= 0) return -1;
        if (nb >= 0) return 1;
        return 0;
    }

    public static string StripNumber(string name)
    {
        int n = ParseNumber(name);
        if (n < 0) return name;
        int colon = name.IndexOf(':');
        return colon >= 0 && colon + 1 < name.Length ? name[(colon + 1)..] : name;
    }

    public static string StripName(string name)
    {
        int n = ParseNumber(name);
        return n < 0 ? name : n.ToString(CultureInfo.InvariantCulture);
    }
}
