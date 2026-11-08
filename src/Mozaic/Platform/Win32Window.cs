using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mozaic.Core;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>Base class for Mozaic's own native windows (bar, tab bars, tray owner, overlays, launcher).</summary>
internal abstract unsafe class Win32Window
{
    private const string ClassName = "MozaicWindow";
    private static readonly Dictionary<IntPtr, Win32Window> s_windows = new();
    private static Win32Window? s_creating;
    private static bool s_registered;
    private static IntPtr s_classNamePtr;

    public IntPtr Handle { get; private set; }
    public bool IsAlive => Handle != 0;

    public static IntPtr Instance { get; } = Kernel32.GetModuleHandleW(null);

    public static bool IsOwnWindow(IntPtr hwnd) => s_windows.ContainsKey(hwnd);

    private static void EnsureClass()
    {
        if (s_registered) return;
        s_classNamePtr = Marshal.StringToHGlobalUni(ClassName);
        var wc = new WNDCLASSEXW
        {
            cbSize = (uint)sizeof(WNDCLASSEXW),
            style = C.CS_DBLCLKS,
            lpfnWndProc = &StaticWndProc,
            hInstance = Instance,
            hCursor = User32.LoadCursorW(0, C.IDC_ARROW),
            lpszClassName = (char*)s_classNamePtr,
        };
        if (User32.RegisterClassExW(&wc) == 0)
            throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastPInvokeError());
        s_registered = true;
    }

    protected void Create(uint exStyle, uint style, string title, Rect r, IntPtr parent = 0)
    {
        EnsureClass();
        s_creating = this;
        try
        {
            Handle = User32.CreateWindowExW(exStyle, ClassName, title, style, r.Left, r.Top, Math.Max(1, r.Width), Math.Max(1, r.Height),
                parent, 0, Instance, 0);
        }
        finally
        {
            s_creating = null;
        }
        if (Handle == 0) throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastPInvokeError());
        s_windows[Handle] = this;
    }

    public void Destroy()
    {
        if (Handle != 0) User32.DestroyWindow(Handle);
    }

    public void Invalidate()
    {
        if (Handle != 0) User32.InvalidateRect(Handle, 0, false);
    }

    [UnmanagedCallersOnly]
    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (!s_windows.TryGetValue(hwnd, out var win))
            {
                if (s_creating == null) return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
                win = s_creating;
                win.Handle = hwnd;
                s_windows[hwnd] = win;
            }
            if (msg == C.WM_NCDESTROY)
            {
                s_windows.Remove(hwnd);
                win.Handle = 0;
                win.OnDestroyed();
                return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
            }
            if (msg == C.WM_PAINT)
            {
                PAINTSTRUCT ps;
                var hdc = User32.BeginPaint(hwnd, &ps);
                try
                {
                    User32.GetClientRect(hwnd, out var rc);
                    using var canvas = new Canvas(hdc, rc.Right - rc.Left, rc.Bottom - rc.Top);
                    win.OnPaint(canvas);
                }
                finally
                {
                    User32.EndPaint(hwnd, &ps);
                }
                return 0;
            }
            if (msg == C.WM_ERASEBKGND) return 1;
            if (win.WndProc(msg, wParam, lParam, out var result)) return result;
        }
        catch (Exception e)
        {
            Log.Error("WndProc msg=0x" + msg.ToString("X"), e);
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    /// <summary>Handle a message; return true if handled.</summary>
    protected virtual bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = 0;
        return false;
    }

    protected virtual void OnPaint(Canvas canvas) { }

    protected virtual void OnDestroyed() { }

    protected static int LoWord(IntPtr v) => (short)((long)v & 0xFFFF);
    protected static int HiWord(IntPtr v) => (short)(((long)v >> 16) & 0xFFFF);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static bool NoActivate(uint msg, out IntPtr result)
    {
        result = C.MA_NOACTIVATE;
        return msg == C.WM_MOUSEACTIVATE;
    }
}
