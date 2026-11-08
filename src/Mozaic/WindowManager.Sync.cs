using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;
using Mozaic.State;
using Mozaic.UI;

namespace Mozaic;

internal sealed partial class WindowManager
{
    private readonly Dictionary<Node, BarWindow> _bars = new();
    private bool _applyQueued, _applying, _focusDirty, _warp;
    private IntPtr _lastRaisedFor;
    private string _lastHiddenSignature = "";

    public void RequestApply()
    {
        if (_applyQueued || _stopping) return;
        _applyQueued = true;
        Dispatcher.Post(Apply);
    }

    /// <summary>Marks OS focus as out of date (a command moved focus); optionally warps the mouse to another output.</summary>
    public void RequestFocusSync(bool warp)
    {
        _focusDirty = true;
        _warp |= warp;
        RequestApply();
    }

    private int Scale(Node output, int logical) => (int)Math.Round(logical * output.Dpi / 96.0);

    public LayoutSettings LayoutSettingsFor(Node output) => new()
    {
        InnerGap = Scale(output, Config.GapsInner),
        OuterTop = Scale(output, Config.GapsTop),
        OuterRight = Scale(output, Config.GapsRight),
        OuterBottom = Scale(output, Config.GapsBottom),
        OuterLeft = Scale(output, Config.GapsLeft),
        SmartGaps = Config.SmartGaps,
        BorderWidth = Scale(output, Config.BorderWidth),
        TabHeight = TabHeightFor(output),
    };

    public int TabHeightFor(Node output) => Fonts.LineHeight(Config.Font.Size, output.Dpi) + Scale(output, 6);

    private void Apply()
    {
        _applyQueued = false;
        if (_applying || _stopping) return;
        _applying = true;
        try
        {
            ApplyCore();
        }
        catch (Exception e)
        {
            Log.Error("apply", e);
        }
        finally
        {
            _applying = false;
        }
    }

    private void ApplyCore()
    {
        var tabBars = LayoutEngine.Compute(Tree, LayoutSettingsFor);
        if (Paused)
        {
            UpdateBars();
            return;
        }

        var toHide = new List<ManagedWindow>();
        foreach (var mw in _windows.Values)
        {
            var n = mw.Node;
            if (!n.Visible || n.Parent == null)
            {
                toHide.Add(mw);
                continue;
            }
            if (n.InFloating && !n.Fullscreen)
            {
                if (n.FloatRect is Rect fr && mw.LastTarget != fr && !mw.InMoveSize && _drag?.Window != mw)
                    WindowOps.Place(mw, fr);
            }
            else
            {
                PlaceTiled(mw, n.WindowRect);
            }
            if (mw.HiddenByUs) WindowOps.Show(mw);
        }
        // Hide after showing the new windows: switching workspaces never flashes the desktop.
        foreach (var mw in toHide)
            if (!mw.HiddenByUs) WindowOps.Hide(mw);

        _tabs.Update(tabBars);
        UpdateBars();
        UpdateBorders();
        SyncFocus();
        RaiseFloating();
        UpdateFocusFrame();
        UpdateEffects();
        PersistHidden();
        _state.ScheduleSave(this);
    }

    private void PlaceTiled(ManagedWindow mw, Rect rect)
    {
        if (mw.NativeMaximized || mw.NativeFullscreen || mw.InMoveSize) return;
        if (mw.LastTarget == rect) return;
        WindowOps.Place(mw, rect);
    }

    private void UpdateBorders()
    {
        if (!Config.WindowBorderColors)
        {
            foreach (var mw in _windows.Values)
                if (mw.AppliedBorderColor != null) WindowOps.SetBorderColor(mw, null);
            return;
        }
        var focused = Tree.Focused;
        var focusedWin = Tree.FocusedWindow;
        HashSet<Node>? group = focused is { Kind: NodeKind.Container or NodeKind.Workspace }
            ? Tree.WindowsIn(focused).ToHashSet()
            : null;
        var inactiveFocus = new HashSet<Node>();
        foreach (var o in Tree.Outputs)
        {
            if (o.CurrentWorkspace == null || o.CurrentWorkspace == Tree.FocusedWorkspace) continue;
            var d = Tree.DescendFocused(o.CurrentWorkspace);
            if (d.Kind == NodeKind.Window) inactiveFocus.Add(d);
        }
        foreach (var mw in _windows.Values)
        {
            var n = mw.Node;
            if (!n.Visible || mw.HiddenByUs) continue;
            uint color;
            if (n.Urgent) color = Config.Urgent.ChildBorder;
            else if (n == focusedWin || group?.Contains(n) == true) color = Config.Focused.ChildBorder;
            else if (inactiveFocus.Contains(n)) color = Config.FocusedInactive.ChildBorder;
            else color = Config.Unfocused.ChildBorder;
            WindowOps.SetBorderColor(mw, color);
        }
    }

