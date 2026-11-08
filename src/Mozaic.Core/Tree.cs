using System.Globalization;

namespace Mozaic.Core;

public sealed class TreeOptions
{
    /// <summary>"horizontal", "vertical" or "auto" (horizontal on landscape outputs).</summary>
    public string DefaultOrientation { get; set; } = "auto";

    /// <summary>Default layout new windows on empty workspaces get wrapped in (null = split).</summary>
    public LayoutKind? WorkspaceLayout { get; set; }

    public bool FocusWrapping { get; set; } = true;

    /// <summary>Remove split containers left with a single child after a close/move (avoids i3's "useless nesting").</summary>
    public bool FlattenContainers { get; set; } = true;

    /// <summary>New windows split the focused window along its longer side (dwindle/spiral, like i3's autotiling script).</summary>
    public bool Autotiling { get; set; }

    /// <summary>Picks the output a new workspace should be created on (config "workspace X output Y").</summary>
    public Func<string, Node?>? WorkspaceOutputResolver { get; set; }
}

/// <summary>
/// The i3 container tree plus all operations on it. Pure logic: no OS calls, fully unit-testable.
/// The window manager mutates the tree and then renders it (layout + sync to real windows).
/// </summary>
public sealed class Tree
{
    private const double MinPercent = 0.03;

    public Tree(TreeOptions? options = null) => Options = options ?? new TreeOptions();

    public TreeOptions Options { get; set; }
    public Node Root { get; } = new(NodeKind.Root) { Name = "root" };

    /// <summary>The focused con: usually a window, a container after "focus parent", or an empty workspace.</summary>
    public Node? Focused { get; private set; }

    /// <summary>Hidden scratchpad windows (detached from the tree), least recently used first.</summary>
    public List<Node> Scratchpad { get; } = new();

    public string? PreviousWorkspaceName { get; set; }

    public IReadOnlyList<Node> Outputs => Root.Children;
    public IEnumerable<Node> Workspaces => Root.Children.SelectMany(o => o.Children);

    public Node? FocusedOutput
    {
        get
        {
            var o = Focused != null ? OutputOf(Focused) : null;
            return o ?? (Root.FocusStack.Count > 0 ? Root.FocusStack[0] : Root.Children.FirstOrDefault());
        }
    }

    public Node? FocusedWorkspace => (Focused != null ? WorkspaceOf(Focused) : null) ?? FocusedOutput?.CurrentWorkspace;

    /// <summary>The window that should hold the OS keyboard focus (descends into a focused container).</summary>
    public Node? FocusedWindow
    {
        get
        {
            if (Focused == null) return null;
            var d = DescendFocused(Focused);
            return d.Kind == NodeKind.Window ? d : null;
        }
    }

    // =====================================================================================
    // Queries
    // =====================================================================================

    public static Node? WorkspaceOf(Node? n)
    {
        while (n != null && n.Kind != NodeKind.Workspace) n = n.Parent;
        return n;
    }

    public static Node? OutputOf(Node? n)
    {
        while (n != null && n.Kind != NodeKind.Output) n = n.Parent;
        return n;
    }

    public static bool IsAncestor(Node ancestor, Node n)
    {
        for (var p = n.Parent; p != null; p = p.Parent)
            if (p == ancestor) return true;
        return false;
    }

    public bool IsAttached(Node n)
    {
        var c = n;
        while (c.Parent != null) c = c.Parent;
        return c == Root;
    }

    public static bool IsEmptyWorkspace(Node ws) => ws.Children.Count == 0 && ws.Floating.Count == 0;

    /// <summary>Follows the focus stacks down to the most recently focused leaf (i3's con_descend_focused).</summary>
    public static Node DescendFocused(Node n)
    {
        while (n.Kind != NodeKind.Window && n.FocusStack.Count > 0) n = n.FocusStack[0];
        return n;
    }

    /// <summary>Like <see cref="DescendFocused"/> but ignores floating windows.</summary>
    public static Node DescendTilingFocused(Node n)
    {
        while (n.Kind != NodeKind.Window)
        {
            Node? next = null;
            foreach (var c in n.FocusStack)
                if (!c.InFloating) { next = c; break; }
            if (next == null) break;
            n = next;
        }
        return n;
    }

    public static IEnumerable<Node> WindowsIn(Node n)
    {
        if (n.Kind == NodeKind.Window) { yield return n; yield break; }
        foreach (var c in n.Children)
            foreach (var w in WindowsIn(c)) yield return w;
        foreach (var f in n.Floating)
            foreach (var w in WindowsIn(f)) yield return w;
    }

    public IEnumerable<Node> AllWindows()
    {
        foreach (var ws in Workspaces)
            foreach (var w in WindowsIn(ws)) yield return w;
        foreach (var s in Scratchpad) yield return s;
    }

