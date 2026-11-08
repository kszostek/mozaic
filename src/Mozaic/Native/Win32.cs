using System.Runtime.InteropServices;
using Rect = Mozaic.Core.Rect;

namespace Mozaic.Native;

#pragma warning disable SYSLIB1054, CA1401, IDE1006

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
    public RECT(int l, int t, int r, int b) { Left = l; Top = t; Right = r; Bottom = b; }
    public readonly Rect ToRect() => new(Left, Top, Right, Bottom);
    public static RECT From(Rect r) => new(r.Left, r.Top, r.Right, r.Bottom);
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X, Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx, cy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
    public uint lPrivate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WNDCLASSEXW
{
    public uint cbSize;
    public uint style;
    public delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public IntPtr hInstance;
    public IntPtr hIcon;
    public IntPtr hCursor;
    public IntPtr hbrBackground;
    public char* lpszMenuName;
    public char* lpszClassName;
    public IntPtr hIconSm;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MONITORINFOEXW
{
    public uint cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    public fixed char szDevice[32];
}

[StructLayout(LayoutKind.Sequential)]
internal struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public uint mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public uint mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Explicit)]
internal struct InputUnion
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
}

[StructLayout(LayoutKind.Sequential)]
internal struct INPUT
{
    public uint type;
    public InputUnion U;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PAINTSTRUCT
{
    public IntPtr hdc;
    public int fErase;
    public RECT rcPaint;
    public int fRestore;
    public int fIncUpdate;
    public fixed byte rgbReserved[32];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NOTIFYICONDATAW
{
    public uint cbSize;
    public IntPtr hWnd;
    public uint uID;
    public uint uFlags;
    public uint uCallbackMessage;
    public IntPtr hIcon;
    public fixed char szTip[128];
    public uint dwState;
    public uint dwStateMask;
    public fixed char szInfo[256];
    public uint uTimeoutOrVersion;
    public fixed char szInfoTitle[64];
    public uint dwInfoFlags;
    public Guid guidItem;
    public IntPtr hBalloonIcon;
}

[StructLayout(LayoutKind.Sequential)]
internal struct MEMORYSTATUSEX
{
    public uint dwLength;
    public uint dwMemoryLoad;
    public ulong ullTotalPhys;
    public ulong ullAvailPhys;
    public ulong ullTotalPageFile;
    public ulong ullAvailPageFile;
    public ulong ullTotalVirtual;
    public ulong ullAvailVirtual;
    public ulong ullAvailExtendedVirtual;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    public int fIcon;
    public uint xHotspot;
    public uint yHotspot;
    public IntPtr hbmMask;
    public IntPtr hbmColor;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION
{
    public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TRACKMOUSEEVENT
{
    public uint cbSize;
    public uint dwFlags;
    public IntPtr hwndTrack;
    public uint dwHoverTime;
}

internal static class C
{
    // Window messages
    public const uint WM_CREATE = 0x0001, WM_DESTROY = 0x0002, WM_MOVE = 0x0003, WM_SIZE = 0x0005, WM_ACTIVATE = 0x0006,
        WM_SETFOCUS = 0x0007, WM_KILLFOCUS = 0x0008, WM_PAINT = 0x000F, WM_CLOSE = 0x0010, WM_QUERYENDSESSION = 0x0011,
        WM_QUIT = 0x0012, WM_ERASEBKGND = 0x0014, WM_ENDSESSION = 0x0016, WM_SETTINGCHANGE = 0x001A,
        WM_SETCURSOR = 0x0020, WM_MOUSEACTIVATE = 0x0021, WM_NCDESTROY = 0x0082, WM_NCHITTEST = 0x0084,
        WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105,
        WM_COMMAND = 0x0111, WM_TIMER = 0x0113, WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202,
        WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MBUTTONDOWN = 0x0207,
        WM_MBUTTONUP = 0x0208, WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E, WM_POWERBROADCAST = 0x0218,
        WM_DISPLAYCHANGE = 0x007E, WM_DPICHANGED = 0x02E0, WM_WTSSESSION_CHANGE = 0x02B1, WM_MOUSELEAVE = 0x02A3,
        WM_NULL = 0x0000, WM_APP = 0x8000, WM_USER = 0x0400, WM_GETMINMAXINFO = 0x0024, WM_ACTIVATEAPP = 0x001C;

    // Window styles
    public const uint WS_OVERLAPPED = 0x00000000, WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_MINIMIZE = 0x20000000,
        WS_VISIBLE = 0x10000000, WS_DISABLED = 0x08000000, WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000,
        WS_MAXIMIZE = 0x01000000, WS_CAPTION = 0x00C00000, WS_BORDER = 0x00800000, WS_DLGFRAME = 0x00400000,
        WS_VSCROLL = 0x00200000, WS_HSCROLL = 0x00100000, WS_SYSMENU = 0x00080000, WS_THICKFRAME = 0x00040000,
        WS_MINIMIZEBOX = 0x00020000, WS_MAXIMIZEBOX = 0x00010000, WS_OVERLAPPEDWINDOW = 0x00CF0000;

    public const uint WS_EX_DLGMODALFRAME = 0x00000001, WS_EX_TOPMOST = 0x00000008, WS_EX_TRANSPARENT = 0x00000020,
        WS_EX_TOOLWINDOW = 0x00000080, WS_EX_WINDOWEDGE = 0x00000100, WS_EX_APPWINDOW = 0x00040000,
        WS_EX_LAYERED = 0x00080000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_NOREDIRECTIONBITMAP = 0x00200000;

    public const int GWL_STYLE = -16, GWL_EXSTYLE = -20;
    public const uint GW_OWNER = 4, GW_HWNDPREV = 3, GW_HWNDNEXT = 2;
    public const uint GA_PARENT = 1, GA_ROOT = 2, GA_ROOTOWNER = 3;

    // ShowWindow
    public const int SW_HIDE = 0, SW_SHOWNORMAL = 1, SW_SHOWMINIMIZED = 2, SW_MAXIMIZE = 3, SW_SHOWNOACTIVATE = 4,
        SW_SHOW = 5, SW_MINIMIZE = 6, SW_SHOWMINNOACTIVE = 7, SW_SHOWNA = 8, SW_RESTORE = 9;

    // SetWindowPos
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOREDRAW = 0x0008,
        SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020, SWP_SHOWWINDOW = 0x0040, SWP_HIDEWINDOW = 0x0080,
        SWP_NOCOPYBITS = 0x0100, SWP_NOOWNERZORDER = 0x0200, SWP_NOSENDCHANGING = 0x0400, SWP_ASYNCWINDOWPOS = 0x4000;

    public static readonly IntPtr HWND_TOP = 0, HWND_BOTTOM = 1, HWND_TOPMOST = -1, HWND_NOTOPMOST = -2, HWND_MESSAGE = -3;

    // WinEvents
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MOVESIZESTART = 0x000A, EVENT_SYSTEM_MOVESIZEEND = 0x000B,
        EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017,
        EVENT_OBJECT_CREATE = 0x8000, EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_SHOW = 0x8002, EVENT_OBJECT_HIDE = 0x8003,
        EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_NAMECHANGE = 0x800C,
        EVENT_OBJECT_CLOAKED = 0x8017, EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000, WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const int OBJID_WINDOW = 0, CHILDID_SELF = 0;

    // Hooks
    public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14, HC_ACTION = 0;
    public const uint LLKHF_INJECTED = 0x10, LLKHF_UP = 0x80, LLMHF_INJECTED = 0x01;

    // Input
    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_EXTENDEDKEY = 0x0001;

    // Virtual keys
    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C,
        VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_ESCAPE = 0x1B, VK_RETURN = 0x0D,
        VK_UP = 0x26, VK_DOWN = 0x28, VK_BACK = 0x08, VK_TAB = 0x09, VK_PRIOR = 0x21, VK_NEXT = 0x22,
        VK_HOME = 0x24, VK_END = 0x23, VK_DELETE = 0x2E, VK_LEFT = 0x25, VK_RIGHT = 0x27;

    // DWM
    public const uint DWMWA_CLOAKED = 14, DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_WINDOW_CORNER_PREFERENCE = 33,
        DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_COLOR_DEFAULT = 0xFFFFFFFF, DWMWA_COLOR_NONE = 0xFFFFFFFE;

    // Misc
    public const int MA_NOACTIVATE = 3;
    public const uint MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTONULL = 0, MONITORINFOF_PRIMARY = 1;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const uint CS_HREDRAW = 2, CS_VREDRAW = 1, CS_DBLCLKS = 8, CS_DROPSHADOW = 0x00020000;
    public const int IDC_ARROW = 32512, IDC_HAND = 32649, IDC_SIZEALL = 32646;
    public const int HTCLIENT = 1, HTTRANSPARENT = -1;
    public const uint LWA_COLORKEY = 1, LWA_ALPHA = 2;
    public const uint ULW_ALPHA = 2;
    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    public const uint RDW_INVALIDATE = 0x1, RDW_ERASE = 0x4, RDW_ALLCHILDREN = 0x80, RDW_FRAME = 0x400;
    public const uint SPI_SETWORKAREA = 0x002F;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TOKEN_QUERY = 0x0008;
    public const int TokenElevation = 20;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // Shell_NotifyIcon
    public const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10, NIF_SHOWTIP = 0x80;
    public const uint NIIF_INFO = 1, NIIF_WARNING = 2, NIIF_ERROR = 3;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIN_SELECT = 0x0400, NIN_KEYSELECT = 0x0401;

    // Menus
    public const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 8, MF_GRAYED = 1, MF_DISABLED = 2;
    public const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100, TPM_BOTTOMALIGN = 0x20, TPM_NONOTIFY = 0x80;

    // DrawText
    public const uint DT_LEFT = 0, DT_CENTER = 1, DT_RIGHT = 2, DT_VCENTER = 4, DT_SINGLELINE = 0x20,
        DT_NOPREFIX = 0x800, DT_END_ELLIPSIS = 0x8000, DT_CALCRECT = 0x400, DT_NOCLIP = 0x100,
        DT_WORDBREAK = 0x10;

    // GDI
    public const int TRANSPARENT = 1, OPAQUE = 2;
    public const uint SRCCOPY = 0x00CC0020;
    public const int FW_NORMAL = 400, FW_SEMIBOLD = 600, FW_BOLD = 700;
    public const int NULL_BRUSH = 5, NULL_PEN = 8;
    public const uint BI_RGB = 0, DIB_RGB_COLORS = 0;
    public const uint CLEARTYPE_QUALITY = 5, DEFAULT_CHARSET = 1, OUT_DEFAULT_PRECIS = 0, CLIP_DEFAULT_PRECIS = 0,
        DEFAULT_PITCH = 0;
    public const int PS_SOLID = 0;

    // Power / session
    public const int PBT_APMRESUMEAUTOMATIC = 0x12, PBT_APMRESUMESUSPEND = 0x7;
    public const int WTS_SESSION_UNLOCK = 0x8, WTS_SESSION_LOCK = 0x7;
    public const uint NOTIFY_FOR_THIS_SESSION = 0;

    // Shell hook
    public const int HSHELL_FLASH = 0x8006, HSHELL_RUDEAPPACTIVATED = 0x8004, HSHELL_WINDOWACTIVATED = 4;

    public const uint TME_LEAVE = 2;
    public const uint MB_YESNO = 4, MB_ICONQUESTION = 0x20, MB_ICONERROR = 0x10, MB_ICONINFORMATION = 0x40,
        MB_TOPMOST = 0x40000, MB_SETFOREGROUND = 0x10000, MB_OK = 0;
    public const int IDYES = 6;
}

internal static unsafe partial class User32
{
    private const string Dll = "user32.dll";

    [LibraryImport(Dll, SetLastError = true)]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* wc);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    public static partial IntPtr CreateWindowExW(uint exStyle, string className, string? windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport(Dll)]
    public static partial IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport(Dll)]
    public static partial int GetMessageW(MSG* msg, IntPtr hwnd, uint min, uint max);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* msg);

    [LibraryImport(Dll)]
    public static partial IntPtr DispatchMessageW(MSG* msg);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostThreadMessageW(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport(Dll)]
    public static partial IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport(Dll)]
    public static partial IntPtr SendMessageTimeoutW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [LibraryImport(Dll)]
    public static partial void PostQuitMessage(int code);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr hwnd, int cmd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindowAsync(IntPtr hwnd, int cmd);

