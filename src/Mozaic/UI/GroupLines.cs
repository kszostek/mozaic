using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>
/// "Current" lines in the gaps between windows of the same tile (a container that splits a part of the workspace),
/// so you can see which windows belong together. The focused window's tile carries the strongest current, the
/// tiles around it a bit less, other tiles less still. The current flows for a moment after the focus or layout changes and
/// while $mod is held (or all the time with "group_lines always"); otherwise the lines are static and cost
/// nothing. Each line is a small layered window (a few pixels wide) placed just below the tiled windows, so
/// it is only visible in the gaps and floating windows stay above it.
/// </summary>
internal sealed class GroupLines
{
    private const int FrameMs = 33;
    private const int BurstMs = 3000;
    private const int FadeMs = 700;
    private const int RampMs = 180;

    private readonly WindowManager _wm;
    private readonly Dictionary<(int Container, int Index), Strip> _strips = new();
    private readonly List<Strip> _spare = new();
    private readonly long _t0 = Environment.TickCount64;
    private nuint _timer;
    private long _burstStart, _burstUntil;
    private bool _modHeld, _suspended, _always;
    private Node? _lastFocus;
    private bool _zOrderDirty;

    public GroupLines(WindowManager wm) => _wm = wm;

    public int Count => _strips.Count;

    public readonly record struct Line(GroupEdge Edge, uint Color, int Dpi);

    /// <summary>Shows exactly these lines (called after every layout pass).</summary>
    public void Update(List<Line> lines, Node? focus, bool always)
    {
        _always = always;
        bool changed = false;
        var seen = new HashSet<(int, int)>();
        foreach (var line in lines)
        {
            var key = (line.Edge.Container.Id, line.Edge.Index);
            seen.Add(key);
            if (!_strips.TryGetValue(key, out var strip))
            {
                strip = TakeStrip();
                _strips[key] = strip;
                strip.Phase = (key.Item1 * 7919 + key.Item2 * 104729) % 1000;
                changed = true;
            }
            if (strip.Set(line.Edge.Rect, line.Edge.Vertical, line.Edge.Level, line.Color, line.Dpi)) changed = true;
        }
        foreach (var key in _strips.Keys.Where(k => !seen.Contains(k)).ToList())
        {
            var s = _strips[key];
            _strips.Remove(key);
            s.HideStrip();
            _spare.Add(s);
            changed = true;
        }
        if (changed) _zOrderDirty = true;
        if (changed || focus != _lastFocus) Kick();
        _lastFocus = focus;
        if (_timer == 0) RenderAll(Environment.TickCount64);
    }

    /// <summary>The focus or the layout changed: let the current flow for a moment.</summary>
    public void Kick()
    {
        if (_strips.Count == 0) return;
        long now = Environment.TickCount64;
        if (Energy(now) <= 0) _burstStart = now;
        _burstUntil = now + BurstMs;
        EnsureTimer();
    }

    public void SetModifierHeld(bool held)
    {
        if (held == _modHeld) return;
        _modHeld = held;
        long now = Environment.TickCount64;
        if (held)
        {
            if (Energy(now) <= 0) _burstStart = now;
            EnsureTimer();
        }
        else
        {
            _burstUntil = Math.Max(_burstUntil, now);
        }
    }