    public IEnumerable<Node> AllNodes()
    {
        var stack = new Stack<Node>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (int i = n.Floating.Count - 1; i >= 0; i--) stack.Push(n.Floating[i]);
            for (int i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
        foreach (var s in Scratchpad) yield return s;
    }

    public Node? FindWorkspace(string name) => Workspaces.FirstOrDefault(w => w.Name == name);

    public Node? FindWorkspaceByNumber(int number) => Workspaces.FirstOrDefault(w => w.Number == number);

    public int FirstFreeWorkspaceNumber()
    {
        var used = Workspaces.Select(w => w.Number).ToHashSet();
        int n = 1;
        while (used.Contains(n)) n++;
        return n;
    }

    public Node? FindOutput(string name)
    {
        if (name.Equals("primary", StringComparison.OrdinalIgnoreCase))
            return Outputs.FirstOrDefault(o => o.Primary) ?? Outputs.FirstOrDefault();
        var o = Outputs.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (o != null) return o;
        // Allow 1-based positional names ("1" = leftmost output).
        if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int idx) && idx >= 1 && idx <= Outputs.Count)
            return Outputs[idx - 1];
        return null;
    }

    public Node? FindMark(string mark) => AllNodes().FirstOrDefault(n => n.Marks.Contains(mark));

    /// <summary>All workspaces in i3 order: numbered ascending, then named ones by output order.</summary>
    public List<Node> OrderedWorkspaces()
    {
        var list = Workspaces.ToList();
        var numbered = list.Where(w => w.Number >= 0).OrderBy(w => w.Number).ThenBy(w => w.Name, StringComparer.Ordinal);
        return numbered.Concat(list.Where(w => w.Number < 0)).ToList();
    }

    public Node? OutputInDirection(Node from, Direction d)
    {
        var a = from.OutputRect;
        Node? best = null;
        (int noOverlap, long dist, long overlap) bestScore = (int.MaxValue, long.MaxValue, 0);
        foreach (var o in Outputs)
        {
            if (o == from) continue;
            var b = o.OutputRect;
            bool inDir;
            long dist, overlap;
            switch (d)
            {
                case Direction.Right:
                    inDir = b.Left >= a.Right - 16 || b.CenterX > a.Right;
                    dist = Math.Abs(b.Left - a.Right);
                    overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                    break;
                case Direction.Left:
                    inDir = b.Right <= a.Left + 16 || b.CenterX < a.Left;
                    dist = Math.Abs(a.Left - b.Right);
                    overlap = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
                    break;
                case Direction.Down:
                    inDir = b.Top >= a.Bottom - 16 || b.CenterY > a.Bottom;
                    dist = Math.Abs(b.Top - a.Bottom);
                    overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    break;
                default:
                    inDir = b.Bottom <= a.Top + 16 || b.CenterY < a.Top;
                    dist = Math.Abs(a.Top - b.Bottom);
                    overlap = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
                    break;
            }
            if (!inDir) continue;
            var score = (overlap > 0 ? 0 : 1, dist, -overlap);
            if (best == null || score.CompareTo(bestScore) < 0) { best = o; bestScore = score; }
        }
        return best;
    }

    // =====================================================================================
    // Outputs and workspaces
    // =====================================================================================

    public Node AddOutput(string name, Rect rect, Rect workArea, bool primary = false, int dpi = 96)
    {
        var o = new Node(NodeKind.Output)
        {
            Name = name, OutputRect = rect, WorkArea = workArea, LayoutArea = workArea,
            Primary = primary, Dpi = dpi, Parent = Root,
        };
        Root.Children.Add(o);
        Root.FocusStack.Add(o);
        SortOutputs();
        return o;
    }

    public void SortOutputs() => Root.Children.Sort((a, b) =>
    {
        int c = a.OutputRect.Left.CompareTo(b.OutputRect.Left);
        return c != 0 ? c : a.OutputRect.Top.CompareTo(b.OutputRect.Top);
    });

    /// <summary>Removes an output; its workspaces move to the primary (or first remaining) output.</summary>
    public void RemoveOutput(Node output)
    {
        var fallback = Outputs.FirstOrDefault(o => o != output && o.Primary) ?? Outputs.FirstOrDefault(o => o != output);
        if (fallback == null) return;
        bool focusWasHere = FocusedOutput == output;
        var wasCurrent = output.CurrentWorkspace;
        foreach (var ws in output.Children.ToList())
        {
            output.Children.Remove(ws);
            output.FocusStack.Remove(ws);
            if (IsEmptyWorkspace(ws)) { ws.Parent = null; continue; }
            foreach (var f in ws.Floating) TranslateFloating(f, output, fallback);
            ws.Parent = fallback;
            InsertWorkspaceSorted(fallback, ws);
            fallback.FocusStack.Add(ws);
        }
        output.CurrentWorkspace = null;
        Root.Children.Remove(output);
        Root.FocusStack.Remove(output);
        output.Parent = null;

        if (focusWasHere && wasCurrent != null && IsAttached(wasCurrent)) Focus(DescendFocused(wasCurrent));
        else if (Focused == null || !IsAttached(Focused)) Focus(DescendFocused(EnsureWorkspace(fallback)));
    }

    public LayoutKind DefaultLayoutFor(Node output) => Options.DefaultOrientation switch
    {
        "vertical" => LayoutKind.SplitV,
        "horizontal" => LayoutKind.SplitH,
        _ => output.OutputRect.Height > output.OutputRect.Width ? LayoutKind.SplitV : LayoutKind.SplitH,
    };

    public Node CreateWorkspace(string name, Node output)
    {
        var layout = DefaultLayoutFor(output);
        var ws = new Node(NodeKind.Workspace) { Name = name, Layout = layout, LastSplitLayout = layout, Parent = output };
        InsertWorkspaceSorted(output, ws);
        output.FocusStack.Add(ws);
        output.CurrentWorkspace ??= ws;
        return ws;
    }

    private static void InsertWorkspaceSorted(Node output, Node ws)
    {
        int idx = output.Children.Count;
        for (int i = 0; i < output.Children.Count; i++)
        {
            if (WorkspaceNames.Compare(ws, output.Children[i]) < 0) { idx = i; break; }
        }
        output.Children.Insert(idx, ws);
    }

    public Node GetOrCreateWorkspace(string name, Node? output = null)
    {
        var ws = FindWorkspace(name);
        if (ws != null) return ws;
        output ??= Options.WorkspaceOutputResolver?.Invoke(name) ?? FocusedOutput ?? Outputs[0];
        return CreateWorkspace(name, output);
    }

    /// <summary>Makes sure the output shows a workspace (creating or adopting one if needed).</summary>
    public Node EnsureWorkspace(Node output, string? preferredName = null)
    {
        if (output.CurrentWorkspace != null && output.CurrentWorkspace.Parent == output) return output.CurrentWorkspace;
        output.CurrentWorkspace = null;
        if (output.Children.Count > 0)
        {
            output.CurrentWorkspace = output.FocusStack.FirstOrDefault() ?? output.Children[0];
            return output.CurrentWorkspace;
        }
        string name = preferredName ?? FirstFreeWorkspaceNumber().ToString(CultureInfo.InvariantCulture);
        var existing = FindWorkspace(name);
        if (existing != null)
        {
            var src = existing.Parent!;
            if (src.CurrentWorkspace != existing)
            {
                src.Children.Remove(existing);
                src.FocusStack.Remove(existing);
                foreach (var f in existing.Floating) TranslateFloating(f, src, output);
                existing.Parent = output;
                InsertWorkspaceSorted(output, existing);
                output.FocusStack.Add(existing);
                output.CurrentWorkspace = existing;
                return existing;
            }
            name = FirstFreeWorkspaceNumber().ToString(CultureInfo.InvariantCulture);
        }
        var ws = CreateWorkspace(name, output);
        output.CurrentWorkspace = ws;
        return ws;
    }

    private void RemoveWorkspace(Node ws)
    {
        var o = ws.Parent;
        if (o == null || o.CurrentWorkspace == ws) return;
        o.Children.Remove(ws);
        o.FocusStack.Remove(ws);
        ws.Parent = null;
    }

    private void RemoveIfEmptyAndHidden(Node? ws)
    {
        if (ws?.Parent != null && IsEmptyWorkspace(ws) && ws.Parent.CurrentWorkspace != ws && Focused != ws)
            RemoveWorkspace(ws);
    }

    /// <summary>Shows the workspace on its output and focuses its last focused con.</summary>
    public void ShowWorkspace(Node ws) => Focus(DescendFocused(ws));

    private void MakeVisible(Node ws)
    {
        var o = ws.Parent!;
        var old = o.CurrentWorkspace;
        if (old == ws) return;
        o.CurrentWorkspace = ws;
        if (old == null) return;
        // Sticky floating windows follow the visible workspace of their output.
        foreach (var s in old.Floating.Where(f => f.Sticky).ToList())
        {
            DetachRaw(s);
            AttachFloating(s, ws, front: false);
        }
        if (IsEmptyWorkspace(old)) RemoveWorkspace(old);
    }

    public void RenameWorkspace(Node ws, string newName)
    {
        var o = ws.Parent!;
        ws.Name = newName;
        o.Children.Remove(ws);
        InsertWorkspaceSorted(o, ws);
    }

    public void MoveWorkspaceToOutput(Node ws, Node target)
    {
        var src = ws.Parent;
        if (src == null || src == target) return;
        bool wasVisible = src.CurrentWorkspace == ws;
        src.Children.Remove(ws);
        src.FocusStack.Remove(ws);
        if (wasVisible)
        {
            src.CurrentWorkspace = null;
            EnsureWorkspace(src);
        }
        foreach (var f in ws.Floating) TranslateFloating(f, src, target);
        ws.Parent = target;
        InsertWorkspaceSorted(target, ws);
        target.FocusStack.Insert(0, ws);
        Focus(DescendFocused(ws));
    }

    public static void TranslateFloating(Node f, Node from, Node to)
    {
        if (f.FloatRect is not Rect r) return;
        var a = from.LayoutArea;
        var b = to.LayoutArea;
        double rx = a.Width > 0 ? (double)(r.Left - a.Left) / a.Width : 0;
        double ry = a.Height > 0 ? (double)(r.Top - a.Top) / a.Height : 0;
        int w = Math.Min(r.Width, b.Width), h = Math.Min(r.Height, b.Height);
        var moved = Rect.FromSize(b.Left + (int)(rx * b.Width), b.Top + (int)(ry * b.Height), w, h);
        f.FloatRect = moved.ClampInto(b);
    }

    // =====================================================================================
    // Low-level attach / detach
    // =====================================================================================

    private static void AttachTiling(Node child, Node parent, int index)
    {
        child.Parent = parent;
        child.InFloating = false;
        parent.Children.Insert(Math.Clamp(index, 0, parent.Children.Count), child);
        parent.FocusStack.Add(child);
        child.Percent = 0;
        FixPercent(parent);
    }

    private static void AttachFloating(Node win, Node ws, bool front)
    {
        win.Parent = ws;
        win.InFloating = true;
        ws.Floating.Add(win);
        if (front) ws.FocusStack.Insert(0, win); else ws.FocusStack.Add(win);
    }

    private static void DetachRaw(Node n)
    {
        var p = n.Parent;
        if (p == null) return;
        bool floating = n.InFloating;
        if (floating) p.Floating.Remove(n); else p.Children.Remove(n);
        p.FocusStack.Remove(n);
        n.Parent = null;
        n.InFloating = false;
        if (!floating) FixPercent(p);
    }

    /// <summary>Puts <paramref name="replacement"/> into <paramref name="old"/>'s slot (children, focus stack and percent).</summary>
    private static void ReplaceInParent(Node old, Node replacement)
    {
        var p = old.Parent!;
        int ci = p.Children.IndexOf(old);
        int fi = p.FocusStack.IndexOf(old);
        p.Children[ci] = replacement;
        if (fi >= 0) p.FocusStack[fi] = replacement; else p.FocusStack.Add(replacement);
        replacement.Parent = p;
        replacement.InFloating = false;
        replacement.Percent = old.Percent;
        old.Parent = null;
    }

    /// <summary>Port of i3's con_fix_percent: gives new (0%) children an average share and normalizes to 1.</summary>
    public static void FixPercent(Node con)
    {
        var kids = con.Children;
        int n = kids.Count;
        if (n == 0) return;
        double total = 0;
        int withPct = 0;
        foreach (var k in kids)
            if (k.Percent > 0) { total += k.Percent; withPct++; }
        if (withPct != n)
        {
            double share = withPct == 0 ? 1.0 : total / withPct;
            foreach (var k in kids)
                if (k.Percent <= 0) { k.Percent = share; total += share; }
        }
        if (total <= 0)
            foreach (var k in kids) k.Percent = 1.0 / n;
        else if (Math.Abs(total - 1.0) > 1e-9)
            foreach (var k in kids) k.Percent /= total;
    }

    /// <summary>Removes empty containers upwards and (optionally) flattens single-child split containers.</summary>
    private void CleanupAfterRemoval(Node? con)
    {
        while (con != null && con.Kind == NodeKind.Container)
        {
            var parent = con.Parent;
            if (parent == null) return;
            if (con.Children.Count == 0)
            {
                DetachRaw(con);
                con = parent;
                continue;
            }
            if (con.Children.Count == 1 && Options.FlattenContainers && con.IsSplit)
                CollapseSingle(con);
            break;
        }
        var ws = con?.Kind == NodeKind.Workspace ? con : WorkspaceOf(con);
        if (ws != null && Options.FlattenContainers) AbsorbSingleSplitChild(ws);
    }

    private void CollapseSingle(Node con)
    {
        var child = con.Children[0];
        var parent = con.Parent!;
        con.Children.Clear();
        con.FocusStack.Clear();
        ReplaceInParent(con, child);
        if (Focused == con) Focused = child;
        if (child.Kind == NodeKind.Container && child.IsSplit && parent.IsSplit && child.Layout == parent.Layout)
            MergeIntoParent(child);
    }

    /// <summary>Replaces a split container by its children (parent has the same orientation, so geometry is unchanged).</summary>
    private void MergeIntoParent(Node con)
    {
        var parent = con.Parent!;
        int ci = parent.Children.IndexOf(con);
        int fi = parent.FocusStack.IndexOf(con);
        var kids = con.Children.ToList();
        var focusOrder = con.FocusStack.ToList();
        double pct = con.Percent;
        parent.Children.RemoveAt(ci);
        if (fi >= 0) parent.FocusStack.RemoveAt(fi); else fi = parent.FocusStack.Count;
        for (int i = 0; i < kids.Count; i++)
        {
            kids[i].Parent = parent;
            kids[i].Percent *= pct;
            parent.Children.Insert(ci + i, kids[i]);
        }
        parent.FocusStack.InsertRange(Math.Min(fi, parent.FocusStack.Count), focusOrder);
        con.Children.Clear();
        con.FocusStack.Clear();
        con.Parent = null;
        if (Focused == con) Focused = DescendFocused(focusOrder[0]);
        FixPercent(parent);
    }

    /// <summary>A workspace whose only tiling child is a split container takes over that container's children.</summary>
    private void AbsorbSingleSplitChild(Node ws)
    {
        if (ws.Children.Count != 1) return;
        var child = ws.Children[0];
        if (child.Kind != NodeKind.Container || !child.IsSplit) return;
        ws.Layout = child.Layout;
        ws.LastSplitLayout = child.Layout;
        child.Percent = 1.0;
        MergeIntoParent(child);
    }

    /// <summary>i3's tree_flatten: removes redundant pairs of split containers created by directional moves.</summary>
    public void FlattenRedundant(Node con)
    {
        foreach (var c in con.Children.ToList()) FlattenRedundant(c);
        if (con.Kind != NodeKind.Container || con.Parent == null || con.Children.Count != 1) return;
        var child = con.Children[0];
        var parent = con.Parent;
        if (child.Kind != NodeKind.Container || !con.IsSplit || !child.IsSplit) return;
        if (con.Orientation == child.Orientation || child.Orientation != parent.Orientation) return;
        // con's only child has the parent's orientation: both levels are redundant.
        con.Children.Clear();
        con.FocusStack.Clear();
        ReplaceInParent(con, child);
        if (Focused == con) Focused = child;
        if (parent.IsSplit && parent.Layout == child.Layout) MergeIntoParent(child);
    }

    // =====================================================================================
    // Focus
    // =====================================================================================

    /// <summary>Focuses a con, making its workspace visible on its output (i3's workspace_show + con_activate).</summary>
    public void Focus(Node n)
    {
        if (!IsAttached(n)) return;
        var prevWs = FocusedWorkspace;
        var ws = n.Kind == NodeKind.Workspace ? n : WorkspaceOf(n);
        if (ws?.Parent is { } output && output.CurrentWorkspace != ws) MakeVisible(ws);
        Focused = n;
        for (var c = n; c.Parent != null; c = c.Parent) PromoteInParent(c);
        if (n.Kind == NodeKind.Window) n.Urgent = false;
        if (prevWs != null && ws != null && prevWs != ws) PreviousWorkspaceName = prevWs.Name;
        var nowWindow = FocusedWindow;
        if (nowWindow != null && nowWindow != _lastWindow)
        {
            if (_lastWindow != null) PreviousWindow = _lastWindow;
            _lastWindow = nowWindow;
        }
    }

    private Node? _lastWindow;

    /// <summary>The window that had the focus before the current one ("focus last").</summary>
    public Node? PreviousWindow { get; private set; }

    /// <summary>Mozaic extension "focus last": jumps back to the previously focused window (any workspace).</summary>
    public bool FocusLast()
    {
        var prev = PreviousWindow;
        if (prev == null || prev.Parent == null || !IsAttached(prev)) return false;
        Focus(prev);
        return true;
    }

    private static void PromoteInParent(Node c)
    {
        var fs = c.Parent!.FocusStack;
        int i = fs.IndexOf(c);
        if (i == 0) return;
        if (i > 0) fs.RemoveAt(i);
        fs.Insert(0, c);
    }

    /// <summary>Makes <paramref name="n"/> the most recent focus inside its workspace without moving global focus.</summary>
    public static void PromoteWithinWorkspace(Node n)
    {
        for (var c = n; c.Parent != null && c.Kind != NodeKind.Workspace; c = c.Parent) PromoteInParent(c);
    }

    private Node NextFocusAfterRemoval(Node n)
    {
        var p = n.Parent!;
        if (n.InFloating)
        {
            var cand = p.FocusStack.FirstOrDefault(x => x != n);
            return cand != null ? DescendFocused(cand) : p;
        }
        var sib = p.FocusStack.FirstOrDefault(x => x != n && !x.InFloating);
        if (sib != null) return DescendFocused(sib);
        if (p.Kind == NodeKind.Workspace)
        {
            var f = p.FocusStack.FirstOrDefault(x => x != n);
            return f != null ? DescendFocused(f) : p;
        }
        return NextFocusAfterRemoval(p);
    }

    public bool FocusDirection(Direction dir)
    {
        var con = Focused;
        if (con == null) return false;
        if (con.Fullscreen) con.Fullscreen = false;
        if (con.Kind == NodeKind.Workspace) return FocusOutputDirection(OutputOf(con)!, dir);
        if (con.InFloating)
        {
            if (dir.ToOrientation() != Orientation.Horizontal) return false;
            var list = con.Parent!.Floating;
            if (list.Count < 2) return false;
            int i = list.IndexOf(con);
            var next = dir == Direction.Right ? list[(i + 1) % list.Count] : list[(i - 1 + list.Count) % list.Count];
            Focus(next);
            return true;
        }
        return TreeNext(con, dir, Options.FocusWrapping);
    }

    private bool TreeNext(Node con, Direction dir, bool wrap)
    {
        if (con.Kind == NodeKind.Workspace) return FocusOutputDirection(OutputOf(con)!, dir);
        var parent = con.Parent!;
        if (parent.Orientation != dir.ToOrientation() || parent.Children.Count == 1)
            return TreeNext(parent, dir, wrap);

        int idx = parent.Children.IndexOf(con);
        bool forward = !dir.IsBackward();
        Node? next = forward
            ? (idx + 1 < parent.Children.Count ? parent.Children[idx + 1] : null)
            : (idx > 0 ? parent.Children[idx - 1] : null);
        if (next == null)
        {
            // Try going higher first (e.g. to another output); wrap only if that fails.
            if (TreeNext(parent, dir, false)) return true;
            if (!wrap) return false;
            next = forward ? parent.Children[0] : parent.Children[^1];
            if (next == con) return false;
        }
        Focus(DescendFocused(next));
        return true;
    }

    public bool FocusOutputDirection(Node output, Direction dir)
    {
        var next = OutputInDirection(output, dir);
        if (next?.CurrentWorkspace == null) return false;
        Focus(DescendFocused(next.CurrentWorkspace));
        return true;
    }

    public bool FocusOutput(Node output)
    {
        var ws = EnsureWorkspace(output);
        Focus(DescendFocused(ws));
        return true;
    }

    public bool FocusParent()
    {
        var f = Focused;
        if (f == null || f.Kind == NodeKind.Workspace || f.InFloating || f.Parent == null) return false;
        if (f.Parent.Kind is not (NodeKind.Container or NodeKind.Workspace)) return false;
        f.Fullscreen = false;
        Focus(f.Parent);
        return true;
    }

    public bool FocusChild()
    {
        var f = Focused;
        if (f == null || f.Kind == NodeKind.Window || f.FocusStack.Count == 0) return false;
        Focus(f.FocusStack[0]);
        return true;
    }

    /// <summary>"focus mode_toggle" / "focus floating" / "focus tiling".</summary>
    public bool FocusModeToggle(bool? toFloating = null)
    {
        var ws = FocusedWorkspace;
        if (ws == null) return false;
        bool inFloating = Focused?.InFloating == true;
        bool wantFloating = toFloating ?? !inFloating;
        if (wantFloating)
        {
            var f = ws.FocusStack.FirstOrDefault(x => x.InFloating);
            if (f == null) return false;
            Focus(f);
            return true;
        }
        var t = DescendTilingFocused(ws);
        if (t == ws && ws.Children.Count == 0) return false;
        Focus(t);
        return true;
    }

    /// <summary>"focus next|prev [sibling]": cycles through siblings regardless of orientation.</summary>
    public bool FocusSibling(bool forward, bool siblingOnly)
    {
        var f = Focused;
        if (f?.Parent == null || f.Kind == NodeKind.Workspace) return false;
        var list = f.InFloating ? f.Parent.Floating : f.Parent.Children;
        if (list.Count < 2)
        {
            if (siblingOnly || f.Parent.Kind == NodeKind.Workspace) return false;
            var saved = Focused;
            Focused = f.Parent;
            bool ok = FocusSibling(forward, false);
            if (!ok) Focused = saved;
            return ok;
        }
        int i = list.IndexOf(f);
        var next = list[(i + (forward ? 1 : -1) + list.Count) % list.Count];
        Focus(siblingOnly ? next : DescendFocused(next));
        return true;
    }

    // =====================================================================================
    // Inserting and removing windows
    // =====================================================================================

    /// <summary>Inserts a new window following i3's rules: after the focused con of the target workspace.</summary>
    public void AddWindow(Node win, Node ws, bool floating)
    {
        if (floating) AttachFloating(win, ws, front: false);
        else InsertTiling(win, ws);
    }

    private void InsertTiling(Node con, Node ws)
    {
        Node? anchor = null;
        bool focusedHere = Focused != null && WorkspaceOf(Focused) == ws;
        if (focusedHere && Focused!.Kind != NodeKind.Workspace && !Focused.InFloating && Focused != con && !IsAncestor(con, Focused))
        {
            anchor = Focused;
        }
        else if (!(focusedHere && Focused!.Kind == NodeKind.Workspace))
        {
            var t = DescendTilingFocused(ws);
            if (t != ws) anchor = t;
        }

        if (anchor == null)
        {
            var wl = ws.WorkspaceLayout ?? Options.WorkspaceLayout;
            if (ws.Children.Count == 0 && wl is LayoutKind.Stacked or LayoutKind.Tabbed && con.Kind == NodeKind.Window)
            {
                var c = new Node(NodeKind.Container) { Layout = wl.Value, LastSplitLayout = ws.Layout };
                AttachTiling(c, ws, 0);
                AttachTiling(con, c, 0);
                return;
            }
            AttachTiling(con, ws, ws.Children.Count);
            return;
        }
        if (Options.Autotiling && anchor.Kind == NodeKind.Window && anchor.Parent is { IsSplit: true } ap && !anchor.Rect.IsEmpty)
        {
            var want = anchor.Rect.Width > anchor.Rect.Height ? Orientation.Horizontal : Orientation.Vertical;
            if (ap.Orientation != want) Split(want, anchor);
        }
        var p = anchor.Parent!;
        AttachTiling(con, p, p.Children.IndexOf(anchor) + 1);
    }

    /// <summary>Removes a window (closed, hidden, minimized) and moves focus like i3 does.</summary>
    public void Remove(Node n)
    {
        if (n.Parent == null)
        {
            Scratchpad.Remove(n);
            return;
        }
        bool focusAffected = Focused != null && (Focused == n || IsAncestor(n, Focused));
        var next = focusAffected ? NextFocusAfterRemoval(n) : null;
        var parent = n.Parent;
        var ws = WorkspaceOf(n);
        bool floating = n.InFloating;
        DetachRaw(n);
        if (!floating) CleanupAfterRemoval(parent);

        if (focusAffected || Focused == null || !IsAttached(Focused))
        {
            Node? target = next != null && IsAttached(next) ? next : null;
            if (target == null && ws != null && IsAttached(ws)) target = DescendFocused(ws);
            if (target == null && FocusedOutput is { } fo) target = DescendFocused(EnsureWorkspace(fo));
            if (target != null)
            {
                // Removing a window on a hidden workspace must not switch workspaces.
                if (focusAffected) Focus(target);
                else if (Focused == null || !IsAttached(Focused)) Focus(target);
            }
        }
        RemoveIfEmptyAndHidden(ws);
    }

    // =====================================================================================
    // Moving
    // =====================================================================================

    private static Node? ParentWithOrientation(Node con, Orientation o)
    {
        var parent = con.Parent;
        if (parent == null || con.InFloating) return null;
        while (parent != null && parent.Orientation != o)
        {
            if (parent.Kind == NodeKind.Workspace) return null;
            parent = parent.Parent;
            if (parent == null || parent.Kind is NodeKind.Output or NodeKind.Root) return null;
        }
        return parent;
    }

    /// <summary>i3's con_descend_direction: the con inside <paramref name="con"/> nearest to where we enter from.</summary>
    private static Node DescendDirection(Node con, Direction dir)
    {
        while (true)
        {
            var o = con.Orientation;
            if (o == Orientation.None || con.Children.Count == 0) return con;
            Node? most;
            if (o == dir.ToOrientation())
            {
                // Entering a container of the same orientation: take the edge child facing us.
                most = dir.IsBackward() ? con.Children[^1] : con.Children[0];
            }
            else
            {
                most = con.FocusStack.FirstOrDefault(c => !c.InFloating);
            }
            if (most == null) return con;
            con = most;
        }
    }

    /// <summary>Port of i3's tree_move: moves the focused con in a direction, entering/leaving containers.</summary>
    public bool MoveDirection(Direction dir)
    {
        var con = Focused;
        if (con == null || con.Kind == NodeKind.Workspace || con.InFloating || con.Parent == null) return false;
        con.Fullscreen = false;
        var ws = WorkspaceOf(con)!;
        var o = dir.ToOrientation();

        if (con.Parent.Kind == NodeKind.Workspace && con.Parent.Children.Count == 1)
            return MoveToOutputDirected(con, dir);

        var same = ParentWithOrientation(con, o);
        while (true)
        {
            if (same == null)
            {
                ForceOrientation(ws, o);
                same = ParentWithOrientation(con, o);
                if (same == null) return false;
            }
            if (same == con.Parent)
            {
                int idx = same.Children.IndexOf(con);
                int swapIdx = dir.IsBackward() ? idx - 1 : idx + 1;
                if (swapIdx >= 0 && swapIdx < same.Children.Count)
                {
                    var swap = same.Children[swapIdx];
                    if (swap.Kind != NodeKind.Window)
                    {
                        var target = DescendDirection(swap, dir);
                        bool after = target.Parent!.Orientation != o || dir.IsBackward();
                        InsertConInto(con, target, after);
                        FinishMove(con, ws);
                        return true;
                    }
                    same.Children[idx] = swap;
                    same.Children[swapIdx] = con;
                    return true;
                }
                if (con.Parent.Kind == NodeKind.Workspace)
                    return MoveToOutputDirected(con, dir);
                same = ParentWithOrientation(con.Parent, o);
                if (same == null) continue;
            }
            break;
        }

        // Move out to an ancestor container: 'above' is con's ancestor inside 'same'.
        var above = con;
        while (above.Parent != same) above = above.Parent!;
        int aIdx = same.Children.IndexOf(above);
        int nIdx = dir.IsBackward() ? aIdx - 1 : aIdx + 1;
        var nextCon = nIdx >= 0 && nIdx < same.Children.Count ? same.Children[nIdx] : null;
        if (nextCon != null && nextCon.Kind != NodeKind.Window)
        {
            var target = DescendDirection(nextCon, dir);
            bool after = target.Parent!.Orientation != o || dir.IsBackward();
            InsertConInto(con, target, after);
        }
        else if (nextCon == null && con.Parent!.Parent?.Kind == NodeKind.Workspace && con.Parent.Children.Count == 1)
        {
            // Lone child of a container at the workspace edge: behave as if the workspace were its parent.
            return MoveToOutputDirected(con, dir);
        }
        else
        {
            InsertConInto(con, above, after: !dir.IsBackward());
        }
        FinishMove(con, ws);
        return true;
    }

    private void FinishMove(Node con, Node ws)
    {
        FlattenRedundant(ws);
        if (Options.FlattenContainers) AbsorbSingleSplitChild(ws);
        if (IsAttached(con)) Focus(con);
    }

    /// <summary>i3's ws_force_orientation: wraps the workspace's children so the workspace gets orientation o.</summary>
    private void ForceOrientation(Node ws, Orientation o)
    {
        var split = Encapsulate(ws, ws.Layout);
        ws.Layout = o.ToSplit();
        ws.LastSplitLayout = ws.Layout;
        split.Percent = 1.0;
    }

    /// <summary>Wraps all tiling children of a workspace into one new container (i3's workspace_encapsulate).</summary>
    private static Node Encapsulate(Node ws, LayoutKind layout)
    {
        var c = new Node(NodeKind.Container) { Layout = layout, LastSplitLayout = ws.LastSplitLayout, Percent = 1.0 };
        int firstTilingFocus = ws.FocusStack.FindIndex(x => !x.InFloating);
        var focusOrder = ws.FocusStack.Where(x => !x.InFloating).ToList();
        foreach (var k in ws.Children)
        {
            k.Parent = c;
            c.Children.Add(k);
        }
        c.FocusStack.AddRange(focusOrder);
        ws.Children.Clear();
        ws.FocusStack.RemoveAll(x => !x.InFloating);
        c.Parent = ws;
        ws.Children.Add(c);
        int at = firstTilingFocus < 0 ? ws.FocusStack.Count : Math.Min(firstTilingFocus, ws.FocusStack.Count);
        ws.FocusStack.Insert(at, c);
        return c;
    }

    private void InsertConInto(Node con, Node target, bool after)
    {
        if (target == con || IsAncestor(con, target)) return;
        var oldParent = con.Parent!;
        DetachRaw(con);
        var parent = target.Parent!;
        int idx = parent.Children.IndexOf(target);
        con.Parent = parent;
        con.InFloating = false;
        parent.Children.Insert(after ? idx + 1 : idx, con);
        parent.FocusStack.Insert(0, con);
        con.Percent = 0;
        FixPercent(parent);
        if (oldParent != parent) CleanupAfterRemoval(oldParent);
    }

    private bool MoveToOutputDirected(Node con, Direction dir)
    {
        var output = OutputOf(con);
        if (output == null) return false;
        var next = OutputInDirection(output, dir);
        if (next == null) return false;
        var ws = EnsureWorkspace(next);
        AttachToWorkspaceEdge(con, ws, dir);
        return true;
    }

    private void AttachToWorkspaceEdge(Node con, Node ws, Direction dir)
    {
        var srcWs = WorkspaceOf(con);
        var oldParent = con.Parent!;
        bool wasFocused = Focused == con;
        var next = wasFocused ? NextFocusAfterRemoval(con) : null;
        DetachRaw(con);
        CleanupAfterRemoval(oldParent);
        con.Parent = ws;
        con.InFloating = false;
        if (dir.IsBackward()) ws.Children.Add(con); else ws.Children.Insert(0, con);
        ws.FocusStack.Insert(0, con);
        con.Percent = 0;
        FixPercent(ws);
        if (wasFocused) Focus(con);
        else if (next != null && IsAttached(next) && Focused != null && !IsAttached(Focused)) Focus(next);
        RemoveIfEmptyAndHidden(srcWs);
    }

    /// <summary>"move container to workspace": appends after the target workspace's focused con.</summary>
    public void MoveToWorkspace(Node con, Node ws)
    {
        if (con.Kind is not (NodeKind.Window or NodeKind.Container)) return;
        var srcWs = WorkspaceOf(con);
        if (srcWs == null || srcWs == ws) return;
        bool focusAffected = Focused != null && (Focused == con || IsAncestor(con, Focused));
        var next = focusAffected ? NextFocusAfterRemoval(con) : null;
        var srcOut = srcWs.Parent!;
        var dstOut = ws.Parent!;
        bool floating = con.InFloating;
        var oldParent = con.Parent!;

        DetachRaw(con);
        if (!floating) CleanupAfterRemoval(oldParent);
        foreach (var w in WindowsIn(con)) w.Fullscreen = false;

        if (floating)
        {
            if (srcOut != dstOut) TranslateFloating(con, srcOut, dstOut);
            AttachFloating(con, ws, front: true);
        }
        else
        {
            InsertTiling(con, ws);
        }
        PromoteWithinWorkspace(con);

        if (focusAffected)
        {
            Node target = next != null && IsAttached(next) && WorkspaceOf(next) == srcWs ? next : DescendFocused(srcWs);
            if (!IsAttached(target)) target = DescendFocused(EnsureWorkspace(srcOut));
            Focus(target);
        }
        RemoveIfEmptyAndHidden(srcWs);
    }

    /// <summary>Drag-and-drop helper: puts <paramref name="con"/> next to <paramref name="target"/> on the given side.</summary>
    public void MoveNextTo(Node con, Node target, Direction side)
    {
        if (con == target || IsAncestor(con, target) || target.Parent == null || target.InFloating) return;
        var srcWs = WorkspaceOf(con);
        bool wasFloating = con.InFloating;
        var oldParent = con.Parent;
        if (oldParent != null)
        {
            DetachRaw(con);
            if (!wasFloating) CleanupAfterRemoval(oldParent);
        }
        con.Fullscreen = false;
        var o = side.ToOrientation();
        var p = target.Parent!;
        if (p.IsSplit && p.Children.Count == 1 && p.Orientation != o)
        {
            // Like i3's split: a lone child just changes its parent's orientation (no extra nesting).
            p.Layout = o.ToSplit();
            p.LastSplitLayout = p.Layout;
        }
        if (p.IsSplit && p.Orientation == o)
        {
            int i = p.Children.IndexOf(target);
            AttachTiling(con, p, side.IsBackward() ? i : i + 1);
        }
        else
        {
            var split = new Node(NodeKind.Container) { Layout = o.ToSplit(), LastSplitLayout = o.ToSplit() };
            ReplaceInParent(target, split);
            target.Parent = split;
            target.Percent = 1.0;
            split.Children.Add(target);
            split.FocusStack.Add(target);
            AttachTiling(con, split, side.IsBackward() ? 0 : 1);
        }
        Focus(con);
        RemoveIfEmptyAndHidden(srcWs);
    }

    public bool Swap(Node a, Node b)
    {
        if (a == b || a.Parent == null || b.Parent == null || a.InFloating || b.InFloating) return false;
        if (IsAncestor(a, b) || IsAncestor(b, a)) return false;
        var focusedWsBefore = FocusedWorkspace;
        var pa = a.Parent;
        var pb = b.Parent;
        int ia = pa.Children.IndexOf(a), ib = pb.Children.IndexOf(b);
        int fa = pa.FocusStack.IndexOf(a), fb = pb.FocusStack.IndexOf(b);
        (a.Percent, b.Percent) = (b.Percent, a.Percent);
        if (pa == pb)
        {
            pa.Children[ia] = b;
            pa.Children[ib] = a;
        }
        else
        {
            pa.Children[ia] = b;
            pb.Children[ib] = a;
            pa.FocusStack[fa] = b;
            pb.FocusStack[fb] = a;
            a.Parent = pb;
            b.Parent = pa;
        }
        foreach (var w in WindowsIn(a).Concat(WindowsIn(b))) w.Fullscreen = false;
        if (Focused != null && (Focused == a || Focused == b || IsAncestor(a, Focused) || IsAncestor(b, Focused)))
        {
            var f = Focused;
            if (WorkspaceOf(f) != focusedWsBefore) f = f == a || IsAncestor(a, f) ? b : a;
            Focus(DescendFocused(f));
        }
        return true;
    }

    // =====================================================================================
    // Split / layout
    // =====================================================================================

    /// <summary>Port of i3's tree_split.</summary>
    public void Split(Orientation orientation, Node? con = null)
    {
        con ??= Focused;
        if (con == null || con.InFloating || con.Kind is NodeKind.Root or NodeKind.Output) return;
        var layout = orientation.ToSplit();
        if (con.Kind == NodeKind.Workspace)
        {
            if (con.Children.Count < 2)
            {
                con.Layout = layout;
                con.LastSplitLayout = layout;
                return;
            }
            con = Encapsulate(con, con.Layout);
        }
        var parent = con.Parent!;
        if (parent.Children.Count == 1 && parent.IsSplit)
        {
            parent.Layout = layout;
            parent.LastSplitLayout = layout;
            return;
        }
        var split = new Node(NodeKind.Container) { Layout = layout, LastSplitLayout = layout };
        ReplaceInParent(con, split);
        con.Parent = split;
        con.Percent = 1.0;
        split.Children.Add(con);
        split.FocusStack.Add(con);
    }

    private static Node? LayoutTarget(Node con) =>
        con.Kind == NodeKind.Workspace ? con : con.Parent is { Kind: NodeKind.Container or NodeKind.Workspace } p ? p : null;

    /// <summary>Port of i3's con_set_layout: changes the layout of the focused con's parent.</summary>
    public void SetLayout(LayoutKind layout, Node? con = null)
    {
        con ??= Focused;
        if (con == null || con.InFloating) return;
        var target = LayoutTarget(con);
        if (target == null) return;
        if (target.IsSplit) target.LastSplitLayout = target.Layout;

        if (target.Kind == NodeKind.Workspace)
        {
            if (target.Children.Count == 0)
            {
                if (layout is LayoutKind.Stacked or LayoutKind.Tabbed) target.WorkspaceLayout = layout;
                else { target.WorkspaceLayout = null; target.Layout = layout; target.LastSplitLayout = layout; }
                return;
            }
            if (layout is LayoutKind.Stacked or LayoutKind.Tabbed)
            {
                var c = Encapsulate(target, layout);
                c.LastSplitLayout = target.LastSplitLayout;
                FlattenRedundant(target);
                return;
            }
            target.Layout = layout;
            target.LastSplitLayout = layout;
            return;
        }
        target.Layout = layout;
        if (target.IsSplit) target.LastSplitLayout = layout;
        var ws = WorkspaceOf(target);
        if (ws != null && Options.FlattenContainers) AbsorbSingleSplitChild(ws);
    }

    public void SetLayoutDefault(Node? con = null)
    {
        con ??= Focused;
        if (con == null) return;
        var target = LayoutTarget(con);
        if (target == null) return;
        SetLayout(target.IsSplit ? target.Layout : target.LastSplitLayout, con);
    }

    /// <summary>"layout toggle [split|all|default|layout1 layout2 ...]".</summary>
    public void ToggleLayout(IReadOnlyList<string> modes, Node? con = null)
    {
        con ??= Focused;
        if (con == null || con.InFloating) return;
        var parent = LayoutTarget(con);
        if (parent == null) return;
        if (modes.Count == 0) modes = new[] { "default" };

        if (modes.Count == 1 && modes[0] is "all" or "default")
        {
            bool all = modes[0] == "all";
            switch (parent.Layout)
            {
                case LayoutKind.Stacked: SetLayout(LayoutKind.Tabbed, con); break;
                case LayoutKind.Tabbed: SetLayout(all ? LayoutKind.SplitH : parent.LastSplitLayout, con); break;
                case LayoutKind.SplitH when all: SetLayout(LayoutKind.SplitV, con); break;
                default: SetLayout(LayoutKind.Stacked, con); break;
            }
            return;
        }

        LayoutKind ToggleSplitOf(Node p) => p.IsSplit
            ? (p.Layout == LayoutKind.SplitH ? LayoutKind.SplitV : LayoutKind.SplitH)
            : p.LastSplitLayout;

        if (modes.Count == 1 && modes[0] == "split")
        {
            SetLayout(ToggleSplitOf(parent), con);
            return;
        }

        // Cycle through an explicit list of layouts.
        var list = new List<LayoutKind>();
        foreach (var m in modes)
        {
            if (m == "split") list.Add(ToggleSplitOf(parent));
            else if (TryParseLayout(m, out var l)) list.Add(l);
        }
        if (list.Count == 0) return;
        int cur = list.IndexOf(parent.Layout);
        SetLayout(list[(cur + 1) % list.Count], con);
    }

    public static bool TryParseLayout(string s, out LayoutKind layout)
    {
        switch (s.ToLowerInvariant())
        {
            case "splith": layout = LayoutKind.SplitH; return true;
            case "splitv": layout = LayoutKind.SplitV; return true;
            case "stacked":
            case "stacking": layout = LayoutKind.Stacked; return true;
            case "tabbed": layout = LayoutKind.Tabbed; return true;
            default: layout = LayoutKind.SplitH; return false;
        }
    }

    public static string LayoutName(LayoutKind l) => l switch
    {
        LayoutKind.SplitH => "splith",
        LayoutKind.SplitV => "splitv",
        LayoutKind.Stacked => "stacked",
        _ => "tabbed",
    };

    // =====================================================================================
    // Resize
    // =====================================================================================

    /// <summary>i3's resize_find_tiling_participants.</summary>
    public static (Node? First, Node? Second) FindResizeParticipants(Node con, Direction dir, bool bothSides)
    {
        var first = con;
        Node? second = null;
        var o = dir.ToOrientation();
        while (first.Kind != NodeKind.Workspace && !first.InFloating && second == null)
        {
            var p = first.Parent;
            if (p == null) return (null, null);
            if (p.Orientation != o || p.Layout is LayoutKind.Stacked or LayoutKind.Tabbed)
            {
                first = p;
                continue;
            }
            int i = p.Children.IndexOf(first);
            Node? prev = i > 0 ? p.Children[i - 1] : null;
            Node? next = i + 1 < p.Children.Count ? p.Children[i + 1] : null;
            second = dir.IsBackward() ? prev ?? (bothSides ? next : null) : next ?? (bothSides ? prev : null);
            if (second == null) first = p;
        }
        return second == null ? (null, null) : (first, second);
    }

    private static double MinPercentFor(Node parent, Orientation o)
    {
        int size = o == Orientation.Horizontal ? parent.Rect.Width : parent.Rect.Height;
        return size > 0 ? Math.Max(MinPercent, 60.0 / size) : MinPercent;
    }

    private static int SizeAlong(Node n, Orientation o) => o == Orientation.Horizontal ? n.Rect.Width : n.Rect.Height;

    /// <summary>"resize grow|shrink width|height": changes the con's share, taking/giving evenly from siblings.</summary>
    public bool ResizeTiling(Node con, bool width, double? ppt, int? px)
    {
        var (first, second) = FindResizeParticipants(con, width ? Direction.Left : Direction.Down, bothSides: true);
        if (first == null || second == null) return false;
        var parent = first.Parent!;
        var o = width ? Orientation.Horizontal : Orientation.Vertical;
        double delta = ppt ?? (px.HasValue && SizeAlong(parent, o) > 0 ? (double)px.Value / SizeAlong(parent, o) : 0);
        if (delta == 0) return false;
        int n = parent.Children.Count;
        FixPercent(parent);
        double min = MinPercentFor(parent, o);
        double newPct = first.Percent + delta;
        double sub = delta / (n - 1);
        if (newPct < min || newPct > 1 - min * (n - 1)) return false;
        foreach (var c in parent.Children)
            if (c != first && c.Percent - sub < min) return false;
        first.Percent = newPct;
        foreach (var c in parent.Children)
            if (c != first) c.Percent -= sub;
        FixPercent(parent);
        return true;
    }

    /// <summary>Moves one edge of a con by <paramref name="px"/> pixels (positive = grow), adjusting only the neighbor on that side.</summary>
    public bool ResizeEdge(Node con, Direction edge, int px, double? ppt = null)
    {
        var (first, second) = FindResizeParticipants(con, edge, bothSides: false);
        if (first == null || second == null) return false;
        var parent = first.Parent!;
        var o = edge.ToOrientation();
        int size = SizeAlong(parent, o);
        if (size <= 0 && ppt == null) return false;
        FixPercent(parent);
        double min = MinPercentFor(parent, o);
        double delta = ppt ?? (double)px / size;
        double total = first.Percent + second.Percent;
        double f = Math.Clamp(first.Percent + delta, min, total - min);
        first.Percent = f;
        second.Percent = total - f;
        return true;
    }

    /// <summary>"resize set": sets the con's share to an absolute percentage (0..1).</summary>
    public bool ResizeSet(Node con, bool width, double targetPct)
    {
        var (first, _) = FindResizeParticipants(con, width ? Direction.Left : Direction.Down, bothSides: true);
        if (first == null) return false;
        FixPercent(first.Parent!);
        return ResizeTiling(first, width, targetPct - first.Percent, null);
    }

    /// <summary>Equalizes the sizes of all containers inside <paramref name="con"/> (Mozaic extension).</summary>
    public static void Balance(Node con)
    {
        foreach (var c in con.Children)
        {
            c.Percent = 1.0 / con.Children.Count;
            Balance(c);
        }
    }

    // =====================================================================================
    // Floating, fullscreen, scratchpad, marks
    // =====================================================================================

    public void SetFloating(Node win, bool floating)
    {
        if (win.Kind != NodeKind.Window || win.Parent == null || win.InFloating == floating) return;
        var ws = WorkspaceOf(win)!;
        bool wasFocused = Focused == win;
        var parent = win.Parent;
        DetachRaw(win);
        if (floating)
        {
            CleanupAfterRemoval(parent);
            if (win.FloatRect is not Rect r || r.IntersectionArea(OutputOf(ws)!.LayoutArea) == 0)
                win.FloatRect = DefaultFloatRect(ws);
            AttachFloating(win, ws, front: true);
        }
        else
        {
            win.IsScratchpad = false;
            win.Sticky = false;
            InsertTiling(win, ws);
        }
        if (wasFocused) Focus(win);
        else if (Focused != null && !IsAttached(Focused)) Focus(DescendFocused(ws));
    }

    public static Rect DefaultFloatRect(Node ws)
    {
        var area = OutputOf(ws)?.LayoutArea ?? new Rect(0, 0, 1280, 720);
        return Rect.FromSize(0, 0, area.Width * 3 / 5, area.Height * 3 / 5).CenteredIn(area);
    }

    public void SetFullscreen(Node win, bool on)
    {
        if (on)
        {
            var ws = WorkspaceOf(win);
            if (ws != null)
                foreach (var w in WindowsIn(ws)) w.Fullscreen = false;
        }
        win.Fullscreen = on;
    }

    public void MoveToScratchpad(Node win)
    {
        if (win.Kind != NodeKind.Window || win.Parent == null) return;
        var ws = WorkspaceOf(win)!;
        bool focusAffected = Focused == win;
        var next = focusAffected ? NextFocusAfterRemoval(win) : null;
        var parent = win.Parent;
        bool wasFloating = win.InFloating;
        if (!wasFloating && win.FloatRect == null) win.FloatRect = DefaultFloatRect(ws);
        DetachRaw(win);
        if (!wasFloating) CleanupAfterRemoval(parent);
        win.Fullscreen = false;
        win.Sticky = false;
        win.IsScratchpad = true;
        Scratchpad.Remove(win);
        Scratchpad.Add(win);
        if (focusAffected)
            Focus(next != null && IsAttached(next) ? next : DescendFocused(ws));
        RemoveIfEmptyAndHidden(ws);
    }

    /// <summary>i3's "scratchpad show": toggles/cycles scratchpad windows on the focused workspace.</summary>
    public bool ScratchpadShow(Node? specific = null)
    {
        var ws = FocusedWorkspace;
        if (ws == null) return false;

        if (specific == null && Focused is { IsScratchpad: true, InFloating: true } f && WorkspaceOf(f) == ws)
        {
            MoveToScratchpad(f);
            return true;
        }
        if (specific != null)
        {
            if (specific.Parent != null)
            {
                if (WorkspaceOf(specific) == ws)
                {
                    if (Focused == specific) MoveToScratchpad(specific); else Focus(specific);
                    return true;
                }
                MoveToWorkspace(specific, ws);
                Focus(specific);
                return true;
            }
            Scratchpad.Remove(specific);
            ShowScratch(specific, ws);
            return true;
        }
        var visibleHere = ws.Floating.FirstOrDefault(n => n.IsScratchpad);
        if (visibleHere != null)
        {
            Focus(visibleHere);
            return true;
        }
        if (Scratchpad.Count == 0) return false;
        var w = Scratchpad[0];
        Scratchpad.RemoveAt(0);
        ShowScratch(w, ws);
        return true;
    }

    private void ShowScratch(Node w, Node ws)
    {
        var area = OutputOf(ws)!.LayoutArea;
        var r = w.FloatRect ?? DefaultFloatRect(ws);
        w.FloatRect = r.IntersectionArea(area) * 2 < r.Width * r.Height ? r.CenteredIn(area) : r.ClampInto(area);
        AttachFloating(w, ws, front: true);
        Focus(w);
    }

    // =====================================================================================
    // Restoring a saved layout
    // =====================================================================================

    /// <summary>Appends a node while rebuilding a saved tree (keeps its percent; focus order is fixed up later).</summary>
    public static void RestoreAttach(Node child, Node parent, bool floating)
    {
        child.Parent = parent;
        child.InFloating = floating;
        if (floating) parent.Floating.Add(child); else parent.Children.Add(child);
        parent.FocusStack.Add(child);
    }

    /// <summary>Makes a workspace the visible one of its output without changing focus.</summary>
    public void SetVisibleWorkspace(Node ws)
    {
        if (ws.Parent is { Kind: NodeKind.Output }) MakeVisible(ws);
    }

    public void PruneEmptyWorkspaces()
    {
        foreach (var ws in Workspaces.ToList()) RemoveIfEmptyAndHidden(ws);
    }

    public void Mark(Node n, string mark, bool add, bool toggle)
    {
        if (toggle && n.Marks.Contains(mark))
        {
            n.Marks.Remove(mark);
            return;
        }
        foreach (var other in AllNodes()) other.Marks.Remove(mark);
        if (!add) n.Marks.Clear();
        n.Marks.Add(mark);
    }

    public void Unmark(string? mark)
    {
        foreach (var n in AllNodes())
        {
            if (mark == null) n.Marks.Clear(); else n.Marks.Remove(mark);
        }
    }
}
