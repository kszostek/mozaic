using Mozaic.Core;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>Operations that change real windows. All calls that cross into other processes are asynchronous
/// so that a hung application can never block the window manager.</summary>
internal static unsafe class WindowOps
{
    /// <summary>Marker put into dwExtraInfo of input we synthesize, so our own hooks can recognise it.</summary>
    public const nint InjectedMarker = 0x4D5A4943; // "MZIC"

    private const uint PlaceFlags = C.SWP_NOACTIVATE | C.SWP_NOZORDER | C.SWP_NOOWNERZORDER | C.SWP_ASYNCWINDOWPOS;

    public static void UpdateFrameInsets(ManagedWindow mw)
    {
        var hwnd = mw.Hwnd;
        if (!User32.IsWindowVisible(hwnd) || User32.IsIconic(hwnd) || User32.IsZoomed(hwnd)) return;
        var wr = WindowQuery.WindowRect(hwnd);
        var fr = WindowQuery.FrameRect(hwnd);
        if (fr.IsEmpty || wr.IsEmpty) return;
        var insets = (fr.Left - wr.Left, fr.Top - wr.Top, wr.Right - fr.Right, wr.Bottom - fr.Bottom);
        // Sanity check: insets are small and non-negative.
        if (insets.Item1 is < 0 or > 40 || insets.Item2 is < 0 or > 40 || insets.Item3 is < 0 or > 40 || insets.Item4 is < 0 or > 40) return;
        mw.FrameInsets = insets;
        mw.FrameInsetsKnown = true;
    }

    /// <summary>Moves a window so that its *visible frame* exactly covers <paramref name="target"/>.</summary>
    public static void Place(ManagedWindow mw, Rect target)
    {
        var hwnd = mw.Hwnd;
        if (target.IsEmpty) return;
        UpdateFrameInsets(mw);
        var (l, t, r, b) = mw.FrameInsets;
        int x = target.Left - l, y = target.Top - t, w = target.Width + l + r, h = target.Height + t + b;

        int curDpi = Monitors.DpiForWindow(hwnd);
        int newDpi = Monitors.DpiForRect(target);
        User32.SetWindowPos(hwnd, 0, x, y, w, h, PlaceFlags);
        // When crossing monitors with different scaling the app resizes itself on WM_DPICHANGED;
        // a second request (queued after it) puts it where we want it.
        if (curDpi != newDpi) User32.SetWindowPos(hwnd, 0, x, y, w, h, PlaceFlags);
        mw.LastTarget = target;
        mw.LastPlaced = Environment.TickCount64;
    }

    public static void Hide(ManagedWindow mw)
    {
        if (!User32.IsWindowVisible(mw.Hwnd)) { mw.HiddenByUs = true; return; }
        mw.HiddenByUs = true;
        mw.PendingHides++;
        mw.LastVisibilityOp = Environment.TickCount64;
        User32.ShowWindowAsync(mw.Hwnd, C.SW_HIDE);
    }

    public static void Show(ManagedWindow mw)
    {
        mw.HiddenByUs = false;
        if (User32.IsWindowVisible(mw.Hwnd)) return;
        mw.PendingShows++;
        mw.LastVisibilityOp = Environment.TickCount64;
        User32.ShowWindowAsync(mw.Hwnd, C.SW_SHOWNA);
    }

    public static void Restore(IntPtr hwnd) => User32.ShowWindowAsync(hwnd, C.SW_SHOWNOACTIVATE);

    /// <summary>Gives a window the keyboard focus despite Windows' foreground lock.</summary>
    public static bool Focus(IntPtr hwnd)
    {
        if (hwnd == 0 || !User32.IsWindow(hwnd)) return false;
        if (User32.GetForegroundWindow() == hwnd) return true;
        if (User32.IsIconic(hwnd)) User32.ShowWindowAsync(hwnd, C.SW_RESTORE);

        // A no-op input event makes us "the process that received the last input", which lifts the
        // foreground lock without touching modifier key state (unlike the classic Alt-key trick).
        INPUT input = default;
        input.type = C.INPUT_MOUSE;
        input.U.mi.dwExtraInfo = InjectedMarker;
        User32.SendInput(1, &input, sizeof(INPUT));
        if (User32.SetForegroundWindow(hwnd) && User32.GetForegroundWindow() == hwnd) return true;

        // Fallback: attach to the foreground thread's input queue.
        var fg = User32.GetForegroundWindow();
        uint fgThread = fg != 0 ? WindowQuery.ThreadId(fg) : 0;
        uint self = Kernel32.GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != self && User32.AttachThreadInput(self, fgThread, true);
        try
        {
            User32.BringWindowToTop(hwnd);
            User32.SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) User32.AttachThreadInput(self, fgThread, false);
        }
        return User32.GetForegroundWindow() == hwnd;
    }

    public static void RaiseNoActivate(IntPtr hwnd) =>
        User32.SetWindowPos(hwnd, C.HWND_TOP, 0, 0, 0, 0,
            C.SWP_NOMOVE | C.SWP_NOSIZE | C.SWP_NOACTIVATE | C.SWP_NOOWNERZORDER | C.SWP_ASYNCWINDOWPOS);

    public static void Close(IntPtr hwnd) => User32.PostMessageW(hwnd, C.WM_CLOSE, 0, 0);

    /// <summary>Sets the Windows 11 window border color (0xRRGGBB), or resets it with null.</summary>
    public static void SetBorderColor(ManagedWindow mw, uint? rgb)
    {
        if (mw.AppliedBorderColor == rgb) return;
        uint value = rgb.HasValue ? Canvas.ToColorRef(rgb.Value) : C.DWMWA_COLOR_DEFAULT;
        Dwm.DwmSetWindowAttribute(mw.Hwnd, C.DWMWA_BORDER_COLOR, &value, sizeof(uint));
        mw.AppliedBorderColor = rgb;
    }

    public static void SetCornerPreference(IntPtr hwnd, string style)
    {
        int pref = style switch
        {
            "square" => 1,
            "round" => 2,
            "small" => 3,
            _ => 0,
        };
        Dwm.DwmSetWindowAttribute(hwnd, C.DWMWA_WINDOW_CORNER_PREFERENCE, &pref, sizeof(int));
    }

    /// <summary>Sends a harmless unassigned key (VK 0xE8) so that releasing Win/Alt after a swallowed
    /// hotkey does not open the Start menu or activate a menu bar.</summary>
    public static void SendMaskKey()
    {
        var inputs = stackalloc INPUT[2];
        inputs[0] = default;
        inputs[0].type = C.INPUT_KEYBOARD;
        inputs[0].U.ki.wVk = 0xE8;
        inputs[0].U.ki.dwExtraInfo = InjectedMarker;
        inputs[1] = inputs[0];
        inputs[1].U.ki.dwFlags = C.KEYEVENTF_KEYUP;
        User32.SendInput(2, inputs, sizeof(INPUT));
    }
}
