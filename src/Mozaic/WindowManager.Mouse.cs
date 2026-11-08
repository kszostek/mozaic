using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic;

internal sealed partial class WindowManager
{
    private sealed class DragState(ManagedWindow window, DragButton button, int x, int y, Rect startFrame, bool floating)
    {
        public ManagedWindow Window { get; } = window;
        public DragButton Button { get; } = button;
        public int StartX { get; } = x;
        public int StartY { get; } = y;
        public Rect StartFrame { get; } = startFrame;
        public bool Floating { get; } = floating;
        public int LastX { get; set; } = x;
        public int LastY { get; set; } = y;
        public bool ResizeLeft { get; init; }
        public bool ResizeTop { get; init; }
    }

    private readonly record struct DropPlan(Node? Target, Direction? Side, Node? Workspace, Rect Preview);

    private DragState? _drag;
    private nuint _ffmTimer;
    private POINT _ffmLast;

    // =====================================================================================
    // Native drags (title bar / window edges)
    // =====================================================================================

    private void OnMoveSizeStart(ManagedWindow mw)
    {
        mw.InMoveSize = true;
        Dispatcher.ClearTimer(ref mw.EnforceTimer);
        var frame = WindowQuery.FrameRect(mw.Hwnd);
        mw.MoveSizeStartFrame = mw.Node.InFloating ? frame : mw.LastTarget ?? frame;
        User32.GetCursorPos(out var pt);
        var output = Tree.OutputOf(mw.Node) ?? Tree.Outputs[0];
        int band = Scale(output, 10);
        mw.MoveSizeIsResize = pt.X <= frame.Left + band || pt.X >= frame.Right - band ||
                              pt.Y >= frame.Bottom - band || pt.Y <= frame.Top + Scale(output, 4);
        Log.Debug($"move/size start {mw} ({(mw.MoveSizeIsResize ? "resize" : "move")}) cursor {pt.X},{pt.Y} frame {frame}");
        _frame.Hide();
    }

    private void OnMoveSizeEnd(ManagedWindow mw)
    {
        if (!mw.InMoveSize) return;
        mw.InMoveSize = false;
        _preview.Hide();
        var n = mw.Node;
        if (n.Parent == null) return;
        var frame = WindowQuery.FrameRect(mw.Hwnd);

        if (n.InFloating && !n.Fullscreen)
        {
            n.FloatRect = frame;
            mw.LastTarget = frame;
            MoveFloatingToOutputUnderIt(n);
            RequestApply();
            return;
        }
        if (User32.IsZoomed(mw.Hwnd)) return; // dragged to the top edge: Windows maximized it

        var start = mw.MoveSizeStartFrame;
        if (Log.DebugEnabled)
        {
            User32.GetCursorPos(out var cp);
            Log.Debug($"move/size end {mw} frame {frame} (start {start}) cursor {cp.X},{cp.Y}");
        }
        bool sizeChanged = Math.Abs(frame.Width - start.Width) > 3 || Math.Abs(frame.Height - start.Height) > 3;
        _snapAssistGuardUntil = Environment.TickCount64 + 2000;
        _focusDirty = true;
        if (Config.TilingDrag && !Paused)
        {
            if (mw.MoveSizeIsResize && sizeChanged)
            {
                int dl = frame.Left - start.Left, dr = frame.Right - start.Right;
                int dt = frame.Top - start.Top, db = frame.Bottom - start.Bottom;
                if (Math.Abs(dl) > 2) Tree.ResizeEdge(n, Direction.Left, -dl);
                if (Math.Abs(dr) > 2) Tree.ResizeEdge(n, Direction.Right, dr);
                if (Math.Abs(dt) > 2) Tree.ResizeEdge(n, Direction.Up, -dt);
                if (Math.Abs(db) > 2) Tree.ResizeEdge(n, Direction.Down, db);
            }
            else if (!frame.NearlyEquals(start, 3))
            {
                User32.GetCursorPos(out var pt);
                DropTiled(n, pt.X, pt.Y);
            }
        }
        mw.LastTarget = null;
        RequestApply();
    }

