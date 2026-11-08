using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>Small application icons for the bar. Icons from other processes are shared USER objects and
/// can be drawn directly; icons we extract from executables are cached per path.</summary>
internal static unsafe class IconCache
{
    private const uint WM_GETICON = 0x007F;
    private const int ICON_SMALL = 0, ICON_BIG = 1, ICON_SMALL2 = 2;
    private const int GCLP_HICON = -14, GCLP_HICONSM = -34;
    private const uint DI_NORMAL = 3;

    private static readonly Dictionary<IntPtr, IntPtr> s_byWindow = new();
    private static readonly Dictionary<string, IntPtr> s_byExe = new(StringComparer.OrdinalIgnoreCase);

    public static IntPtr Get(ManagedWindow mw)
    {
        if (s_byWindow.TryGetValue(mw.Hwnd, out var icon)) return icon;
        icon = Query(mw);
        s_byWindow[mw.Hwnd] = icon;
        return icon;
    }

    public static void Forget(IntPtr hwnd) => s_byWindow.Remove(hwnd);

    private static IntPtr Query(ManagedWindow mw)
    {
        var hwnd = mw.Hwnd;
        foreach (int kind in new[] { ICON_SMALL2, ICON_SMALL, ICON_BIG })
        {
            // Never block on a hung application.
            if (User32.SendMessageTimeoutW(hwnd, WM_GETICON, kind, 0, C.SMTO_ABORTIFHUNG, 40, out var r) != 0 && r != 0) return r;
        }
        var cls = User32.GetClassLongPtrW(hwnd, GCLP_HICONSM);
        if (cls != 0) return cls;
        cls = User32.GetClassLongPtrW(hwnd, GCLP_HICON);
        if (cls != 0) return cls;
        var path = WindowQuery.ProcessPath(mw.ClassName == "ApplicationFrameWindow" ? WindowQuery.EffectivePid(hwnd, mw.ClassName) : mw.Pid);
        if (string.IsNullOrEmpty(path)) return 0;
        if (s_byExe.TryGetValue(path, out var cached)) return cached;
        IntPtr small = 0;
        Shell32Icons.ExtractIconExW(path, 0, null, &small, 1);
        s_byExe[path] = small;
        return small;
    }

    public static void Draw(IntPtr hdc, IntPtr icon, int x, int y, int size)
    {
        if (icon != 0) User32.DrawIconEx(hdc, x, y, icon, size, size, 0, 0, DI_NORMAL);
    }
}