    /// <summary>Stops animating while the session is locked.</summary>
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;
        if (!suspended) EnsureTimer();
    }

    /// <summary>Places the lines just below the lowest tiled window (call when windows appeared or moved).</summary>
    public void KeepBelowTiledWindows()
    {
        if (_strips.Count == 0) return;
        IntPtr bottom = 0;
        foreach (var h in WindowQuery.TopLevelWindows())
            if (_wm.Windows.TryGetValue(h, out var mw) && mw.Node.Visible && !mw.HiddenByUs && !mw.Node.InFloating) bottom = h;
        if (bottom == 0) return;
        foreach (var s in _strips.Values) s.PlaceBelow(bottom);
        _zOrderDirty = false;
    }

    public void HideAll()
    {
        foreach (var s in _strips.Values)
        {
            s.HideStrip();
            _spare.Add(s);
        }
        _strips.Clear();
        Dispatcher.ClearTimer(ref _timer);
    }

    public void DestroyAll()
    {
        Dispatcher.ClearTimer(ref _timer);
        foreach (var s in _strips.Values.Concat(_spare)) s.Destroy();
        _strips.Clear();
        _spare.Clear();
    }

    private Strip TakeStrip()
    {
        if (_spare.Count == 0) return new Strip();
        var s = _spare[^1];
        _spare.RemoveAt(_spare.Count - 1);
        return s;
    }

    private bool Continuous => (_always || _modHeld) && !_suspended;

    private double Energy(long now)
    {
        if (_suspended) return 0;
        double e = Continuous || now < _burstUntil ? 1 : 1 - (now - _burstUntil) / (double)FadeMs;
        double ramp = (now - _burstStart) / (double)RampMs;
        return Math.Clamp(Math.Min(e, ramp), 0, 1);
    }

    private void EnsureTimer()
    {
        if (_timer != 0 || _suspended || _strips.Count == 0) return;
        _timer = Dispatcher.SetInterval(FrameMs, Tick);
    }

    private void Tick()
    {
        long now = Environment.TickCount64;
        RenderAll(now);
        if (Energy(now) <= 0 && !Continuous) Dispatcher.ClearTimer(ref _timer);
    }

    private void RenderAll(long now)
    {
        double energy = Energy(now);
        double t = (now - _t0) / 1000.0;
        foreach (var s in _strips.Values) s.Render(t, energy);
        // New lines are shown only now that they have content, directly at their place in the z-order.
        if (_zOrderDirty) KeepBelowTiledWindows();
    }

    /// <summary>One line: a click-through layered window covering one gap.</summary>
    private sealed unsafe class Strip : Win32Window
    {
        private readonly LayeredSurface _surface = new();
        private Rect _rect;
        private bool _vertical, _shown, _needsShow;
        private int _level, _dpi = 96;
        private uint _color;
        private double _renderedEnergy = -1;

        public double Phase { get; set; }

        public Strip() => Create(C.WS_EX_LAYERED | C.WS_EX_TRANSPARENT | C.WS_EX_TOOLWINDOW | C.WS_EX_NOACTIVATE,
            C.WS_POPUP, "Mozaic group line", new Rect(0, 0, 1, 1));

        /// <summary>Returns true when the line moved or changed.</summary>
        public bool Set(Rect rect, bool vertical, int level, uint color, int dpi)
        {
            if (rect == _rect && vertical == _vertical && level == _level && color == _color && dpi == _dpi && _shown) return false;
            _rect = rect;
            _vertical = vertical;
            _level = level;
            _color = color;
            _dpi = dpi;
            _renderedEnergy = -1;
            if (!_shown) _needsShow = true;
            return true;
        }

        public void PlaceBelow(IntPtr window)
        {
            uint flags = C.SWP_NOMOVE | C.SWP_NOSIZE | C.SWP_NOACTIVATE | C.SWP_NOOWNERZORDER;
            if (_needsShow && _renderedEnergy >= 0)
            {
                flags |= C.SWP_SHOWWINDOW;
                _needsShow = false;
                _shown = true;
            }
            User32.SetWindowPos(Handle, window, 0, 0, 0, 0, flags);
        }

        public void HideStrip()
        {
            _needsShow = false;
            if (!_shown) return;
            _shown = false;
            User32.ShowWindow(Handle, C.SW_HIDE);
        }

        public void Render(double t, double energy)
        {
            // The focused window's tile carries the full current, the tiles around it a little less, other tiles less still.
            double e = energy * _level switch { 0 => 1.0, 1 => 0.8, _ => 0.55 };
            if (e <= 0 && _renderedEnergy == 0) return;
            int w = _rect.Width, h = _rect.Height;
            if (w <= 0 || h <= 0) return;
            _surface.Resize(w, h);
            int len = _vertical ? h : w, thick = _vertical ? w : h;
            double s = _dpi / 96.0;
            double baseA = _level switch { 0 => 0.95, 1 => 0.8, _ => 0.6 };
            double speed = 330 * s, spacing = 210 * s, tail = 150 * s;
            double center = thick / 2.0, halfCore = Math.Clamp(thick / 3.0, 0.75, 1.3 * s);
            double sigma = Math.Max(1.4, thick / 2.6);
            double jitterAmp = Math.Min(1.8 * s, thick / 3.0);
            double cr = (_color >> 16) & 0xFF, cg = (_color >> 8) & 0xFF, cb = _color & 0xFF;
            double head = t * speed + Phase;
            // A second, faster and thinner train of sparks running over the main pulses.
            double head2 = t * speed * 1.9 + Phase * 1.3, spacing2 = spacing * 1.7, tail2 = tail * 0.45;
            uint* bits = _surface.Bits;
            for (int i = 0; i < len; i++)
            {
                double p = 0;
                if (e > 0)
                {
                    // Distance behind the nearest pulse head: a sharp front and a long fading tail.
                    double u = (head - i) % spacing;
                    if (u < 0) u += spacing;
                    double p1 = u < tail ? Math.Exp(-u * 2.6 / tail) : Math.Exp(-(spacing - u) / (2.5 * s));
                    double u2 = (head2 - i) % spacing2;
                    if (u2 < 0) u2 += spacing2;
                    double p2 = u2 < tail2 ? Math.Exp(-u2 * 3 / tail2) : 0;
                    p = Math.Min(1, (p1 + p2 * 0.55) * e);
                }
                double c = center;
                if (p > 0.03) c += (Math.Sin(i * 0.9 + t * 37) * 0.6 + Math.Sin(i * 0.23 - t * 23) * 0.4) * p * jitterAmp;
                for (int k = 0; k < thick; k++)
                {
                    double d = Math.Abs(k + 0.5 - c);
                    double core = d < halfCore ? 1 - d / halfCore * 0.35 : Math.Max(0, 0.65 - (d - halfCore) * 0.75);
                    double glow = Math.Exp(-(d * d) / (2 * sigma * sigma));
                    double a = Math.Min(1, baseA * (core + glow * 0.18) + p * (core + glow * 0.95));
                    double white = Math.Min(1, p * (core * 0.95 + glow * 0.2));
                    uint A = (uint)(a * 255);
                    uint r = (uint)((cr + (255 - cr) * white) * a), g = (uint)((cg + (255 - cg) * white) * a), b = (uint)((cb + (255 - cb) * white) * a);
                    int x = _vertical ? k : i, y = _vertical ? i : k;
                    bits[y * w + x] = (A << 24) | (r << 16) | (g << 8) | b;
                }
            }
            _surface.Present(Handle, _rect.Left, _rect.Top);
            _renderedEnergy = e;
        }

        protected override void OnDestroyed() => _surface.Dispose();

        protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
        {
            if (NoActivate(msg, out result)) return true;
            if (msg == C.WM_NCHITTEST)
            {
                result = C.HTTRANSPARENT;
                return true;
            }
            return false;
        }
    }
}
