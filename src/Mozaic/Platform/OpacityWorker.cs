using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>
/// Makes windows translucent (inactive_opacity) with WS_EX_LAYERED + SetLayeredWindowAttributes, fading
/// smoothly between levels. Changing another program's window style sends that window messages
/// synchronously, so all of it runs on a thread of its own: a hung application can only delay its own fade,
/// never the window manager. The windows made translucent are listed in a file, so that if Mozaic is killed
/// the next start makes them opaque again (like hidden windows, no window is ever left half-transparent).
/// </summary>
internal sealed class OpacityWorker(string path)
{
    /// <summary>Fading back to fully visible is quick; dimming is gentler.</summary>
    private const int FadeInMs = 160, FadeOutMs = 320, FrameMs = 15;

    private readonly record struct Target(IntPtr Hwnd, byte Alpha, uint Pid, string ClassName);

    /// <summary>A window we made layered: its current opacity and the fade it is in.</summary>
    private sealed class Layered(Target window)
    {
        public Target Window = window;
        public double From = 255, To = 255, Current = 255;
        public long Start;
        public bool Moving => Current != To;
    }

    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, Target> _pending = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private Thread? _thread;
    private volatile bool _stopping;

    // Every window that may be layered by us, readable from any thread (to make them visible at once on exit).
    private readonly ConcurrentDictionary<IntPtr, byte> _touched = new();

