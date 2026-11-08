using System.Globalization;
using System.Text;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>
/// Makes windows translucent (inactive_opacity) with WS_EX_LAYERED + SetLayeredWindowAttributes.
/// Changing another program's window style sends that window messages synchronously, so all of it runs on
/// a thread of its own: a hung application can only delay its own fade, never the window manager.
/// The windows made translucent are listed in a file, so that if Mozaic is killed the next start makes
/// them opaque again (like hidden windows, no window is ever left half-transparent).
/// </summary>
internal sealed class OpacityWorker(string path)
{
    private readonly record struct Target(IntPtr Hwnd, byte Alpha, uint Pid, string ClassName);

    private readonly object _lock = new();
    private readonly Dictionary<IntPtr, Target> _pending = new();
    private readonly AutoResetEvent _signal = new(false);
    private Thread? _thread;
    private volatile bool _stopping;

    // Worker thread only.
    private readonly Dictionary<IntPtr, Target> _layered = new();   // windows we made layered
    private readonly HashSet<IntPtr> _theirs = new();               // already layered by their application: left alone

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

    /// <summary>Makes every window opaque again and stops the thread (waits at most <paramref name="timeoutMs"/>).</summary>
    public void Shutdown(int timeoutMs)
    {
        if (_thread == null) return;
        _stopping = true;
        _signal.Set();
        if (!_thread.Join(timeoutMs)) Log.Warn("opacity: a window did not respond while being made opaque; the next start will retry");
        _thread = null;
    }

    private void Run()
    {
        try
        {
            RecoverLeftovers();
            while (true)
            {
                _signal.WaitOne();
                List<Target> batch;
                lock (_lock)
                {
                    batch = _pending.Values.ToList();
                    _pending.Clear();
                }
                if (_stopping)
                {
                    foreach (var t in _layered.Values.ToList()) MakeOpaque(t.Hwnd);
                    Save();
                    return;
                }
                bool changed = false;
                foreach (var t in batch) changed |= Apply(t);
                if (changed) Save();
            }
        }
        catch (Exception e)
        {
            Log.Error("opacity worker", e);
        }
    }

    /// <summary>Returns true when the set of layered windows changed.</summary>
    private bool Apply(Target t)
    {
        var hwnd = t.Hwnd;
        if (!User32.IsWindow(hwnd))
        {
            _theirs.Remove(hwnd);
            return _layered.Remove(hwnd);
        }
        if (t.Alpha == 255)
        {
            if (!_layered.ContainsKey(hwnd)) return false;
            MakeOpaque(hwnd);
            return true;
        }
        if (_theirs.Contains(hwnd) || User32.IsHungAppWindow(hwnd)) return false;
        bool added = false;
        if (!_layered.ContainsKey(hwnd))
        {
            int ex = User32.GetWindowLongW(hwnd, C.GWL_EXSTYLE);
            if ((ex & (int)C.WS_EX_LAYERED) != 0)
            {
                // The application draws itself translucent (or with UpdateLayeredWindow): don't interfere.
                _theirs.Add(hwnd);
                return false;
            }
            User32.SetWindowLongPtrW(hwnd, C.GWL_EXSTYLE, ex | (int)C.WS_EX_LAYERED);
            added = true;
        }
        User32.SetLayeredWindowAttributes(hwnd, 0, t.Alpha, C.LWA_ALPHA);
        _layered[hwnd] = t;
        return added;
    }

    private void MakeOpaque(IntPtr hwnd)
    {
        _layered.Remove(hwnd);
        if (!User32.IsWindow(hwnd)) return;
        int ex = User32.GetWindowLongW(hwnd, C.GWL_EXSTYLE);
        if ((ex & (int)C.WS_EX_LAYERED) == 0) return;
        User32.SetWindowLongPtrW(hwnd, C.GWL_EXSTYLE, ex & ~(int)C.WS_EX_LAYERED);
        // A window that stops being layered has to repaint itself.
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
            if (User32.IsHungAppWindow(t.Hwnd)) continue;
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
            foreach (var t in _layered.Values)
                sb.Append(((long)t.Hwnd).ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(t.Pid.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(t.ClassName).Append('\n');
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
