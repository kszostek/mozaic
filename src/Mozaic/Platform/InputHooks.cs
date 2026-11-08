using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mozaic.Core.Config;
using Mozaic.Native;

namespace Mozaic.Platform;

internal enum DragButton { Left, Right }

/// <summary>
/// Low-level keyboard and mouse hooks, running on their own thread with their own message loop.
/// The hook callbacks only match bindings and post work to the main thread, so they always return
/// within microseconds (Windows silently removes slow low-level hooks).
/// </summary>
internal static unsafe class InputHooks
{
    private static Thread? s_thread;
    private static uint s_threadId;
    private static IntPtr s_kbHook, s_mouseHook;
    private static readonly ManualResetEventSlim s_started = new();

    private static volatile Dictionary<string, Dictionary<KeyCombo, Binding>> s_bindings = new();
    private static volatile string s_mode = "default";
    private static readonly HashSet<int> s_swallowed = new();
    private static readonly Dictionary<int, Binding> s_pendingRelease = new();

    // Mouse dragging state (hook thread only, except the snapshot of managed windows).
    private static volatile int s_dragModifier; // Modifiers, 0 = disabled
    private static volatile bool s_dragging;
    private static DragButton s_dragButton;
    private static int s_movePending;
    private static POINT s_lastPoint;

    /// <summary>Handles of managed windows (updated by the main thread) so the mouse hook can decide synchronously.</summary>
    public static ConcurrentDictionary<IntPtr, byte> ManagedHandles { get; } = new();

    public static volatile bool KeyboardEnabled = true;

    // Physical key state as seen by the hook. GetAsyncKeyState can't be used for keys we swallow
    // (a swallowed event never updates it), e.g. to find out whether a hotkey is still held.
    private static readonly bool[] s_physDown = new bool[256];

    // "$mod is held" notifications (for the group-lines animation).
    private static volatile int s_watchModifier; // Modifiers, 0 = off
    private static bool s_watchHeld;

    /// <summary>Runs a bound command: (command, virtual key that triggered it, modifiers held).</summary>
    public static Action<string, int, Modifiers>? OnCommand { get; set; }

    /// <summary>Called on the main thread when the watched modifier ($mod) is pressed or released.</summary>
    public static Action<bool>? OnModifierHeld { get; set; }

    public static bool IsPhysicallyDown(int vk) => vk is > 0 and < 256 && s_physDown[vk];

    /// <summary>Modifiers held right now, from the hook's own key state.</summary>
    public static Modifiers PhysicalModifiers()
    {
        // Keyboards report left/right codes; injected input may use the generic ones (VK_SHIFT/CONTROL/MENU).
        var m = Modifiers.None;
        if (s_physDown[0xA0] || s_physDown[0xA1] || s_physDown[C.VK_SHIFT]) m |= Modifiers.Shift;
        if (s_physDown[0xA2] || s_physDown[0xA3] || s_physDown[C.VK_CONTROL]) m |= Modifiers.Ctrl;
        if (s_physDown[0xA4] || s_physDown[0xA5] || s_physDown[C.VK_MENU]) m |= Modifiers.Alt;
        if (s_physDown[0x5B] || s_physDown[0x5C]) m |= Modifiers.Win;
        return m;
    }

    public static void WatchModifier(Modifiers mods)
    {
        s_watchModifier = (int)mods;
        s_watchHeld = false;
    }
    public static Action<IntPtr, DragButton, int, int>? OnDragStart { get; set; }
    public static Action<int, int>? OnDragMove { get; set; }
    public static Action<int, int, bool>? OnDragEnd { get; set; }

    public static string Mode => s_mode;

    public static void SetBindings(IEnumerable<Binding> bindings)
    {
        var map = new Dictionary<string, Dictionary<KeyCombo, Binding>>(StringComparer.Ordinal);
        foreach (var b in bindings)
        {
            if (!map.TryGetValue(b.Mode, out var m)) map[b.Mode] = m = new Dictionary<KeyCombo, Binding>();
            m[b.Combo] = b;
        }
        s_bindings = map;
    }