    [LibraryImport(Dll, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(IntPtr hwnd, out RECT rect);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsZoomed(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsHungAppWindow(IntPtr hwnd);

    [LibraryImport(Dll)]
    public static partial int GetWindowLongW(IntPtr hwnd, int index);

    [LibraryImport(Dll)]
    public static partial int GetWindowTextW(IntPtr hwnd, char* buffer, int max);

    [LibraryImport(Dll)]
    public static partial int GetWindowTextLengthW(IntPtr hwnd);

    [LibraryImport(Dll)]
    public static partial int GetClassNameW(IntPtr hwnd, char* buffer, int max);

    [LibraryImport(Dll)]
    public static partial IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [LibraryImport(Dll)]
    public static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [LibraryImport(Dll)]
    public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [LibraryImport(Dll)]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint attach, uint attachTo, [MarshalAs(UnmanagedType.Bool)] bool doAttach);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint pid);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr param);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumChildWindows(IntPtr parent, delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr param);

    [LibraryImport(Dll)]
    public static partial IntPtr SetWinEventHook(uint min, uint max, IntPtr module,
        delegate* unmanaged<IntPtr, uint, IntPtr, int, int, uint, uint, void> proc, uint pid, uint tid, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(IntPtr hook);

    [LibraryImport(Dll, SetLastError = true)]
    public static partial IntPtr SetWindowsHookExW(int id, delegate* unmanaged<int, IntPtr, IntPtr, IntPtr> proc, IntPtr module, uint tid);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hook);

