using Mozaic.Core;
using Mozaic.Core.Config;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>Keeps one title-bar window per visible tabbed/stacked container, and per window when title bars are on.</summary>
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
        foreach (var tb in _active.Values) tb.Redraw();
    }

    public void HideAll() => Update(new List<TabBarInfo>());

    public void DestroyAll()
    {
        foreach (var tb in _active.Values.Concat(_free)) tb.Destroy();
        _active.Clear();
        _free.Clear();
    }
}

/// <summary>
/// The tabs of a tabbed/stacked container, or the title bar of one window: rounded (titlebar_radius), with the
/// application's icon (titlebar_icons) and buttons that act on the tile (titlebar_buttons). A layered window with
/// per-pixel alpha, so the rounded corners are smooth and the space around them is click-through.
/// </summary>
internal sealed class TabBarWindow : Win32Window
{
    private readonly record struct Hit(Rect Rect, Node Target, string? Button);

    private readonly WindowManager _wm;
    private readonly LayeredSurface _surface = new();
    private readonly List<Hit> _hits = new();
    private TabBarInfo _info;
    private bool _visible, _wanted, _queued, _tracking;
    private (Node? Target, string? Button) _hover, _pressed;

    public TabBarWindow(WindowManager wm)
    {
        _wm = wm;
        Create(C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE | C.WS_EX_LAYERED, C.WS_POPUP, "Mozaic tabs", new Rect(0, 0, 1, 1));
    }

    public void Set(TabBarInfo info)
    {
        _info = info;
        _wanted = true;
        Redraw();
    }

    public void HideBar()
    {
        _wanted = false;
        _hover = _pressed = default;
        if (!_visible) return;
        _visible = false;
        User32.ShowWindow(Handle, C.SW_HIDE);
    }