    // Worker thread only.
    private readonly Dictionary<IntPtr, Layered> _layered = new();
    private readonly HashSet<IntPtr> _theirs = new(); // already layered by their application: left alone

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Mozaic opacity" };
        _thread.Start();
    }

    /// <summary>Requests an opacity (255 = opaque) for a window. Main thread; cheap when nothing changes.</summary>
    public void Set(ManagedWindow mw, byte alpha)
    {
        if (mw.RequestedAlpha == alpha) return;
        mw.RequestedAlpha = alpha;
        lock (_lock) _pending[mw.Hwnd] = new Target(mw.Hwnd, alpha, mw.Pid, mw.ClassName);
        _signal.Set();
    }

    /// <summary>
    /// Makes every window we touched fully visible right now, from any thread. It cannot block: setting the
    /// opacity of a layered window does not wait for the window's application (removing the style does,
    /// which is left to the worker).
    /// </summary>
    public void ForceOpaqueNow()
    {
        foreach (var h in _touched.Keys)
            if (User32.IsWindow(h)) User32.SetLayeredWindowAttributes(h, 0, 255, C.LWA_ALPHA);
    }

    /// <summary>Makes every window opaque again and stops the thread (waits at most <paramref name="timeoutMs"/>).</summary>
    public void Shutdown(int timeoutMs)
    {
        if (_thread == null) return;
        _stopping = true;
        ForceOpaqueNow();
        _signal.Set();
        if (!_thread.Join(timeoutMs)) Log.Warn("opacity: a window did not respond while its style was restored; the next start will retry");
        _thread = null;
    }

    private long Now => _clock.ElapsedMilliseconds;

    private void Run()
    {
        try
        {
            RecoverLeftovers();
            while (true)
            {
                bool moving = _layered.Values.Any(l => l.Moving);
                _signal.WaitOne(moving ? FrameMs : Timeout.Infinite);
                List<Target> batch;
                lock (_lock)
                {
                    batch = _pending.Values.ToList();
                    _pending.Clear();
                }
                if (_stopping)
                {
                    // First everything visible (fast), then the styles back (may wait for each application).
                    foreach (var h in _layered.Keys)
                        if (User32.IsWindow(h)) User32.SetLayeredWindowAttributes(h, 0, 255, C.LWA_ALPHA);
                    foreach (var h in _layered.Keys.ToList()) MakeOpaque(h);
                    Save();
                    return;
                }
                bool changed = false;
                foreach (var t in batch) changed |= Retarget(t);
                changed |= Step();
                if (changed) Save();
            }
        }
        catch (Exception e)
        {
            Log.Error("opacity worker", e);
        }
    }

    /// <summary>Starts a fade towards the requested opacity. Returns true when the set of layered windows changed.</summary>
    private bool Retarget(Target t)
    {
        var hwnd = t.Hwnd;
        if (!User32.IsWindow(hwnd))
        {
            _theirs.Remove(hwnd);
            _touched.TryRemove(hwnd, out _);
            return _layered.Remove(hwnd);
        }
        bool added = false;
        if (!_layered.TryGetValue(hwnd, out var l))
        {
            if (t.Alpha == 255 || _theirs.Contains(hwnd) || User32.IsHungAppWindow(hwnd)) return false;
            int ex = User32.GetWindowLongW(hwnd, C.GWL_EXSTYLE);
            if ((ex & (int)C.WS_EX_LAYERED) != 0)
            {
                // The application draws itself translucent (or with UpdateLayeredWindow): don't interfere.
                _theirs.Add(hwnd);
                return false;
            }
            _touched[hwnd] = 0;
            User32.SetWindowLongPtrW(hwnd, C.GWL_EXSTYLE, ex | (int)C.WS_EX_LAYERED);
            User32.SetLayeredWindowAttributes(hwnd, 0, 255, C.LWA_ALPHA);
            _layered[hwnd] = l = new Layered(t);
            added = true;
        }
        l.Window = t;
        if (l.To == t.Alpha) return added;
        l.From = l.Current;
        l.To = t.Alpha;
        l.Start = Now;
        return added;
    }

    /// <summary>Advances every fade by one frame. Returns true when the set of layered windows changed.</summary>
    private bool Step()
    {
        bool changed = false;
        long now = Now;
        foreach (var l in _layered.Values.Where(l => l.Moving).ToList())
        {
            var hwnd = l.Window.Hwnd;
            if (!User32.IsWindow(hwnd))
            {
                _layered.Remove(hwnd);
                _touched.TryRemove(hwnd, out _);
                changed = true;
                continue;
            }
            double duration = l.To > l.From ? FadeInMs : FadeOutMs;
            double p = Math.Clamp((now - l.Start) / duration, 0, 1);
            double eased = 1 - Math.Pow(1 - p, 3); // ease-out: quick start, soft landing
            l.Current = p >= 1 ? l.To : l.From + (l.To - l.From) * eased;
            User32.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(l.Current), C.LWA_ALPHA);
            if (p >= 1 && l.To >= 255)
            {
                MakeOpaque(hwnd); // fully visible again: no reason to stay layered
                changed = true;
            }
        }
        return changed;
    }

    private void MakeOpaque(IntPtr hwnd)
    {
        _layered.Remove(hwnd);
        _touched.TryRemove(hwnd, out _);
        if (!User32.IsWindow(hwnd)) return;
        User32.SetLayeredWindowAttributes(hwnd, 0, 255, C.LWA_ALPHA);
        // Removing the style makes the window repaint, which waits for its application: skip windows that hang.
        if (User32.IsHungAppWindow(hwnd)) return;
        int ex = User32.GetWindowLongW(hwnd, C.GWL_EXSTYLE);
        if ((ex & (int)C.WS_EX_LAYERED) == 0) return;
        User32.SetWindowLongPtrW(hwnd, C.GWL_EXSTYLE, ex & ~(int)C.WS_EX_LAYERED);
        User32.RedrawWindow(hwnd, 0, 0, C.RDW_INVALIDATE | C.RDW_ERASE | C.RDW_FRAME | C.RDW_ALLCHILDREN);
    }

    /// <summary>Windows a previous (killed) Mozaic left translucent are made opaque again.</summary>
    private void RecoverLeftovers()
    {
        List<Target> list;
        try
        {
            if (!File.Exists(path)) return;
            list = File.ReadAllLines(path).Select(Parse).Where(t => t.Hwnd != 0).ToList();
        }
        catch (Exception e)
        {
            Log.Warn("cannot read translucent window list: " + e.Message);
            return;
        }
        foreach (var t in list)
        {
            if (!User32.IsWindow(t.Hwnd) || WindowQuery.Pid(t.Hwnd) != t.Pid || WindowQuery.ClassName(t.Hwnd) != t.ClassName) continue;
            // Only undo what we did: a constant-alpha layered window, not one using a color key.
            if (!User32.GetLayeredWindowAttributes(t.Hwnd, out _, out _, out uint flags) || (flags & C.LWA_COLORKEY) != 0) continue;
            MakeOpaque(t.Hwnd);
            Log.Info("made a window opaque again that a previous run left translucent: " + WindowQuery.Describe(t.Hwnd));
        }
        Save();
    }

    private static Target Parse(string line)
    {
        var p = line.Split('\t');
        if (p.Length < 3 || !long.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long h) ||
            !uint.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint pid))
            return default;
        return new Target((IntPtr)h, 0, pid, p[2]);
    }

    private void Save()
    {
        try
        {
            if (_layered.Count == 0)
            {
                File.Delete(path);
                return;
            }
            var sb = new StringBuilder();
            foreach (var l in _layered.Values)
                sb.Append(((long)l.Window.Hwnd).ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(l.Window.Pid.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(l.Window.ClassName).Append('\n');
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warn("cannot write translucent window list: " + e.Message);
        }
    }
}
