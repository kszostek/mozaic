namespace Mozaic.Core;

/// <summary>
/// The gap between two neighbouring windows of the same tile (a container that splits one part of the
/// workspace into several windows) — where Mozaic draws the "current" line that shows they belong together.
/// </summary>
/// <param name="Rect">The gap itself (between the two windows' visible frames).</param>
/// <param name="Vertical">True for a vertical line (children side by side).</param>
/// <param name="Level">0: the tile of the focused window, 1: a tile containing it, 2: any other tile.</param>
public readonly record struct GroupEdge(Node Container, int Index, Rect Rect, bool Vertical, int Level);

public static class GroupEdges
{
    /// <summary>
    /// Edges inside the tiles of a visible workspace, after <see cref="LayoutEngine.Compute"/>. The workspace's own
    /// split (and a container that is its only child) separates tiles from each other, so it gets no lines.
    /// <paramref name="focus"/> is the workspace's focused window or container (it decides the levels).
    /// </summary>
    public static List<GroupEdge> Compute(Node workspace, Node? focus, int innerGap)
    {
        var edges = new List<GroupEdge>();
        if (innerGap < 2 || !workspace.Visible) return edges;
        int a = innerGap / 2, b = innerGap - a;

        var topLevel = new HashSet<Node> { workspace };
        for (var t = workspace; t.Children.Count == 1 && t.Children[0].Kind == NodeKind.Container; t = t.Children[0])
            topLevel.Add(t.Children[0]);

        var chain = new HashSet<Node>();
        Node? group = focus == null || focus.InFloating ? null : focus.Kind == NodeKind.Window ? focus.Parent : focus;
        for (var n = group; n != null && n.Kind is NodeKind.Container or NodeKind.Workspace; n = n.Parent) chain.Add(n);

        void Walk(Node n)
        {
            if (n.Kind == NodeKind.Window || !n.Visible) return;
            var kids = n.Children;
            if (n.IsSplit && kids.Count >= 2 && !topLevel.Contains(n))
            {
                int level = n == group ? 0 : chain.Contains(n) ? 1 : 2;
                bool horiz = n.Layout == LayoutKind.SplitH;
                for (int i = 0; i + 1 < kids.Count; i++)
                {
                    if (!kids[i].Visible || !kids[i + 1].Visible) continue;
                    // Children share the cross axis (a workspace's own Rect is larger: it includes the outer gaps).
                    var r = kids[i].Rect;
                    var edge = horiz
                        ? new Rect(r.Right - b, r.Top + a, r.Right + a, r.Bottom - b)
                        : new Rect(r.Left + a, r.Bottom - b, r.Right - b, r.Bottom + a);
                    if (!edge.IsEmpty) edges.Add(new GroupEdge(n, i, edge, horiz, level));
                }
            }
            foreach (var c in kids) Walk(c);
        }

        Walk(workspace);
        return edges;
    }
}

/// <summary>How transparent a window is, by how recently it was used.</summary>
public static class OpacityCurve
{
    /// <summary>
    /// Opacity in percent for the window used <paramref name="rank"/> focus changes ago: 0 = the active window
    /// (100%), 1 = the previously used one (<paramref name="first"/>%), then each older window a bit less,
    /// approaching <paramref name="min"/>%.
    /// </summary>
    public static int Percent(int rank, int first, int min)
    {
        if (rank <= 0) return 100;
        first = Math.Clamp(first, 1, 100);
        min = Math.Clamp(Math.Min(min, first), 1, 100);
        return (int)Math.Round(min + (first - min) * Math.Pow(0.7, rank - 1));
    }

    public static byte ToAlpha(int percent) => (byte)Math.Clamp((int)Math.Round(percent * 255 / 100.0), 0, 255);
}
