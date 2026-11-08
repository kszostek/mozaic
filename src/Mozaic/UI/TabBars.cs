using Mozaic.Core;
using Mozaic.Core.Config;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>Keeps one title-bar window per visible tabbed/stacked container.</summary>
internal sealed class TabBarPool(WindowManager wm)
{
    private readonly Dictionary<Node, TabBarWindow> _active = new();
    private readonly Stack<TabBarWindow> _free = new();

    public void Update(List<TabBarInfo> infos)
    {
        var seen = new HashSet<Node>();
        foreach (var info in infos)
        {
            seen.Add(info.Container);
            if (!_active.TryGetValue(info.Container, out var tb))
            {
                tb = _free.Count > 0 ? _free.Pop() : new TabBarWindow(wm);
                _active[info.Container] = tb;
            }
            tb.Set(info);
        }
        foreach (var key in _active.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var tb = _active[key];
            _active.Remove(key);
            tb.HideBar();
            _free.Push(tb);
        }
    }

    public void InvalidateAll()
    {
        foreach (var tb in _active.Values) tb.Invalidate();
    }

    public void HideAll() => Update(new List<TabBarInfo>());

    public void DestroyAll()
    {
        foreach (var tb in _active.Values.Concat(_free)) tb.Destroy();
        _active.Clear();
        _free.Clear();
    }
}

internal sealed class TabBarWindow : Win32Window
{
    private readonly WindowManager _wm;
    private readonly List<(Rect Rect, Node Child)> _hits = new();
    private TabBarInfo _info;
    private bool _visible;

    public TabBarWindow(WindowManager wm)
    {
        _wm = wm;
        Create(C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE, C.WS_POPUP, "Mozaic tabs", new Rect(0, 0, 1, 1));
    }

    public void Set(TabBarInfo info)
    {
        bool moved = info.Rect != _info.Rect || !_visible;
        _info = info;
        if (moved)
        {
            var r = info.Rect;
            User32.SetWindowPos(Handle, _visible ? 0 : C.HWND_TOP, r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height),
                C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW | (_visible ? C.SWP_NOZORDER : 0));
            _visible = true;
        }
        Invalidate();
    }

    public void HideBar()
    {
        if (!_visible) return;
        _visible = false;
        User32.ShowWindow(Handle, C.SW_HIDE);
    }

    private static string Label(Node n)
    {
        if (n.Kind == NodeKind.Window)
        {
            if (n.Title.Length > 0) return n.Title;
            return n.Tag is ManagedWindow mw ? Path.GetFileNameWithoutExtension(mw.ProcessName) : "";
        }
        string l = n.Layout switch
        {
            LayoutKind.SplitH => "H",
            LayoutKind.SplitV => "V",
            LayoutKind.Tabbed => "T",
            _ => "S",
        };
        var inner = n.Children.Select(c => c.Kind == NodeKind.Window && c.Tag is ManagedWindow m
            ? Path.GetFileNameWithoutExtension(m.ProcessName)
            : Label(c));
        return l + "[" + string.Join(" ", inner) + "]";
    }

    protected override void OnPaint(Canvas c)
    {
        _hits.Clear();
        var con = _info.Container;
        var kids = con.Children;
        var output = Tree.OutputOf(con);
        if (kids.Count == 0 || output == null) return;
        var cfg = _wm.Config;
        c.SetFont(Fonts.Get(cfg.Font.Family, cfg.Font.Size, output.Dpi));
        c.Fill(new Rect(0, 0, c.Width, c.Height), cfg.Unfocused.Background);

        var tree = _wm.Tree;
        var focused = tree.Focused;
        var active = con.FocusStack.FirstOrDefault(n => !n.InFloating);
        int pad = (int)Math.Round(6 * output.Dpi / 96.0);
        for (int i = 0; i < kids.Count; i++)
        {
            var child = kids[i];
            Rect r = _info.Stacked
                ? new Rect(0, i * _info.RowHeight, c.Width, Math.Min(c.Height, (i + 1) * _info.RowHeight))
                : new Rect(c.Width * i / kids.Count, 0, c.Width * (i + 1) / kids.Count, c.Height);
            // Focused: the focus is this tab (or inside it), or a parent container containing this whole tab bar.
            bool isFocused = focused != null &&
                             (focused == child || Tree.IsAncestor(child, focused) ||
                              (focused.Kind != NodeKind.Window && Tree.IsAncestor(focused, child)));
            bool urgent = Tree.WindowsIn(child).Any(w => w.Urgent);
            ClientColor cc = urgent ? cfg.Urgent
                : isFocused ? cfg.Focused
                : child == active ? cfg.FocusedInactive
                : cfg.Unfocused;
            c.Fill(r, cc.Background);
            c.Frame(r, cc.Border, 1);
            c.Text(Label(child), new Rect(r.Left + pad, r.Top, r.Right - pad, r.Bottom), cc.Text, C.DT_LEFT);
            _hits.Add((r, child));
        }
    }

    protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        if (NoActivate(msg, out result)) return true;
        switch (msg)
        {
            case C.WM_LBUTTONDOWN:
            case C.WM_MBUTTONUP:
            {
                int x = LoWord(lParam), y = HiWord(lParam);
                foreach (var (r, child) in _hits)
                {
                    if (!r.Contains(x, y)) continue;
                    if (msg == C.WM_MBUTTONUP)
                    {
                        foreach (var w in Tree.WindowsIn(child)) WindowOps.Close(w.Handle);
                    }
                    else if (child.Parent != null)
                    {
                        _wm.Tree.Focus(Tree.DescendFocused(child));
                        _wm.RequestFocusSync(false);
                    }
                    break;
                }
                return true;
            }
            case C.WM_MOUSEWHEEL:
            {
                var con = _info.Container;
                var active = con.FocusStack.FirstOrDefault(n => !n.InFloating);
                if (active == null || con.Children.Count < 2) return true;
                int i = con.Children.IndexOf(active);
                int delta = HiWord(wParam);
                var next = con.Children[(i + (delta > 0 ? -1 : 1) + con.Children.Count) % con.Children.Count];
                _wm.Tree.Focus(Tree.DescendFocused(next));
                _wm.RequestFocusSync(false);
                return true;
            }
            case C.WM_DPICHANGED:
                result = 0;
                return true;
        }
        return false;
    }
}