    private void SyncFocus()
    {
        if (!_focusDirty) return;
        _focusDirty = false;
        bool warp = _warp;
        _warp = false;
        var win = Tree.FocusedWindow;
        if (win?.Tag is ManagedWindow mw && win.Visible)
        {
            _expectedFg = mw.Hwnd;
            _expectedFgAt = Environment.TickCount64;
            if (!WindowOps.Focus(mw.Hwnd))
            {
                // The window may still be in the middle of being shown; try once more.
                Dispatcher.SetTimeout(60, () =>
                {
                    if (Tree.FocusedWindow == win && !_stopping)
                    {
                        _expectedFgAt = Environment.TickCount64;
                        WindowOps.Focus(mw.Hwnd);
                    }
                });
            }
            if (warp) WarpTo(win.WindowRect);
        }
        else if (Tree.FocusedOutput is { } o)
        {
            _expectedFg = 0;
            _sink.Activate(o.LayoutArea);
            if (warp) WarpTo(o.LayoutArea);
        }
    }

    private void WarpTo(Rect target)
    {
        if (Config.MouseWarping == "none" || target.IsEmpty) return;
        User32.GetCursorPos(out var pt);
        var from = OutputAt(pt.X, pt.Y);
        var to = OutputAt(target.CenterX, target.CenterY);
        if (from != to) User32.SetCursorPos(target.CenterX, target.CenterY);
    }

    /// <summary>i3 keeps floating windows above tiled ones; re-raise them when a tiled window gets focus.</summary>
    private void RaiseFloating()
    {
        var win = Tree.FocusedWindow;
        if (win == null || win.InFloating || !Config.FloatingAlwaysOnTop)
        {
            _lastRaisedFor = win?.Handle ?? 0;
            return;
        }
        if (win.Handle == _lastRaisedFor) return;
        _lastRaisedFor = win.Handle;
        var ws = Tree.WorkspaceOf(win);
        if (ws == null) return;
        for (int i = ws.FocusStack.Count - 1; i >= 0; i--)
        {
            var f = ws.FocusStack[i];
            if (f.InFloating && f.Visible) WindowOps.RaiseNoActivate(f.Handle);
        }
    }

    private void UpdateFocusFrame()
    {
        var win = Tree.FocusedWindow;
        if (Config.BorderWidth <= 0 || win == null || win.InFloating || win.Fullscreen || !win.Visible ||
            win.Tag is not ManagedWindow mw || mw.NativeMaximized || mw.NativeFullscreen || mw.InMoveSize)
        {
            _frame.Hide();
            return;
        }
        var output = Tree.OutputOf(win)!;
        int bw = Scale(output, Config.BorderWidth);
        var r = win.WindowRect;
        _frame.ShowAround(new Rect(r.Left - bw, r.Top - bw, r.Right + bw, r.Bottom + bw), bw,
            win.Urgent ? Config.Urgent.ChildBorder : Config.Focused.ChildBorder, mw.Hwnd);
    }

