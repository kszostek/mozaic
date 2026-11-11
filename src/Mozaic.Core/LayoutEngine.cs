namespace Mozaic.Core;

/// <summary>Per-output layout parameters, already scaled to physical pixels.</summary>
public sealed class LayoutSettings
{
    public int InnerGap { get; set; }
    public int OuterTop { get; set; }
    public int OuterRight { get; set; }
    public int OuterBottom { get; set; }
    public int OuterLeft { get; set; }
    public bool SmartGaps { get; set; }
    public int BorderWidth { get; set; }
    public int TabHeight { get; set; } = 24;
    /// <summary>A title bar above every tiled window of a split container (titlebars yes).</summary>
    public bool TitleBars { get; set; }
    /// <summary>Space between a title bar and its window.</summary>
    public int TitleGap { get; set; } = 2;
}

/// <summary>
/// A title bar to draw: the tabs of a visible tabbed/stacked container, or (with title bars on) the title bar of
/// one window, in which case <see cref="Container"/> is the window itself.
/// </summary>
public readonly record struct TabBarInfo(Node Container, Rect Rect, bool Stacked, int RowHeight)
{
    public bool IsWindowTitle => Container.Kind == NodeKind.Window;
}

/// <summary>
/// Computes the rectangles of every visible con. Gaps follow i3-gaps semantics:
/// "inner" between windows, "outer" added on top of inner at the workspace edges.
/// </summary>
public static class LayoutEngine
{
    private readonly record struct Ctx(int A, int B, int Border, int TabHeight, List<TabBarInfo> Bars, bool TitleBars, int TitleGap);

    public static List<TabBarInfo> Compute(Tree tree, Func<Node, LayoutSettings> settingsFor)
    {
        var bars = new List<TabBarInfo>();
        foreach (var n in tree.AllNodes()) n.Visible = false;
        foreach (var output in tree.Outputs)
        {
            var ws = output.CurrentWorkspace;
            if (ws == null) continue;
            ComputeWorkspace(output, ws, settingsFor(output), bars);
        }
        return bars;
    }

    public static void ComputeWorkspace(Node output, Node ws, LayoutSettings s, List<TabBarInfo> bars)
    {
        output.Visible = true;
        ws.Visible = true;
        ws.Rect = output.LayoutArea;

        int inner = Math.Max(0, s.InnerGap);
        int ot = s.OuterTop, or = s.OuterRight, ob = s.OuterBottom, ol = s.OuterLeft;
        if (s.SmartGaps && CountVisibleLeaves(ws) <= 1)
        {
            inner = 0;
            ot = or = ob = ol = 0;
        }
        // Every leaf box is inset by A on its left/top and B on its right/bottom so that two
        // neighbours are exactly 'inner' apart; the workspace area is inset so edges get outer+inner.
        int a = inner / 2, b = inner - a;
        var ctx = new Ctx(a, b, Math.Max(0, s.BorderWidth), Math.Max(8, s.TabHeight), bars, s.TitleBars, Math.Max(0, s.TitleGap));
        var area = ws.Rect.Inset(ol + b, ot + b, or + a, ob + a);
        int firstBar = bars.Count;
        LayoutChildren(ws, area, ctx);
        // A fullscreen window covers the workspace, including its title bars and tabs.
        if (Tree.WindowsIn(ws).Any(w => w.Fullscreen)) bars.RemoveRange(firstBar, bars.Count - firstBar);

        foreach (var f in ws.Floating)
        {
            f.Visible = true;
            var r = f.FloatRect ?? Tree.DefaultFloatRect(ws);
            f.Rect = r;
            f.WindowRect = r;
        }
        foreach (var w in Tree.WindowsIn(ws))
        {
            if (!w.Fullscreen) continue;
            w.Visible = true;
            w.Rect = output.OutputRect;
            w.WindowRect = output.OutputRect;
        }
    }

    private static int CountVisibleLeaves(Node n)
    {
        if (n.Kind == NodeKind.Window) return 1;
        if (n.Children.Count == 0) return 0;
        if (n.Layout is LayoutKind.Stacked or LayoutKind.Tabbed) return 1;
        int s = 0;
        foreach (var c in n.Children) s += CountVisibleLeaves(c);
        return s;
    }

    private static void ComputeNode(Node n, Rect r, Ctx ctx)
    {
        n.Rect = r;
        n.Visible = true;
        if (n.Kind == NodeKind.Window)
        {
            var box = r.Inset(ctx.A, ctx.A, ctx.B, ctx.B);
            // Windows in tabs/stacks already have a title (their tab); very short boxes keep all their space.
            if (ctx.TitleBars && n.Parent is { IsSplit: true } && box.Height >= ctx.TabHeight * 3)
            {
                var header = new Rect(box.Left, box.Top, box.Right, box.Top + ctx.TabHeight);
                ctx.Bars.Add(new TabBarInfo(n, header, false, ctx.TabHeight));
                box = new Rect(box.Left, header.Bottom + ctx.TitleGap, box.Right, box.Bottom);
            }
            n.WindowRect = ctx.Border > 0 ? box.Inset(ctx.Border) : box;
            return;
        }
        LayoutChildren(n, r, ctx);
    }

    private static void LayoutChildren(Node n, Rect r, Ctx ctx)
    {
        var kids = n.Children;
        int count = kids.Count;
        if (count == 0) return;

        if (n.IsSplit)
        {
            Tree.FixPercent(n);
            bool horiz = n.Layout == LayoutKind.SplitH;
            int size = horiz ? r.Width : r.Height;
            int start = horiz ? r.Left : r.Top;
            double acc = 0;
            int pos = start;
            for (int i = 0; i < count; i++)
            {
                acc += kids[i].Percent;
                int end = i == count - 1 ? start + size : start + (int)Math.Round(acc * size);
                var cr = horiz ? new Rect(pos, r.Top, end, r.Bottom) : new Rect(r.Left, pos, r.Right, end);
                ComputeNode(kids[i], cr, ctx);
                pos = end;
            }
            return;
        }

        // Tabbed / stacked: title bar on top, only the focused child is shown.
        var box = r.Inset(ctx.A, ctx.A, ctx.B, ctx.B);
        bool stacked = n.Layout == LayoutKind.Stacked;
        int barH = Math.Min(box.Height, (stacked ? count : 1) * ctx.TabHeight);
        var header = new Rect(box.Left, box.Top, box.Right, box.Top + barH);
        var content = new Rect(box.Left, box.Top + barH, box.Right, box.Bottom);
        ctx.Bars.Add(new TabBarInfo(n, header, stacked, ctx.TabHeight));
        var active = n.FocusStack.FirstOrDefault(c => !c.InFloating) ?? kids[0];
        // Expand by the leaf inset so the child's own inset cancels out and it fills the content area.
        var expanded = new Rect(content.Left - ctx.A, content.Top - ctx.A, content.Right + ctx.B, content.Bottom + ctx.B);
        ComputeNode(active, expanded, ctx);
    }
}
