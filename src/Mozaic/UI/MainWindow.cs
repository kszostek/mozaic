using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>
/// Invisible top-level window that owns the main thread's plumbing: dispatcher, timers,
/// display/work-area/power/session notifications, the tray icon callbacks and the shell hook.
/// (A message-only window would not receive broadcast messages such as WM_DISPLAYCHANGE.)
/// </summary>
internal sealed class MainWindow : Win32Window
{
    public const uint WM_TRAY = C.WM_APP + 2;

    private readonly WindowManager _wm;
    private readonly uint _taskbarCreated;
    private readonly uint _shellHook;

    public MainWindow(WindowManager wm)
    {
        _wm = wm;
        _taskbarCreated = User32.RegisterWindowMessageW("TaskbarCreated");
        _shellHook = User32.RegisterWindowMessageW("SHELLHOOK");
        Create(C.WS_EX_TOOLWINDOW, C.WS_POPUP, "Mozaic", new Rect(0, 0, 1, 1));
        User32.RegisterShellHookWindow(Handle);
        WtsApi32.WTSRegisterSessionNotification(Handle, C.NOTIFY_FOR_THIS_SESSION);
    }

    public Action<IntPtr, IntPtr>? TrayCallback { get; set; }
    public Action<int>? MenuCommand { get; set; }
    public Action? TaskbarCreated { get; set; }

    protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = 0;
        switch (msg)
        {
            case Dispatcher.WM_DISPATCH:
                Dispatcher.Drain();
                return true;
            case C.WM_TIMER:
                Dispatcher.OnTimer((nuint)wParam);
                return true;
            case C.WM_DISPLAYCHANGE:
                _wm.OnDisplayChanged("display change");
                return true;
            case C.WM_SETTINGCHANGE when (uint)wParam == C.SPI_SETWORKAREA:
                _wm.OnDisplayChanged("work area change");
                return true;
            case C.WM_POWERBROADCAST when (int)wParam is C.PBT_APMRESUMEAUTOMATIC or C.PBT_APMRESUMESUSPEND:
                _wm.OnDisplayChanged("resume");
                result = 1;
                return true;
            case C.WM_WTSSESSION_CHANGE when (int)wParam == C.WTS_SESSION_UNLOCK:
                _wm.OnDisplayChanged("session unlock");
                return true;
            case C.WM_QUERYENDSESSION:
                // Make every hidden window visible so that apps can show "save changes?" prompts.
                _wm.OnEndSession();
                result = 1;
                return true;
            case WM_TRAY:
                TrayCallback?.Invoke(wParam, lParam);
                return true;
            case C.WM_COMMAND:
                MenuCommand?.Invoke(LoWord(wParam));
                return true;
        }
        if (msg == _taskbarCreated && _taskbarCreated != 0)
        {
            TaskbarCreated?.Invoke();
            return true;
        }
        if (msg == _shellHook && _shellHook != 0)
        {
            int code = (int)wParam & 0x7FFF;
            if ((int)wParam == C.HSHELL_FLASH || code == (C.HSHELL_FLASH & 0x7FFF))
                _wm.OnWindowFlash(lParam);
            return true;
        }
        return false;
    }

    public void Unregister()
    {
        if (!IsAlive) return;
        User32.DeregisterShellHookWindow(Handle);
        WtsApi32.WTSUnRegisterSessionNotification(Handle);
    }
}