    private void UpdateNativeDragPreview(ManagedWindow mw)
    {
        if (!Config.TilingDrag) return;
        User32.GetCursorPos(out var pt);
        ShowDropPreview(ComputeDrop(mw.Node, pt.X, pt.Y));
    }

    private void ShowDropPreview(DropPlan plan)
    {
        if (plan.Preview.IsEmpty) _preview.Hide();
        else _preview.Show(plan.Preview, Config.Focused.Indicator);
    }

    private DropPlan ComputeDrop(Node moving, int x, int y)
    {
        Node? hit = null;
        foreach (var other in _windows.Values)
        {
            var n = other.Node;
            if (n == moving || !n.Visible || n.InFloating || n.Fullscreen || other.HiddenByUs) continue;
            if (n.Rect.Contains(x, y)) { hit = n; break; }
        }
        if (hit != null)
        {
            var r = hit.Rect;
            double fx = (x - r.Left) / (double)Math.Max(1, r.Width);
            double fy = (y - r.Top) / (double)Math.Max(1, r.Height);
            var edges = new (double Dist, Direction Side)[] { (fx, Direction.Left), (1 - fx, Direction.Right), (fy, Direction.Up), (1 - fy, Direction.Down) };
            var nearest = edges.MinBy(e => e.Dist);
            if (nearest.Dist < 0.3)
            {
                var half = nearest.Side switch
                {
                    Direction.Left => new Rect(r.Left, r.Top, r.CenterX, r.Bottom),
                    Direction.Right => new Rect(r.CenterX, r.Top, r.Right, r.Bottom),
                    Direction.Up => new Rect(r.Left, r.Top, r.Right, r.CenterY),
                    _ => new Rect(r.Left, r.CenterY, r.Right, r.Bottom),
                };
                return new DropPlan(hit, nearest.Side, null, half);
            }
            return new DropPlan(hit, null, null, r);
        }
        var output = OutputAt(x, y);
        var ws = output?.CurrentWorkspace;
        if (ws != null && ws.Children.Count == 0 && ws != Tree.WorkspaceOf(moving))
            return new DropPlan(null, null, ws, output!.LayoutArea);
        return default;
    }

    private void DropTiled(Node n, int x, int y)
    {
        var plan = ComputeDrop(n, x, y);
        if (plan.Target != null && plan.Side is Direction side) Tree.MoveNextTo(n, plan.Target, side);
        else if (plan.Target != null) Tree.Swap(n, plan.Target);
        else if (plan.Workspace != null) Tree.MoveToWorkspace(n, plan.Workspace);
        else return;
        if (n.Parent != null) Tree.Focus(n);
        _focusDirty = true;
    }

    /// <summary>A floating window dropped on another monitor moves to that monitor's workspace.</summary>
    private void MoveFloatingToOutputUnderIt(Node n)
    {
        if (n.FloatRect is not Rect r) return;
        var output = OutputAt(r.CenterX, r.CenterY);
        var ws = output?.CurrentWorkspace;
        if (ws == null || ws == Tree.WorkspaceOf(n)) return;
        var keep = r;
        Tree.MoveToWorkspace(n, ws);
        n.FloatRect = keep;
        Tree.Focus(n);
    }

    // =====================================================================================
    // $mod + drag (floating_modifier)
    // =====================================================================================

    private void OnDragStart(IntPtr hwnd, DragButton button, int x, int y)
    {
        Log.Debug($"mod-drag start 0x{hwnd:X} {button} at {x},{y}");
        if (!_windows.TryGetValue(hwnd, out var mw) || mw.Node.Parent == null || Paused)
        {
            InputHooks.CancelDrag();
            return;
        }
        var n = mw.Node;
        var frame = WindowQuery.FrameRect(hwnd);
        _drag = new DragState(mw, button, x, y, frame, n.InFloating && !n.Fullscreen)
        {
            ResizeLeft = x < frame.CenterX,
            ResizeTop = y < frame.CenterY,
        };
        if (Tree.Focused != n)
        {
            Tree.Focus(n);
            _focusDirty = true;
        }
        _frame.Hide();
        RequestApply();
    }