    public static void SetMode(string mode) => s_mode = mode;

    public static void SetDragModifier(Modifiers? mods) => s_dragModifier = (int)(mods ?? Modifiers.None);

    public static void Start(bool keyboard, bool mouse)
    {
        s_thread = new Thread(() => Run(keyboard, mouse)) { IsBackground = true, Name = "Mozaic input hooks", Priority = ThreadPriority.Highest };
        s_thread.Start();
        s_started.Wait(5000);
    }

    public static void Stop()
    {
        if (s_threadId != 0) User32.PostThreadMessageW(s_threadId, C.WM_QUIT, 0, 0);
        s_thread?.Join(2000);
        s_thread = null;
        s_threadId = 0;
    }

    private static void Run(bool keyboard, bool mouse)
    {
        s_threadId = Kernel32.GetCurrentThreadId();
        var mod = Kernel32.GetModuleHandleW(null);
        if (keyboard)
        {
            s_kbHook = User32.SetWindowsHookExW(C.WH_KEYBOARD_LL, &KeyboardProc, mod, 0);
            if (s_kbHook == 0) Log.Error("keyboard hook failed: " + Marshal.GetLastPInvokeError());
        }
        if (mouse)
        {
            s_mouseHook = User32.SetWindowsHookExW(C.WH_MOUSE_LL, &MouseProc, mod, 0);
            if (s_mouseHook == 0) Log.Error("mouse hook failed: " + Marshal.GetLastPInvokeError());
        }
        s_started.Set();
        MSG msg;
        while (User32.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }
        if (s_kbHook != 0) User32.UnhookWindowsHookEx(s_kbHook);
        if (s_mouseHook != 0) User32.UnhookWindowsHookEx(s_mouseHook);
        s_kbHook = s_mouseHook = 0;
    }

    private static bool Down(int vk) => (User32.GetAsyncKeyState(vk) & 0x8000) != 0;

    public static Modifiers CurrentModifiers()
    {
        var m = Modifiers.None;
        if (Down(C.VK_SHIFT)) m |= Modifiers.Shift;
        if (Down(C.VK_CONTROL)) m |= Modifiers.Ctrl;
        if (Down(C.VK_MENU)) m |= Modifiers.Alt;
        if (Down(C.VK_LWIN) || Down(C.VK_RWIN)) m |= Modifiers.Win;
        return m;
    }

