using System.Collections.Concurrent;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>
/// Runs work on the main (UI) thread. All window-manager state is owned by that thread;
/// hooks, IPC and status threads post closures here.
/// </summary>
internal static class Dispatcher
{
    public const uint WM_DISPATCH = C.WM_APP + 1;

    private static readonly ConcurrentQueue<Action> s_queue = new();
    private static readonly Dictionary<nuint, (Action Action, bool Repeat)> s_timers = new();
    private static IntPtr s_hwnd;
    private static nuint s_nextTimer = 1000;
    private static int s_mainThread;
    private static int s_posted;

    public static bool IsMainThread => Environment.CurrentManagedThreadId == s_mainThread;

    public static void Init(IntPtr hwnd)
    {
        s_hwnd = hwnd;
        s_mainThread = Environment.CurrentManagedThreadId;
    }

    public static void Post(Action action)
    {
        s_queue.Enqueue(action);
        if (Interlocked.Exchange(ref s_posted, 1) == 0)
            User32.PostMessageW(s_hwnd, WM_DISPATCH, 0, 0);
    }

    /// <summary>Runs a function on the main thread and waits for the result (background threads only).</summary>
    public static T Invoke<T>(Func<T> func, int timeoutMs = 10000)
    {
        if (IsMainThread) return func();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception e) { tcs.SetException(e); }
        });
        if (!tcs.Task.Wait(timeoutMs)) throw new TimeoutException("main thread did not respond");
        return tcs.Task.Result;
    }

    public static void Drain()
    {
        Interlocked.Exchange(ref s_posted, 0);
        int budget = 512;
        while (budget-- > 0 && s_queue.TryDequeue(out var a))
        {
            try { a(); }
            catch (Exception e) { Log.Error("dispatched action", e); }
        }
        if (!s_queue.IsEmpty && Interlocked.Exchange(ref s_posted, 1) == 0)
            User32.PostMessageW(s_hwnd, WM_DISPATCH, 0, 0);
    }

    public static nuint SetTimeout(int ms, Action action) => AddTimer(ms, action, false);

    public static nuint SetInterval(int ms, Action action) => AddTimer(ms, action, true);

    private static nuint AddTimer(int ms, Action action, bool repeat)
    {
        var id = s_nextTimer++;
        s_timers[id] = (action, repeat);
        User32.SetTimer(s_hwnd, id, (uint)Math.Max(1, ms), 0);
        return id;
    }

    public static void ClearTimer(ref nuint id)
    {
        if (id == 0) return;
        User32.KillTimer(s_hwnd, id);
        s_timers.Remove(id);
        id = 0;
    }

    public static bool OnTimer(nuint id)
    {
        if (!s_timers.TryGetValue(id, out var t)) return false;
        if (!t.Repeat)
        {
            User32.KillTimer(s_hwnd, id);
            s_timers.Remove(id);
        }
        try { t.Action(); }
        catch (Exception e) { Log.Error("timer", e); }
        return true;
    }
}
