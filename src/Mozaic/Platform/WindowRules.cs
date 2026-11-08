using System.Text.RegularExpressions;
using Mozaic.Core.Config;
using Mozaic.Native;

namespace Mozaic.Platform;

internal enum Verdict { Ignore, NotYet, Tile, Float }

internal sealed record WindowSnapshot(IntPtr Hwnd, uint Pid, string ClassName, string Title, string ProcessName,
    uint Style, uint ExStyle, IntPtr Owner, bool IsDialog);

/// <summary>Decides which windows Mozaic manages and whether they start tiled or floating.</summary>
internal static class WindowRules
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW", "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow", "TopLevelWindowForOverflowXamlIsland", "ForegroundStaging",
        "MultitaskingViewFrame", "TaskListThumbnailWnd", "NotifyIconOverflowWindow", "Xaml_WindowedPopupClass",
        "#32768", "tooltips_class32", "SysShadow", "Ghost", "IME", "MSCTFIME UI", "ApplicationManager_ImmersiveShellWindow",
        "Internet Explorer_Hidden", "EdgeUiInputTopWndClass", "NativeHWNDHost", "DummyDWMListenerWindow",
        "WindowsDashboard", "LockScreenControllerProxyWindow", "Windows.Internal.Shell.TabProxyWindow",
        "PseudoConsoleWindow", "Shell_InputSwitchTopLevelWindow", "TaskManagerWindow_Hidden", "SnapAssistFlyout",
        "Windows.UI.Composition.DesktopWindowContentBridge", "Microsoft.UI.Content.DesktopChildSiteBridge",
        "DesktopWindowXamlSource", "BaseBar", "OleMainThreadWndClass", "MozaicWindow",
    };

    private static readonly HashSet<string> IgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "SearchHost.exe", "StartMenuExperienceHost.exe", "ShellExperienceHost.exe", "TextInputHost.exe",
        "LockApp.exe", "SearchApp.exe", "ShellHost.exe", "PeopleExperienceHost.exe", "Widgets.exe",
        "WidgetBoard.exe", "GameBar.exe", "GameBarFTServer.exe", "ScreenClippingHost.exe", "CrossDeviceResume.exe",
    };

    private static readonly HashSet<string> FloatClasses = new(StringComparer.Ordinal)
    {
        "#32770", // standard dialog box
        "OperationStatusWindow", // explorer copy progress
    };

    /// <summary>Debug/test option: only manage windows whose title matches.</summary>
    public static Regex? ManageOnly { get; set; }

    public static Verdict Classify(IntPtr hwnd, MozaicConfig cfg, out WindowSnapshot? snap) =>
        Classify(hwnd, cfg, out snap, out _);

    /// <summary>Classifies a window; <paramref name="reason"/> explains the decision (shown in diagnostics).</summary>
    public static Verdict Classify(IntPtr hwnd, MozaicConfig cfg, out WindowSnapshot? snap, out string reason)
    {
        snap = null;
        reason = "";
        if (hwnd == 0 || !User32.IsWindow(hwnd)) { reason = "not a window"; return Verdict.Ignore; }
        if (Win32Window.IsOwnWindow(hwnd)) { reason = "Mozaic's own window"; return Verdict.Ignore; }
        uint pid = WindowQuery.Pid(hwnd);
        if (pid == WindowQuery.OwnPid) { reason = "Mozaic's own window"; return Verdict.Ignore; }
        if (User32.GetAncestor(hwnd, C.GA_ROOT) != hwnd) { reason = "child window"; return Verdict.Ignore; }

        uint style = WindowQuery.Style(hwnd);
        uint ex = WindowQuery.ExStyle(hwnd);
        if ((style & C.WS_CHILD) != 0) { reason = "child window"; return Verdict.Ignore; }

        string cls = WindowQuery.ClassName(hwnd);
        // Test windows (--test-window) use Mozaic's window class, so a Mozaic that runs normally — e.g. the one
        // managing your desktop while the tests run — never touches them; only a --manage-only instance does.
        bool testWindow = ManageOnly != null && cls == "MozaicWindow" && ManageOnly.IsMatch(WindowQuery.Title(hwnd));
        if (IgnoredClasses.Contains(cls) && !testWindow) { reason = "system window"; return Verdict.Ignore; }
        if ((ex & C.WS_EX_TOOLWINDOW) != 0 && (ex & C.WS_EX_APPWINDOW) == 0) { reason = "tool window"; return Verdict.Ignore; }
        if ((ex & C.WS_EX_NOACTIVATE) != 0 && (ex & C.WS_EX_APPWINDOW) == 0) { reason = "window that cannot be focused"; return Verdict.Ignore; }

        bool hasCaption = (style & C.WS_CAPTION) == C.WS_CAPTION;
        bool resizable = (style & C.WS_THICKFRAME) != 0;
        bool appWindow = (ex & C.WS_EX_APPWINDOW) != 0;
        if (!hasCaption && !resizable && !appWindow) { reason = "popup without a frame"; return Verdict.Ignore; }

        // Things that may still change: wait for them.
        if (!User32.IsWindowVisible(hwnd)) { reason = "invisible"; return Verdict.NotYet; }
        if (User32.IsIconic(hwnd)) { reason = "minimized"; return Verdict.NotYet; }
        if (WindowQuery.IsCloaked(hwnd)) { reason = "cloaked (another virtual desktop or suspended)"; return Verdict.NotYet; }
        string title = WindowQuery.Title(hwnd);
        if (title.Length == 0 && !appWindow) { reason = "no title yet"; return Verdict.NotYet; }
        var frame = WindowQuery.FrameRect(hwnd);
        if (frame.Width < 24 || frame.Height < 24) { reason = "too small"; return Verdict.NotYet; }

        string process = WindowQuery.EffectiveProcessName(hwnd, cls);
        if (IgnoredProcesses.Contains(process)) { reason = "system window"; return Verdict.Ignore; }

        var owner = WindowQuery.Owner(hwnd);
        bool ownerVisible = owner != 0 && User32.IsWindowVisible(owner);
        bool isDialog = (ownerVisible && !appWindow) || FloatClasses.Contains(cls) || (ex & C.WS_EX_DLGMODALFRAME) != 0;

        snap = new WindowSnapshot(hwnd, pid, cls, title, process, style, ex, owner, isDialog);

        if (ManageOnly != null && !ManageOnly.IsMatch(title)) { reason = "excluded by --manage-only"; return Verdict.Ignore; }

        if (cfg.Ignore.Count > 0)
        {
            var facts = new WindowFacts(0, hwnd, cls, title, process, isDialog, isDialog, false, false, null, Array.Empty<string>());
            foreach (var c in cfg.Ignore)
            {
                if (!c.Matches(facts)) continue;
                reason = "matches an ignore rule";
                return Verdict.Ignore;
            }
        }

        // A non-elevated process can't move elevated windows (UIPI), so leave them alone.
        if (!WindowQuery.SelfElevated && WindowQuery.IsElevated(pid))
        {
            Log.Debug("ignoring elevated window " + WindowQuery.Describe(hwnd));
            reason = "runs as administrator (start Mozaic elevated to manage it)";
            return Verdict.Ignore;
        }

        if (isDialog) { reason = "dialog"; return Verdict.Float; }
        if (!resizable) { reason = "not resizable"; return Verdict.Float; }
        if ((ex & C.WS_EX_TOPMOST) != 0) { reason = "always on top"; return Verdict.Float; }
        return Verdict.Tile;
    }
}