    [LibraryImport(Dll)]
    public static partial IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [LibraryImport(Dll)]
    public static partial short GetAsyncKeyState(int vk);

    [LibraryImport(Dll)]
    public static partial short GetKeyState(int vk);

    [LibraryImport(Dll)]
    public static partial uint SendInput(uint count, INPUT* inputs, int size);

    [LibraryImport(Dll)]
    public static partial short VkKeyScanExW(ushort ch, IntPtr layout);

    [LibraryImport(Dll)]
    public static partial IntPtr GetKeyboardLayout(uint threadId);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, delegate* unmanaged<IntPtr, IntPtr, RECT*, IntPtr, int> proc, IntPtr param);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(IntPtr monitor, MONITORINFOEXW* info);

    [LibraryImport(Dll)]
    public static partial IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [LibraryImport(Dll)]
    public static partial IntPtr MonitorFromPoint(POINT pt, uint flags);

    [LibraryImport(Dll)]
    public static partial IntPtr MonitorFromRect(RECT* rect, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT pt);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    [LibraryImport(Dll)]
    public static partial IntPtr WindowFromPoint(POINT pt);

    [LibraryImport(Dll)]
    public static partial nuint SetTimer(IntPtr hwnd, nuint id, uint ms, IntPtr proc);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(IntPtr hwnd, nuint id);

    [LibraryImport(Dll)]
    public static partial IntPtr BeginPaint(IntPtr hwnd, PAINTSTRUCT* ps);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(IntPtr hwnd, PAINTSTRUCT* ps);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(IntPtr hwnd, IntPtr rect, [MarshalAs(UnmanagedType.Bool)] bool erase);

    [LibraryImport(Dll)]
    public static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport(Dll)]
    public static partial int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [LibraryImport(Dll)]
    public static partial int FillRect(IntPtr hdc, RECT* rect, IntPtr brush);

    [LibraryImport(Dll)]
    public static partial int DrawTextW(IntPtr hdc, char* text, int count, RECT* rect, uint format);

    [LibraryImport(Dll)]
    public static partial IntPtr LoadCursorW(IntPtr instance, IntPtr name);

    [LibraryImport(Dll)]
    public static partial IntPtr SetCursor(IntPtr cursor);

    [LibraryImport(Dll)]
    public static partial IntPtr CreateIconIndirect(ICONINFO* info);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport(Dll)]
    public static partial IntPtr CreatePopupMenu();

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(IntPtr menu, uint flags, nuint id, string? text);

    [LibraryImport(Dll)]
    public static partial int TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr hwnd, IntPtr rect);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(IntPtr menu);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string name);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterShellHookWindow(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeregisterShellHookWindow(IntPtr hwnd);

    [LibraryImport(Dll)]
    public static partial IntPtr SetProcessDpiAwarenessContext(IntPtr value);

    [LibraryImport(Dll)]
    public static partial uint GetDpiForWindow(IntPtr hwnd);

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBoxW(IntPtr hwnd, string text, string caption, uint type);

    [LibraryImport(Dll)]
    public static partial IntPtr GetShellWindow();

    [LibraryImport(Dll, SetLastError = true)]
    public static partial IntPtr SetWindowLongPtrW(IntPtr hwnd, int index, IntPtr value);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLayeredWindowAttributes(IntPtr hwnd, out uint key, out byte alpha, out uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, POINT* ptDst, SIZE* size, IntPtr hdcSrc,
        POINT* ptSrc, uint key, BLENDFUNCTION* blend, uint flags);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);

    [LibraryImport(Dll)]
    public static partial IntPtr SetFocus(IntPtr hwnd);

    [LibraryImport(Dll)]
    public static partial IntPtr SetCapture(IntPtr hwnd);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(TRACKMOUSEEVENT* tme);

    [LibraryImport(Dll)]
    public static partial IntPtr GetDesktopWindow();

    [LibraryImport(Dll)]
    public static partial IntPtr GetClassLongPtrW(IntPtr hwnd, int index);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr icon, int cx, int cy, uint step, IntPtr brush, uint flags);
}

