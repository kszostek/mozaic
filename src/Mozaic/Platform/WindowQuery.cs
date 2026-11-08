using System.Runtime.InteropServices;
using Mozaic.Core;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>Read-only queries about arbitrary top-level windows.</summary>
internal static unsafe class WindowQuery
{
    private static readonly Dictionary<uint, (string Name, long Ticks)> s_processNames = new();
    private static readonly Dictionary<uint, (bool Elevated, long Ticks)> s_elevation = new();
    private static readonly uint s_ownPid = Kernel32.GetCurrentProcessId();
    private static bool? s_selfElevated;
    private static List<IntPtr>? s_children;

    public static uint OwnPid => s_ownPid;

    public static string Title(IntPtr hwnd)
    {
        int len = User32.GetWindowTextLengthW(hwnd);
        if (len <= 0) return "";
        len = Math.Min(len, 1024);
        var buf = stackalloc char[len + 1];
        int n = User32.GetWindowTextW(hwnd, buf, len + 1);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    public static string ClassName(IntPtr hwnd)
    {
        var buf = stackalloc char[257];
        int n = User32.GetClassNameW(hwnd, buf, 257);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    public static uint Style(IntPtr hwnd) => (uint)User32.GetWindowLongW(hwnd, C.GWL_STYLE);
    public static uint ExStyle(IntPtr hwnd) => (uint)User32.GetWindowLongW(hwnd, C.GWL_EXSTYLE);

    public static uint Pid(IntPtr hwnd)
    {
        User32.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    public static uint ThreadId(IntPtr hwnd) => User32.GetWindowThreadProcessId(hwnd, out _);

    public static IntPtr Owner(IntPtr hwnd) => User32.GetWindow(hwnd, C.GW_OWNER);

    public static bool IsCloaked(IntPtr hwnd)
    {
        int cloaked = 0;
        return Dwm.DwmGetWindowAttribute(hwnd, C.DWMWA_CLOAKED, &cloaked, sizeof(int)) == 0 && cloaked != 0;
    }

    public static Rect WindowRect(IntPtr hwnd) => User32.GetWindowRect(hwnd, out var r) ? r.ToRect() : default;

    /// <summary>The visible frame (without the invisible resize borders Windows 10/11 adds).</summary>
    public static Rect FrameRect(IntPtr hwnd)
    {
        RECT r;
        if (Dwm.DwmGetWindowAttribute(hwnd, C.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT)) == 0)
            return r.ToRect();
        return WindowRect(hwnd);
    }

    public static string ProcessName(uint pid) => Path.GetFileName(ProcessPath(pid));

    /// <summary>Full path of the process image (cached for 30 s).</summary>
    public static string ProcessPath(uint pid)
    {
        if (pid == 0) return "";
        long now = Environment.TickCount64;
        if (s_processNames.TryGetValue(pid, out var cached) && now - cached.Ticks < 30_000) return cached.Name;
        string path = "";
        var h = Kernel32.OpenProcess(C.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h != 0)
        {
            var buf = stackalloc char[1024];
            uint size = 1024;
            if (Kernel32.QueryFullProcessImageNameW(h, 0, buf, ref size))
                path = new string(buf, 0, (int)size);
            Kernel32.CloseHandle(h);
        }
        s_processNames[pid] = (path, now);
        return path;
    }

    /// <summary>The process that really owns a window: UWP apps hosted by ApplicationFrameHost resolve to the app.</summary>
    public static uint EffectivePid(IntPtr hwnd, string className)
    {
        uint pid = Pid(hwnd);
        if (className == "ApplicationFrameWindow")
        {
            foreach (var child in Children(hwnd))
            {
                if (ClassName(child) != "Windows.UI.Core.CoreWindow") continue;
                uint cpid = Pid(child);
                if (cpid != pid && cpid != 0) return cpid;
            }
        }
        return pid;
    }

    /// <summary>Process name, resolving UWP apps hosted by ApplicationFrameHost to the real app process.</summary>
    public static string EffectiveProcessName(IntPtr hwnd, string className) => ProcessName(EffectivePid(hwnd, className));

    private static List<IntPtr> Children(IntPtr hwnd)
    {
        var list = new List<IntPtr>();
        s_children = list;
        try { User32.EnumChildWindows(hwnd, &ChildCallback, 0); }
        finally { s_children = null; }
        return list;
    }

    [UnmanagedCallersOnly]
    private static int ChildCallback(IntPtr hwnd, IntPtr param)
    {
        s_children?.Add(hwnd);
        return 1;
    }

    public static bool SelfElevated
    {
        get
        {
            s_selfElevated ??= QueryElevation(Kernel32.GetCurrentProcessId()) == true;
            return s_selfElevated.Value;
        }
    }

    /// <summary>True when the process runs elevated (and we can't control it from a non-elevated process).</summary>
    public static bool IsElevated(uint pid)
    {
        long now = Environment.TickCount64;
        if (s_elevation.TryGetValue(pid, out var c) && now - c.Ticks < 60_000) return c.Elevated;
        bool elevated = QueryElevation(pid) ?? true; // access denied ⇒ most likely elevated
        s_elevation[pid] = (elevated, now);
        return elevated;
    }

    private static bool? QueryElevation(uint pid)
    {
        var h = Kernel32.OpenProcess(C.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == 0) return null;
        try
        {
            if (!Advapi32.OpenProcessToken(h, C.TOKEN_QUERY, out var token)) return null;
            try
            {
                int elevation = 0;
                if (!Advapi32.GetTokenInformation(token, C.TokenElevation, &elevation, sizeof(int), out _)) return null;
                return elevation != 0;
            }
            finally
            {
                Kernel32.CloseHandle(token);
            }
        }
        finally
        {
            Kernel32.CloseHandle(h);
        }
    }

    public static List<IntPtr> TopLevelWindows()
    {
        var list = new List<IntPtr>();
        s_children = list;
        try { User32.EnumWindows(&ChildCallback, 0); }
        finally { s_children = null; }
        return list;
    }

    public static string Describe(IntPtr hwnd) =>
        $"0x{hwnd:X} [{ClassName(hwnd)}] \"{Title(hwnd)}\" ({ProcessName(Pid(hwnd))})";

    public static int LastError => Marshal.GetLastPInvokeError();
}
