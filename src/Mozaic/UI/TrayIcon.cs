using Microsoft.Win32;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

/// <summary>Notification-area icon with the Mozaic menu.</summary>
internal sealed unsafe class TrayIcon : IDisposable
{
    private const uint Id = 1;
    private const int CmdReload = 1, CmdEdit = 2, CmdLog = 3, CmdPause = 4, CmdAutostart = 5, CmdRestart = 6, CmdExit = 7,
        CmdLauncher = 8, CmdSettings = 9;
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly WindowManager _wm;
    private readonly MainWindow _main;
    private readonly IntPtr _icon;

    public TrayIcon(WindowManager wm, MainWindow main)
    {
        _wm = wm;
        _main = main;
        _icon = IconFactory.Create(32);
        main.TrayCallback = OnCallback;
        main.MenuCommand = OnMenu;
        main.TaskbarCreated = Add;
        Add();
    }

    private NOTIFYICONDATAW Data()
    {
        var nid = new NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(NOTIFYICONDATAW),
            hWnd = _main.Handle,
            uID = Id,
            uCallbackMessage = MainWindow.WM_TRAY,
            hIcon = _icon,
        };
        return nid;
    }

    private static void CopyTo(char* dst, int capacity, string s)
    {
        int n = Math.Min(s.Length, capacity - 1);
        for (int i = 0; i < n; i++) dst[i] = s[i];
        dst[n] = '\0';
    }

    private void Add()
    {
        var nid = Data();
        nid.uFlags = C.NIF_MESSAGE | C.NIF_ICON | C.NIF_TIP | C.NIF_SHOWTIP;
        CopyTo(nid.szTip, 128, Tip());
        Shell32.Shell_NotifyIconW(C.NIM_ADD, &nid);
        nid.uTimeoutOrVersion = C.NOTIFYICON_VERSION_4;
        Shell32.Shell_NotifyIconW(C.NIM_SETVERSION, &nid);
    }

    private string Tip() => _wm.Paused ? L.T("Mozaic (tiling paused)") : "Mozaic";

    public void UpdateTip()
    {
        var nid = Data();
        nid.uFlags = C.NIF_TIP | C.NIF_SHOWTIP;
        CopyTo(nid.szTip, 128, Tip());
        Shell32.Shell_NotifyIconW(C.NIM_MODIFY, &nid);
    }

    public void ShowBalloon(string title, string text, bool warning)
    {
        var nid = Data();
        nid.uFlags = C.NIF_INFO;
        nid.dwInfoFlags = warning ? C.NIIF_WARNING : C.NIIF_INFO;
        CopyTo(nid.szInfoTitle, 64, title);
        CopyTo(nid.szInfo, 256, text);
        Shell32.Shell_NotifyIconW(C.NIM_MODIFY, &nid);
    }

    private void OnCallback(IntPtr wParam, IntPtr lParam)
    {
        int ev = (int)((long)lParam & 0xFFFF);
        const int WM_CONTEXTMENU = 0x007B;
        if (ev is WM_CONTEXTMENU or (int)C.WM_RBUTTONUP) ShowMenu();
        else if (ev is (int)C.NIN_SELECT or (int)C.NIN_KEYSELECT) _wm.OpenSettings();
    }

    private static bool AutostartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue("Mozaic") is string;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static void SetAutostart(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) key.SetValue("Mozaic", "\"" + Environment.ProcessPath + "\"");
            else key.DeleteValue("Mozaic", throwOnMissingValue: false);
        }
        catch (Exception e)
        {
            Log.Error("autostart", e);
        }
    }

    private void ShowMenu()
    {
        var menu = User32.CreatePopupMenu();
        int count = _wm.Windows.Count;
        User32.AppendMenuW(menu, C.MF_STRING | C.MF_GRAYED, 0, $"Mozaic {typeof(TrayIcon).Assembly.GetName().Version?.ToString(3)} — {L.F("{0} window(s)", count)}");
        User32.AppendMenuW(menu, C.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, C.MF_STRING, CmdSettings, L.T("Settings…"));
        User32.SetMenuDefaultItem(menu, CmdSettings, 0);
        User32.AppendMenuW(menu, C.MF_STRING, CmdLauncher, L.T("Launch application…"));
        User32.AppendMenuW(menu, C.MF_STRING | (_wm.Paused ? C.MF_CHECKED : 0), CmdPause, L.T("Pause tiling"));
        User32.AppendMenuW(menu, C.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, C.MF_STRING, CmdReload, L.T("Reload configuration"));
        User32.AppendMenuW(menu, C.MF_STRING, CmdEdit, L.T("Edit configuration file"));
        User32.AppendMenuW(menu, C.MF_STRING, CmdLog, L.T("Open log"));
        User32.AppendMenuW(menu, C.MF_STRING | (AutostartEnabled() ? C.MF_CHECKED : 0), CmdAutostart, L.T("Start with Windows"));
        User32.AppendMenuW(menu, C.MF_SEPARATOR, 0, null);
        User32.AppendMenuW(menu, C.MF_STRING, CmdRestart, L.T("Restart"));
        User32.AppendMenuW(menu, C.MF_STRING, CmdExit, L.T("Exit"));

        User32.GetCursorPos(out var pt);
        User32.SetForegroundWindow(_main.Handle);
        int cmd = User32.TrackPopupMenu(menu, C.TPM_RIGHTBUTTON | C.TPM_RETURNCMD | C.TPM_NONOTIFY, pt.X, pt.Y, 0, _main.Handle, 0);
        User32.PostMessageW(_main.Handle, C.WM_NULL, 0, 0);
        User32.DestroyMenu(menu);
        if (cmd != 0) OnMenu(cmd);
        else _wm.RequestFocusSync(false);
    }

    private void OnMenu(int cmd)
    {
        switch (cmd)
        {
            case CmdReload: _wm.Reload(); break;
            case CmdEdit: _wm.OpenConfig(); break;
            case CmdLog: _wm.OpenLog(); break;
            case CmdPause: _wm.TogglePause(); break;
            case CmdAutostart: SetAutostart(!AutostartEnabled()); break;
            case CmdRestart: _wm.Exit(restart: true); break;
            case CmdExit: _wm.Exit(); break;
            case CmdLauncher: _wm.ShowLauncher(LauncherMode.Apps); break;
            case CmdSettings: _wm.OpenSettings(); break;
        }
        if (cmd is CmdReload or CmdPause or CmdAutostart) _wm.RequestFocusSync(false);
    }

    public void Dispose()
    {
        var nid = Data();
        Shell32.Shell_NotifyIconW(C.NIM_DELETE, &nid);
        if (_icon != 0) User32.DestroyIcon(_icon);
    }
}