internal static unsafe partial class Shell32Icons
{
    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ExtractIconExW(string file, int index, IntPtr* large, IntPtr* small, uint count);
}

internal static unsafe partial class Dwm
{
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmGetWindowAttribute(IntPtr hwnd, uint attr, void* value, uint size);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr hwnd, uint attr, void* value, uint size);
}

internal static unsafe partial class Shcore
{
    [LibraryImport("shcore.dll")]
    public static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}

internal static unsafe partial class Kernel32
{
    private const string Dll = "kernel32.dll";

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr GetModuleHandleW(string? name);

    [LibraryImport(Dll)]
    public static partial uint GetCurrentThreadId();

    [LibraryImport(Dll)]
    public static partial uint GetCurrentProcessId();

    [LibraryImport(Dll, SetLastError = true)]
    public static partial IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr handle);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* buffer, ref uint size);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalMemoryStatusEx(MEMORYSTATUSEX* status);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetDiskFreeSpaceExW(char* dir, out ulong freeToCaller, out ulong total, out ulong totalFree);
}

internal static unsafe partial class Advapi32
{
    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(IntPtr token, int cls, void* info, uint length, out uint returned);
}

internal static unsafe partial class Gdi32
{
    private const string Dll = "gdi32.dll";

    [LibraryImport(Dll, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
        uint quality, uint pitchAndFamily, string face);

    [LibraryImport(Dll)]
    public static partial IntPtr SelectObject(IntPtr hdc, IntPtr obj);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(IntPtr obj);

    [LibraryImport(Dll)]
    public static partial IntPtr CreateSolidBrush(uint color);

    [LibraryImport(Dll)]
    public static partial IntPtr CreatePen(int style, int width, uint color);

    [LibraryImport(Dll)]
    public static partial uint SetTextColor(IntPtr hdc, uint color);

    [LibraryImport(Dll)]
    public static partial int SetBkMode(IntPtr hdc, int mode);

    [LibraryImport(Dll)]
    public static partial IntPtr CreateCompatibleDC(IntPtr hdc);

    [LibraryImport(Dll)]
    public static partial IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(IntPtr hdc);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextExtentPoint32W(IntPtr hdc, char* text, int count, out SIZE size);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveToEx(IntPtr hdc, int x, int y, IntPtr prev);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LineTo(IntPtr hdc, int x, int y);

    [LibraryImport(Dll)]
    public static partial IntPtr CreateDIBSection(IntPtr hdc, BITMAPINFOHEADER* bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [LibraryImport(Dll)]
    public static partial IntPtr CreateBitmap(int w, int h, uint planes, uint bpp, void* bits);

    [LibraryImport(Dll)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RoundRect(IntPtr hdc, int l, int t, int r, int b, int w, int h);

    [LibraryImport(Dll)]
    public static partial IntPtr GetStockObject(int obj);
}

internal static unsafe partial class Shell32
{
    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint message, NOTIFYICONDATAW* data);
}

internal static partial class WtsApi32
{
    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(IntPtr hwnd, uint flags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(IntPtr hwnd);
}