    /// <summary>Renders once the current message is done (many updates in a row cost one render).</summary>
    public void Redraw()
    {
        if (_queued) return;
        _queued = true;
        Dispatcher.Post(() =>
        {
            _queued = false;
            if (_wanted && IsAlive) Render();
        });
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

    private static uint Blend(uint a, uint b, double t)
    {
        uint Ch(int s) => (uint)Math.Round(((a >> s) & 0xFF) * (1 - t) + ((b >> s) & 0xFF) * t);
        return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
    }

    private unsafe void Render()
    {
        var r = _info.Rect;
        var con = _info.Container;
        var output = Tree.OutputOf(con);
        if (output == null || r.Width <= 0 || r.Height <= 0) return;
        var tabs = _info.IsWindowTitle ? new List<Node> { con } : con.Children.ToList();
        if (tabs.Count == 0) return;

        var cfg = _wm.Config;
        double s = output.Dpi / 96.0;
        int S(double v) => (int)Math.Round(v * s);
        int radius = S(cfg.TitleBarRadius);
        int spacing = radius > 0 ? Math.Max(1, S(2)) : 0;
        int pad = S(6);
        var tree = _wm.Tree;
        var focused = tree.Focused;
        var active = _info.IsWindowTitle ? con : con.FocusStack.FirstOrDefault(n => !n.InFloating);

        _surface.Resize(r.Width, r.Height);
        _surface.Clear();
        _hits.Clear();
        var shapes = new List<(Rect Rect, uint Border)>();
        using (var c = Canvas.Direct(_surface.Hdc, r.Width, r.Height))
        {
            c.SetFont(Fonts.Get(cfg.Font.Family, cfg.Font.Size, output.Dpi));
            for (int i = 0; i < tabs.Count; i++)
            {
                var child = tabs[i];
                int gap = i < tabs.Count - 1 ? spacing : 0;
                Rect tr = _info.Stacked
                    ? new Rect(0, i * _info.RowHeight, r.Width, Math.Min(r.Height, (i + 1) * _info.RowHeight) - gap)
                    : new Rect(r.Width * i / tabs.Count, 0, r.Width * (i + 1) / tabs.Count - gap, r.Height);
                if (tr.Width <= 0 || tr.Height <= 0) continue;
                // Focused: the focus is this tab (or inside it), or a parent container containing this whole bar.
                bool isFocused = focused != null &&
                                 (focused == child || Tree.IsAncestor(child, focused) ||
                                  (focused.Kind != NodeKind.Window && Tree.IsAncestor(focused, child)));
                bool urgent = Tree.WindowsIn(child).Any(w => w.Urgent);
                ClientColor cc = urgent ? cfg.Urgent
                    : isFocused ? cfg.Focused
                    : child == active ? cfg.FocusedInactive
                    : cfg.Unfocused;
                c.Fill(tr, cc.Background);
                int x = tr.Left + pad;

                var window = child.Kind == NodeKind.Window ? child : Tree.DescendFocused(child);
                if (cfg.TitleBarIcons && window.Tag is ManagedWindow mw && tr.Width > S(60))
                {
                    int iconSize = Math.Min(S(16), tr.Height - S(6));
                    var icon = IconCache.Get(mw);
                    if (icon != 0 && iconSize >= 8)
                    {
                        IconCache.Draw(c.Hdc, icon, x, tr.Top + (tr.Height - iconSize) / 2, iconSize);
                        x += iconSize + S(6);
                    }
                }

                int right = tr.Right - S(3);
                if (child == active && window.Kind == NodeKind.Window && cfg.TitleBarButtons.Count > 0)
                {
                    int size = Math.Min(tr.Height - S(4), S(26));
                    int bw = size + S(6);
                    if (tr.Width - (x - tr.Left) > cfg.TitleBarButtons.Count * bw + S(60))
                    {
                        for (int b = cfg.TitleBarButtons.Count - 1; b >= 0; b--)
                        {
                            string name = cfg.TitleBarButtons[b];
                            var br = new Rect(right - bw, tr.Top + (tr.Height - size) / 2, right, tr.Top + (tr.Height - size) / 2 + size);
                            DrawButton(c, name, br, window, cc, _hover.Target == window && _hover.Button == name, s);
                            _hits.Add(new Hit(br, window, name));
                            right = br.Left;
                        }
                        right -= S(4);
                    }
                }
                c.Text(Label(child), new Rect(x, tr.Top, right, tr.Bottom), cc.Text, C.DT_LEFT);
                _hits.Add(new Hit(tr, child, null));
                shapes.Add((tr, cc.Border));
            }
        }
        double borderWidth = Math.Max(1, s);
        foreach (var (rect, border) in shapes) _surface.ApplyRoundRect(rect, radius, border, borderWidth);
        _surface.Present(Handle, r.Left, r.Top);
        if (!_visible)
        {
            User32.SetWindowPos(Handle, C.HWND_TOP, 0, 0, 0, 0, C.SWP_NOMOVE | C.SWP_NOSIZE | C.SWP_NOACTIVATE | C.SWP_SHOWWINDOW);
            _visible = true;
        }
    }

    /// <summary>Simple line icons, drawn with GDI so they are crisp at any scaling.</summary>
    private static void DrawButton(Canvas c, string name, Rect r, Node window, ClientColor cc, bool hover, double s)
    {
        uint fg = cc.Text;
        if (hover)
        {
            uint bg = name == "close" ? 0xC42B1Cu : Blend(cc.Background, cc.Text, 0.2);
            c.RoundRect(r, (int)Math.Round(4 * s), bg);
            if (name == "close") fg = 0xFFFFFF;
        }
        int pw = Math.Max(1, (int)Math.Round(1.2 * s));
        int g = Math.Max(6, (int)Math.Round(10 * s)) & ~1;
        int x0 = r.Left + (r.Width - g) / 2, y0 = r.Top + (r.Height - g) / 2, x1 = x0 + g, y1 = y0 + g;
        int mx = (x0 + x1) / 2, my = (y0 + y1) / 2;
        void Box(int l, int t, int rr, int b)
        {
            c.Line(l, t, rr, t, fg, pw);
            c.Line(rr, t, rr, b, fg, pw);
            c.Line(rr, b, l, b, fg, pw);
            c.Line(l, b, l, t, fg, pw);
        }
        switch (name)
        {
            case "split":
                // Where the next window opens: beside (vertical divider) or below (horizontal divider).
                Box(x0, y0, x1, y1);
                if (window.Parent?.Orientation == Orientation.Vertical) c.Line(x0, my, x1, my, fg, pw);
                else c.Line(mx, y0, mx, y1, fg, pw);
                break;
            case "layout":
                Box(x0, y0, x1, y1);
                c.Line(x0, y0 + g / 3, x1, y0 + g / 3, fg, pw);
                c.Line(x0 + g / 3, y0, x0 + g / 3, y0 + g / 3, fg, pw);
                c.Line(x0 + 2 * g / 3, y0, x0 + 2 * g / 3, y0 + g / 3, fg, pw);
                break;
            case "floating":
                int o = g / 3;
                Box(x0 + o, y0, x1, y1 - o);
                c.Line(x0, y0 + o, x0 + o, y0 + o, fg, pw);
                c.Line(x0, y0 + o, x0, y1, fg, pw);
                c.Line(x0, y1, x1 - o, y1, fg, pw);
                c.Line(x1 - o, y1, x1 - o, y1 - o, fg, pw);
                break;
            case "fullscreen":
                int k = g / 3;
                c.Line(x0, y0, x0 + k, y0, fg, pw); c.Line(x0, y0, x0, y0 + k, fg, pw);
                c.Line(x1, y0, x1 - k, y0, fg, pw); c.Line(x1, y0, x1, y0 + k, fg, pw);
                c.Line(x0, y1, x0 + k, y1, fg, pw); c.Line(x0, y1, x0, y1 - k, fg, pw);
                c.Line(x1, y1, x1 - k, y1, fg, pw); c.Line(x1, y1, x1, y1 - k, fg, pw);
                break;
            case "close":
                c.Line(x0, y0, x1 + 1, y1 + 1, fg, pw);
                c.Line(x1, y0, x0 - 1, y1 + 1, fg, pw);
                break;
        }
    }

    private Hit? HitAt(int x, int y)
    {
        foreach (var h in _hits)
            if (h.Button != null && h.Rect.Contains(x, y)) return h;
        foreach (var h in _hits)
            if (h.Button == null && h.Rect.Contains(x, y)) return h;
        return null;
    }

    private static string CommandFor(string button) => button switch
    {
        "split" => "split toggle",
        "layout" => "layout toggle all",
        "floating" => "floating toggle",
        "fullscreen" => "fullscreen toggle",
        "close" => "kill",
        _ => "nop",
    };

    protected override unsafe bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        if (NoActivate(msg, out result)) return true;
        int x = LoWord(lParam), y = HiWord(lParam);
        switch (msg)
        {
            case C.WM_MOUSEMOVE:
            {
                if (!_tracking)
                {
                    var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = C.TME_LEAVE, hwndTrack = Handle };
                    _tracking = User32.TrackMouseEvent(&tme);
                }
                var h = HitAt(x, y);
                var hover = h is { Button: not null } hb ? (hb.Target, hb.Button) : default((Node?, string?));
                if (hover != _hover)
                {
                    _hover = hover;
                    Redraw();
                }
                return true;
            }
            case C.WM_MOUSELEAVE:
                _tracking = false;
                if (_hover != default((Node?, string?)))
                {
                    _hover = default;
                    Redraw();
                }
                return true;
            case C.WM_LBUTTONDOWN:
            {
                var h = HitAt(x, y);
                if (h is not { } hit) return true;
                if (hit.Button != null)
                {
                    _pressed = (hit.Target, hit.Button);
                }
                else if (hit.Target.Parent != null)
                {
                    _wm.Tree.Focus(Tree.DescendFocused(hit.Target));
                    _wm.RequestFocusSync(false);
                }
                return true;
            }
            case C.WM_LBUTTONUP:
            {
                var pressed = _pressed;
                _pressed = default;
                if (HitAt(x, y) is { Button: not null } hit && hit.Target == pressed.Target && hit.Button == pressed.Button)
                    _wm.RunTileCommand(hit.Target, CommandFor(hit.Button));
                return true;
            }
            case C.WM_MBUTTONUP:
                if (HitAt(x, y) is { } m)
                    foreach (var w in Tree.WindowsIn(m.Target)) WindowOps.Close(w.Handle);
                return true;
            case C.WM_MOUSEWHEEL:
            {
                var con = _info.Container;
                if (_info.IsWindowTitle) return true;
                var activeTab = con.FocusStack.FirstOrDefault(n => !n.InFloating);
                if (activeTab == null || con.Children.Count < 2) return true;
                int i = con.Children.IndexOf(activeTab);
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

    protected override void OnDestroyed() => _surface.Dispose();
}