/// <summary>Draws the Mozaic logo (three tiles) into an icon, so no image files are needed.</summary>
internal static unsafe class IconFactory
{
    public static IntPtr Create(int size)
    {
        var bmi = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = size,
            biHeight = -size,
            biPlanes = 1,
            biBitCount = 32,
        };
        var color = Gdi32.CreateDIBSection(0, &bmi, 0, out var bits, 0, 0);
        var px = (uint*)bits;
        var pixels = Render(size);
        for (int i = 0; i < size * size; i++) px[i] = pixels[i];
        var maskBits = new byte[size * size / 8];
        IntPtr mask;
        fixed (byte* mb = maskBits) mask = Gdi32.CreateBitmap(size, size, 1, 1, mb);
        var info = new ICONINFO { fIcon = 1, hbmColor = color, hbmMask = mask };
        var icon = User32.CreateIconIndirect(&info);
        Gdi32.DeleteObject(color);
        Gdi32.DeleteObject(mask);
        return icon;
    }

    /// <summary>Premultiplied BGRA pixels of the logo.</summary>
    public static uint[] Render(int size)
    {
        var px = new uint[size * size];
        double s = size / 32.0;
        // Tiles in a 32x32 design grid: one tall tile on the left, two stacked on the right.
        var tiles = new (double L, double T, double R, double B, uint Rgb)[]
        {
            (2, 2, 15, 30, 0x2E9EF4),
            (17, 2, 30, 15, 0x8CCBFF),
            (17, 17, 30, 30, 0x1C6FB5),
        };
        double radius = 3 * s;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double cx = x + 0.5, cy = y + 0.5;
                foreach (var (l, t, r, b, rgb) in tiles)
                {
                    double L = l * s, T = t * s, R = r * s, B = b * s;
                    double a = Coverage(cx, cy, L, T, R, B, radius);
                    if (a <= 0) continue;
                    uint alpha = (uint)Math.Round(a * 255);
                    uint rr = ((rgb >> 16) & 0xFF) * alpha / 255, gg = ((rgb >> 8) & 0xFF) * alpha / 255, bb = (rgb & 0xFF) * alpha / 255;
                    px[y * size + x] = (alpha << 24) | (rr << 16) | (gg << 8) | bb;
                    break;
                }
            }
        }
        return px;
    }

    private static double Coverage(double x, double y, double l, double t, double r, double b, double radius)
    {
        // Signed distance to a rounded rectangle, antialiased over one pixel.
        double cx = Math.Clamp(x, l + radius, r - radius);
        double cy = Math.Clamp(y, t + radius, b - radius);
        double dx = x - cx, dy = y - cy;
        double dist = Math.Sqrt(dx * dx + dy * dy) - radius;
        return Math.Clamp(0.5 - dist, 0, 1);
    }
}
