using System.Runtime.InteropServices;
using Mozaic.Core;
using Mozaic.Native;

namespace Mozaic.Platform;

internal sealed record MonitorInfo(IntPtr Handle, string Name, Rect Bounds, Rect WorkArea, bool Primary, int Dpi);

internal static unsafe class Monitors
{
    private static List<MonitorInfo>? s_collect;

    public static List<MonitorInfo> Enumerate()
    {
        s_collect = new List<MonitorInfo>();
        try
        {
            User32.EnumDisplayMonitors(0, 0, &Callback, 0);
            return s_collect.OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
        }
        finally
        {
            s_collect = null;
        }
    }

    [UnmanagedCallersOnly]
    private static int Callback(IntPtr monitor, IntPtr hdc, RECT* rect, IntPtr data)
    {
        try
        {
            var info = Get(monitor);
            if (info != null) s_collect?.Add(info);
        }
        catch (Exception e)
        {
            Log.Error("EnumDisplayMonitors", e);
        }
        return 1;
    }

    public static MonitorInfo? Get(IntPtr monitor)
    {
        MONITORINFOEXW mi = default;
        mi.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!User32.GetMonitorInfoW(monitor, &mi)) return null;
        string device = new string(mi.szDevice).TrimEnd('\0');
        string name = device.StartsWith(@"\\.\", StringComparison.Ordinal) ? device[4..] : device;
        int dpi = 96;
        if (Shcore.GetDpiForMonitor(monitor, C.MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0) dpi = (int)dx;
        return new MonitorInfo(monitor, name, mi.rcMonitor.ToRect(), mi.rcWork.ToRect(),
            (mi.dwFlags & C.MONITORINFOF_PRIMARY) != 0, dpi);
    }

    public static int DpiForRect(Rect r)
    {
        var rc = RECT.From(r);
        var m = User32.MonitorFromRect(&rc, C.MONITOR_DEFAULTTONEAREST);
        return Shcore.GetDpiForMonitor(m, C.MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0 ? (int)dx : 96;
    }

    public static int DpiForWindow(IntPtr hwnd)
    {
        var m = User32.MonitorFromWindow(hwnd, C.MONITOR_DEFAULTTONEAREST);
        return Shcore.GetDpiForMonitor(m, C.MDT_EFFECTIVE_DPI, out uint dx, out _) == 0 && dx > 0 ? (int)dx : 96;
    }

    public static Rect? BoundsForWindow(IntPtr hwnd)
    {
        var m = User32.MonitorFromWindow(hwnd, C.MONITOR_DEFAULTTONEAREST);
        return Get(m)?.Bounds;
    }
}