    private void PersistHidden()
    {
        var hidden = _windows.Values.Where(w => w.HiddenByUs).OrderBy(w => (long)w.Hwnd).ToList();
        var sig = string.Join(',', hidden.Select(h => ((long)h.Hwnd).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (sig == _lastHiddenSignature) return;
        _lastHiddenSignature = sig;
        _hidden.Save(hidden.Select(h => new HiddenEntry(h.Hwnd, h.Pid, h.ClassName)));
    }

    // =====================================================================================
    // Bars & decorations
    // =====================================================================================

    private void SyncBars()
    {
        foreach (var o in Tree.Outputs)
        {
            bool wants = BarHeightFor(o) > 0;
            if (wants && !_bars.ContainsKey(o)) _bars[o] = new BarWindow(this, o);
            else if (!wants && _bars.Remove(o, out var old)) old.Destroy();
        }
        foreach (var o in _bars.Keys.Where(o => !Tree.Outputs.Contains(o)).ToList())
        {
            _bars[o].Destroy();
            _bars.Remove(o);
        }
        foreach (var (o, bar) in _bars) bar.Reposition(BarRect(o));
    }

    public Rect BarRect(Node o)
    {
        int h = BarHeightFor(o);
        var w = o.WorkArea;
        return Config.Bar?.Position == "bottom"
            ? new Rect(w.Left, w.Bottom - h, w.Right, w.Bottom)
            : new Rect(w.Left, w.Top, w.Right, w.Top + h);
    }

    private void UpdateBars()
    {
        foreach (var (o, bar) in _bars)
        {
            bool fullscreen = false;
            if (o.CurrentWorkspace != null)
            {
                foreach (var w in Tree.WindowsIn(o.CurrentWorkspace))
                {
                    if (w.Fullscreen || (w.Tag is ManagedWindow m && (m.NativeFullscreen || m.NativeMaximized) && w.Visible))
                    {
                        fullscreen = true;
                        break;
                    }
                }
            }
            bar.SetShown(!fullscreen);
            bar.Invalidate();
        }
    }

    public void InvalidateBars()
    {
        foreach (var b in _bars.Values) b.Invalidate();
    }

    private void InvalidateDecorations()
    {
        InvalidateBars();
        _tabs.InvalidateAll();
    }

    // =====================================================================================
    // Keeping windows where they belong
    // =====================================================================================

    private void OnLocationChange(ManagedWindow mw)
    {
        var n = mw.Node;
        var hwnd = mw.Hwnd;
        if (mw.InMoveSize)
        {
            if (!mw.MoveSizeIsResize && !n.InFloating) UpdateNativeDragPreview(mw);
            return;
        }
        if (!n.Visible || mw.HiddenByUs || n.Parent == null || Paused) return;
        if (User32.IsIconic(hwnd)) return;

        if (User32.IsZoomed(hwnd))
        {
            if (!mw.NativeMaximized)
            {
                mw.NativeMaximized = true;
                RequestApply();
            }
            return;
        }
        if (mw.NativeMaximized)
        {
            mw.NativeMaximized = false;
            mw.LastTarget = null;
            RequestApply();
            return;
        }
        if (_drag?.Window == mw) return;

        var frame = WindowQuery.FrameRect(hwnd);
        if (n.InFloating && !n.Fullscreen)
        {
            if (frame.IsEmpty) return;
            n.FloatRect = frame;
            mw.LastTarget = frame;
            return;
        }

        // Native fullscreen (browser F11, video players, games): leave it alone until it ends.
        var mon = Monitors.BoundsForWindow(hwnd);
        if (!n.Fullscreen && mon is Rect m && frame.Left <= m.Left && frame.Top <= m.Top && frame.Right >= m.Right && frame.Bottom >= m.Bottom)
        {
            if (!mw.NativeFullscreen)
            {
                mw.NativeFullscreen = true;
                RequestApply();
            }
            return;
        }
        if (mw.NativeFullscreen)
        {
            mw.NativeFullscreen = false;
            mw.LastTarget = null;
            RequestApply();
            return;
        }

        if (mw.LastTarget is not Rect target || frame.NearlyEquals(target, 2)) return;

        // The application moved/resized itself: put it back (rate-limited, apps with a minimum
        // size larger than their tile would otherwise fight forever).
        long now = Environment.TickCount64;
        if (now - mw.EnforceWindowStart > 1500)
        {
            mw.EnforceWindowStart = now;
            mw.EnforceCount = 0;
        }
        if (mw.EnforceCount >= 4 || mw.EnforceTimer != 0) return;
        mw.EnforceCount++;
        mw.EnforceTimer = Dispatcher.SetTimeout(30, () =>
        {
            mw.EnforceTimer = 0;
            if (!_windows.ContainsKey(mw.Hwnd) || mw.InMoveSize || !mw.Node.Visible || mw.HiddenByUs) return;
            if (mw.NativeMaximized || mw.NativeFullscreen) return;
            if (mw.LastTarget is Rect t) WindowOps.Place(mw, t);
        });
    }
}