    private void OnDragMove(int x, int y)
    {
        var d = _drag;
        if (d == null) return;
        var mw = d.Window;
        var n = mw.Node;
        if (n.Parent == null) { CancelDrag(); return; }
        if (d.Floating)
        {
            var s = d.StartFrame;
            int dx = x - d.StartX, dy = y - d.StartY;
            Rect r;
            if (d.Button == DragButton.Left)
            {
                r = s.Offset(dx, dy);
            }
            else
            {
                int l = s.Left, t = s.Top, rr = s.Right, b = s.Bottom;
                if (d.ResizeLeft) l = Math.Min(s.Left + dx, rr - 80); else rr = Math.Max(s.Right + dx, l + 80);
                if (d.ResizeTop) t = Math.Min(s.Top + dy, b - 60); else b = Math.Max(s.Bottom + dy, t + 60);
                r = new Rect(l, t, rr, b);
            }
            n.FloatRect = r;
            WindowOps.Place(mw, r);
            return;
        }
        if (d.Button == DragButton.Left)
        {
            ShowDropPreview(ComputeDrop(n, x, y));
            return;
        }
        int ddx = x - d.LastX, ddy = y - d.LastY;
        bool changed = false;
        if (Math.Abs(ddx) >= 2)
        {
            changed |= Tree.ResizeEdge(n, d.ResizeLeft ? Direction.Left : Direction.Right, d.ResizeLeft ? -ddx : ddx);
            d.LastX = x;
        }
        if (Math.Abs(ddy) >= 2)
        {
            changed |= Tree.ResizeEdge(n, d.ResizeTop ? Direction.Up : Direction.Down, d.ResizeTop ? -ddy : ddy);
            d.LastY = y;
        }
        if (changed) RequestApply();
    }

    private void OnDragEnd(int x, int y, bool cancelled)
    {
        var d = _drag;
        _drag = null;
        _preview.Hide();
        Log.Debug($"mod-drag end at {x},{y} (active: {d != null})");
        if (d == null) return;
        var n = d.Window.Node;
        if (n.Parent == null) return;
        if (d.Floating) MoveFloatingToOutputUnderIt(n);
        else if (d.Button == DragButton.Left && !cancelled) DropTiled(n, x, y);
        d.Window.LastTarget = d.Floating ? n.FloatRect : null;
        RequestApply();
    }

    private void CancelDrag()
    {
        InputHooks.CancelDrag();
        _drag = null;
        _preview.Hide();
    }

    // =====================================================================================
    // Focus follows mouse
    // =====================================================================================

    private void UpdateFocusFollowsMouse()
    {
        Dispatcher.ClearTimer(ref _ffmTimer);
        if (Config.FocusFollowsMouse) _ffmTimer = Dispatcher.SetInterval(80, FocusFollowsMouseTick);
    }

    private void FocusFollowsMouseTick()
    {
        if (Paused || _drag != null || _stopping) return;
        if (!User32.GetCursorPos(out var pt)) return;
        if (pt.X == _ffmLast.X && pt.Y == _ffmLast.Y) return;
        _ffmLast = pt;
        if ((User32.GetAsyncKeyState(C.VK_LBUTTON) & 0x8000) != 0 || (User32.GetAsyncKeyState(C.VK_RBUTTON) & 0x8000) != 0) return;
        // Never steal focus from menus, popups, the start menu etc.
        var fg = User32.GetForegroundWindow();
        if (fg != 0 && !_windows.ContainsKey(fg) && !Win32Window.IsOwnWindow(fg)) return;
        var root = User32.GetAncestor(User32.WindowFromPoint(pt), C.GA_ROOT);
        if (!_windows.TryGetValue(root, out var mw)) return;
        var n = mw.Node;
        if (!n.Visible || n == Tree.FocusedWindow || mw.InMoveSize || n.Parent == null) return;
        Tree.Focus(n);
        _focusDirty = true;
        RequestApply();
    }
}
