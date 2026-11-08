using Mozaic.Core;
using Mozaic.Core.Config;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>i3bar-style bar for one output: workspace buttons, binding mode, focused title and status blocks.</summary>
internal sealed class BarWindow : Win32Window
{
    private enum HitKind { Workspace, ConfigErrors, Paused }

    private readonly WindowManager _wm;
    private readonly Node _output;
    private readonly List<(Rect Rect, HitKind Kind, string Arg)> _hits = new();
    private bool _shown = true;

    public BarWindow(WindowManager wm, Node output)
    {
        _wm = wm;
        _output = output;
        Create(C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE, C.WS_POPUP, "Mozaic bar", wm.BarRect(output));
        User32.SetWindowPos(Handle, C.HWND_TOP, 0, 0, 0, 0, C.SWP_NOMOVE | C.SWP_NOSIZE | C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
    }

    public void Reposition(Rect r)
    {
        User32.SetWindowPos(Handle, C.HWND_TOP, r.Left, r.Top, r.Width, r.Height,
            C.SWP_NOACTIVATE | (_shown ? C.SWP_SHOWWINDOW : 0));
        Invalidate();
    }

    public void SetShown(bool shown)
    {
        if (shown == _shown) return;
        _shown = shown;
        if (shown)
            User32.SetWindowPos(Handle, C.HWND_TOP, 0, 0, 0, 0, C.SWP_NOMOVE | C.SWP_NOSIZE | C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
        else
            User32.ShowWindow(Handle, C.SW_HIDE);
    }

    private int S(int v) => (int)Math.Round(v * _output.Dpi / 96.0);

    protected override void OnPaint(Canvas c)
    {
        _hits.Clear();
        var cfg = _wm.Config;
        var bar = cfg.Bar;
        if (bar == null) return;
        var colors = bar.Colors;
        var font = bar.Font ?? cfg.Font;
        c.SetFont(Fonts.Get(font.Family, font.Size, _output.Dpi));
        var full = new Rect(0, 0, c.Width, c.Height);
        c.Fill(full, colors.Background);

        int pad = S(7), x = S(2), inset = S(2), spacing = S(2);
        var tree = _wm.Tree;

        int iconSize = Math.Max(12, Math.Min(S(16), c.Height - 2 * inset - S(6)));
        int iconGap = S(4);

        Rect Button(string label, WorkspaceColor wc, int minWidth = 0, List<IntPtr>? icons = null)
        {
            int textW = c.MeasureWidth(label);
            int iconsW = icons is { Count: > 0 } ? icons.Count * (iconSize + iconGap) : 0;
            int w = Math.Max(textW + 2 * pad + iconsW, minWidth);
            var r = new Rect(x, inset, x + w, c.Height - inset);
            c.Fill(r, wc.Background);
            c.Frame(r, wc.Border, Math.Max(1, S(1)));
            if (iconsW == 0)
            {
                c.Text(label, r, wc.Text, C.DT_CENTER);
            }
            else
            {
                int left = r.Left + (r.Width - textW - iconsW) / 2;
                c.Text(label, new Rect(left, r.Top, left + textW, r.Bottom), wc.Text, C.DT_LEFT);
                int ix = left + textW + iconGap, iy = r.Top + (r.Height - iconSize) / 2;
                foreach (var icon in icons!)
                {
                    IconCache.Draw(c.Hdc, icon, ix, iy, iconSize);
                    ix += iconSize + iconGap;
                }
            }
            x += w + spacing;
            return r;
        }

        if (bar.WorkspaceButtons)
        {
            foreach (var ws in _output.Children)
            {
                string label = bar.StripWorkspaceNumbers ? WorkspaceNames.StripNumber(ws.Name)
                    : bar.StripWorkspaceName ? WorkspaceNames.StripName(ws.Name) : ws.Name;
                bool urgent = Tree.WindowsIn(ws).Any(w => w.Urgent);
                var wc = urgent ? colors.UrgentWorkspace
                    : ws == tree.FocusedWorkspace ? colors.FocusedWorkspace
                    : ws == _output.CurrentWorkspace ? colors.ActiveWorkspace
                    : colors.InactiveWorkspace;
                List<IntPtr>? icons = null;
                if (bar.WorkspaceIcons)
                {
                    icons = Tree.WindowsIn(ws)
                        .Select(n => n.Tag as ManagedWindow).OfType<ManagedWindow>()
                        .DistinctBy(m => m.ProcessName, StringComparer.OrdinalIgnoreCase)
                        .Take(6).Select(IconCache.Get).Where(i => i != 0).ToList();
                }
                var r = Button(label, wc, S(bar.WorkspaceMinWidth), icons);
                _hits.Add((r, HitKind.Workspace, ws.Name));
            }
        }
        if (bar.BindingModeIndicator && _wm.Mode != "default")
            Button(_wm.Mode, colors.BindingMode);
        if (_wm.Paused)
            _hits.Add((Button(L.T("tiling paused"), colors.UrgentWorkspace), HitKind.Paused, ""));
        if (_wm.ConfigErrors.Count > 0)
            _hits.Add((Button(_wm.ConfigErrors.Count == 1 ? L.T("config error") : L.F("{0} config errors", _wm.ConfigErrors.Count), colors.UrgentWorkspace), HitKind.ConfigErrors, ""));

        // Status blocks, right to left.
        int right = c.Width - S(6);
        var blocks = _wm.Status.Blocks;
        int sepWidth = S(9);
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            var b = blocks[i];
            if (b.Text.Length == 0) continue;
            int w = c.MeasureWidth(b.Text);
            int bpad = b.Background.HasValue || b.Urgent ? S(5) : 0;
            var r = new Rect(right - w - 2 * bpad, inset, right, c.Height - inset);
            if (r.Left < x + S(40)) break;
            if (b.Urgent) c.Fill(r, colors.UrgentWorkspace.Background);
            else if (b.Background is uint bg) c.Fill(r, bg);
            uint fg = b.Urgent ? colors.UrgentWorkspace.Text : b.Color ?? colors.Statusline;
            c.Text(b.Text, new Rect(r.Left + bpad, r.Top, r.Right - bpad, r.Bottom), fg, C.DT_LEFT);
            right = r.Left;
            if (i > 0 && b.Separator)
            {
                if (bar.SeparatorSymbol != null)
                {
                    int sw = c.MeasureWidth(bar.SeparatorSymbol) + S(8);
                    c.Text(bar.SeparatorSymbol, new Rect(right - sw, 0, right, c.Height), colors.Separator, C.DT_CENTER);
                    right -= sw;
                }
                else
                {
                    int bw = Math.Max(S(b.SeparatorWidth), sepWidth);
                    int mid = right - bw / 2;
                    c.Fill(new Rect(mid, S(5), mid + Math.Max(1, S(1)), c.Height - S(5)), colors.Separator);
                    right -= bw;
                }
            }
        }

        // Title of the window focused on this output's workspace.
        if (bar.ShowTitle && _output.CurrentWorkspace is { } cur)
        {
            var leaf = Tree.DescendFocused(cur);
            if (leaf.Kind == NodeKind.Window && leaf.Parent != null)
            {
                bool focusedOutput = tree.FocusedOutput == _output;
                var tr = new Rect(x + S(8), 0, right - S(12), c.Height);
                if (bar.WorkspaceIcons && leaf.Tag is ManagedWindow lmw && tr.Width > S(80))
                {
                    var icon = IconCache.Get(lmw);
                    if (icon != 0)
                    {
                        IconCache.Draw(c.Hdc, icon, tr.Left, (c.Height - iconSize) / 2, iconSize);
                        tr = new Rect(tr.Left + iconSize + S(6), tr.Top, tr.Right, tr.Bottom);
                    }
                }
                if (tr.Width > S(40))
                    c.Text(leaf.Title, tr, focusedOutput ? colors.Statusline : colors.Separator, C.DT_LEFT);
            }
        }
    }

    protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        if (NoActivate(msg, out result)) return true;
        switch (msg)
        {
            case C.WM_LBUTTONDOWN:
            {
                int x = LoWord(lParam), y = HiWord(lParam);
                foreach (var (r, kind, arg) in _hits)
                {
                    if (!r.Contains(x, y)) continue;
                    switch (kind)
                    {
                        case HitKind.Workspace:
                            _wm.RunCommand("workspace \"" + arg.Replace("\"", "\\\"") + "\"", fromUser: false);
                            break;
                        case HitKind.ConfigErrors:
                            _wm.OpenLog();
                            break;
                        case HitKind.Paused:
                            _wm.TogglePause();
                            break;
                    }
                    break;
                }
                return true;
            }
            case C.WM_MOUSEWHEEL:
            {
                int delta = HiWord(wParam);
                if (_output.CurrentWorkspace is { } ws && _wm.Tree.FocusedWorkspace != ws) _wm.Tree.ShowWorkspace(ws);
                _wm.RunCommand(delta > 0 ? "workspace prev_on_output" : "workspace next_on_output", fromUser: false);
                return true;
            }
            case C.WM_DPICHANGED:
                result = 0;
                return true;
        }
        return false;
    }
}