    [UnmanagedCallersOnly]
    private static IntPtr KeyboardProc(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code == C.HC_ACTION && ((KBDLLHOOKSTRUCT*)lParam)->dwExtraInfo != WindowOps.InjectedMarker)
                TrackPhysical((uint)wParam, (int)((KBDLLHOOKSTRUCT*)lParam)->vkCode);
            if (code == C.HC_ACTION && KeyboardEnabled)
            {
                var k = (KBDLLHOOKSTRUCT*)lParam;
                if (k->dwExtraInfo != WindowOps.InjectedMarker)
                {
                    uint msg = (uint)wParam;
                    int vk = (int)k->vkCode;
                    bool down = msg is C.WM_KEYDOWN or C.WM_SYSKEYDOWN;
                    bool up = msg is C.WM_KEYUP or C.WM_SYSKEYUP;
                    if (down && !KeyNames.IsModifierVk(vk))
                    {
                        var mods = CurrentModifiers();
                        if (s_bindings.TryGetValue(s_mode, out var map) && map.TryGetValue(new KeyCombo(mods, vk), out var b))
                        {
                            if (b.OnRelease) s_pendingRelease[vk] = b;
                            else Fire(b.Command, vk, mods);
                            s_swallowed.Add(vk);
                            if ((mods & (Modifiers.Alt | Modifiers.Win)) != 0) WindowOps.SendMaskKey();
                            return 1;
                        }
                    }
                    else if (up && s_swallowed.Remove(vk))
                    {
                        if (s_pendingRelease.Remove(vk, out var b)) Fire(b.Command, vk, b.Combo.Modifiers);
                        return 1;
                    }
                }
            }
        }
        catch (Exception e)
        {
            Log.Error("keyboard hook", e);
        }
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    private static void TrackPhysical(uint msg, int vk)
    {
        if (vk is <= 0 or >= 256) return;
        bool down = msg is C.WM_KEYDOWN or C.WM_SYSKEYDOWN;
        if (!down && msg is not (C.WM_KEYUP or C.WM_SYSKEYUP)) return;
        s_physDown[vk] = down;

        int watch = s_watchModifier;
        if (watch == 0 || !KeyNames.IsModifierVk(vk)) return;
        var mods = PhysicalModifiers();
        // AltGr (Ctrl+Alt, used to type Polish and other letters) doesn't count as holding Alt.
        var w = (Modifiers)watch;
        bool held = (mods & w) == w && (w.HasFlag(Modifiers.Ctrl) || !mods.HasFlag(Modifiers.Ctrl));
        if (held == s_watchHeld) return;
        s_watchHeld = held;
        var handler = OnModifierHeld;
        if (handler != null) Dispatcher.Post(() => handler(held));
    }

    private static void Fire(string command, int vk, Modifiers mods)
    {
        var handler = OnCommand;
        if (handler != null) Dispatcher.Post(() => handler(command, vk, mods));
    }

    [UnmanagedCallersOnly]
    private static IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code == C.HC_ACTION)
            {
                var m = (MSLLHOOKSTRUCT*)lParam;
                // Only skip our own synthetic input; remote-control tools inject real user input.
                if (m->dwExtraInfo != WindowOps.InjectedMarker && HandleMouse((uint)wParam, m->pt)) return 1;
            }
        }
        catch (Exception e)
        {
            Log.Error("mouse hook", e);
        }
        return User32.CallNextHookEx(0, code, wParam, lParam);
    }

    private static bool HandleMouse(uint msg, POINT pt)
    {
        if (s_dragging)
        {
            switch (msg)
            {
                case C.WM_MOUSEMOVE:
                    s_lastPoint = pt;
                    if (Interlocked.Exchange(ref s_movePending, 1) == 0)
                    {
                        Dispatcher.Post(() =>
                        {
                            Interlocked.Exchange(ref s_movePending, 0);
                            var p = s_lastPoint;
                            OnDragMove?.Invoke(p.X, p.Y);
                        });
                    }
                    return false;
                case C.WM_LBUTTONUP when s_dragButton == DragButton.Left:
                case C.WM_RBUTTONUP when s_dragButton == DragButton.Right:
                    s_dragging = false;
                    var end = pt;
                    Dispatcher.Post(() => OnDragEnd?.Invoke(end.X, end.Y, false));
                    return true;
                case C.WM_LBUTTONDOWN:
                case C.WM_RBUTTONDOWN:
                    return true;
            }
            return false;
        }

        int dragMod = s_dragModifier;
        if (dragMod == 0 || msg is not (C.WM_LBUTTONDOWN or C.WM_RBUTTONDOWN)) return false;
        var mods = (int)CurrentModifiers();
        if (mods != dragMod) return false;
        var root = User32.GetAncestor(User32.WindowFromPoint(pt), C.GA_ROOT);
        if (root == 0 || !ManagedHandles.ContainsKey(root))
        {
            if (Log.DebugEnabled) Log.Debug($"mod-click at {pt.X},{pt.Y} on unmanaged window {WindowQuery.Describe(root)}");
            return false;
        }

        s_dragging = true;
        s_dragButton = msg == C.WM_LBUTTONDOWN ? DragButton.Left : DragButton.Right;
        s_lastPoint = pt;
        var button = s_dragButton;
        if ((dragMod & (int)(Modifiers.Alt | Modifiers.Win)) != 0) WindowOps.SendMaskKey();
        Dispatcher.Post(() => OnDragStart?.Invoke(root, button, pt.X, pt.Y));
        return true;
    }

    /// <summary>Cancels a drag from the main thread (e.g. when the window disappeared).</summary>
    public static void CancelDrag() => s_dragging = false;
}
