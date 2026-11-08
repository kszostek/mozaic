using System.Runtime.InteropServices;
using Mozaic.Native;

namespace Mozaic.Platform;

/// <summary>Subscribes to the system window events (out-of-context, delivered on the main thread).</summary>
internal static unsafe class WinEvents
{
    private static readonly List<IntPtr> s_hooks = new();

    public static Action<uint, IntPtr>? Handler { get; set; }

    public static void Start()
    {
        Hook(C.EVENT_SYSTEM_FOREGROUND, C.EVENT_SYSTEM_FOREGROUND);
        Hook(C.EVENT_SYSTEM_MOVESIZESTART, C.EVENT_SYSTEM_MOVESIZEEND);
        Hook(C.EVENT_SYSTEM_MINIMIZESTART, C.EVENT_SYSTEM_MINIMIZEEND);
        Hook(C.EVENT_OBJECT_DESTROY, C.EVENT_OBJECT_HIDE);
        Hook(C.EVENT_OBJECT_LOCATIONCHANGE, C.EVENT_OBJECT_NAMECHANGE);
        Hook(C.EVENT_OBJECT_CLOAKED, C.EVENT_OBJECT_UNCLOAKED);
    }

    private static void Hook(uint min, uint max)
    {
        var h = User32.SetWinEventHook(min, max, 0, &Callback, 0, 0, C.WINEVENT_OUTOFCONTEXT | C.WINEVENT_SKIPOWNPROCESS);
        if (h == 0) Log.Error($"SetWinEventHook 0x{min:X}-0x{max:X} failed");
        else s_hooks.Add(h);
    }

    public static void Stop()
    {
        foreach (var h in s_hooks) User32.UnhookWinEvent(h);
        s_hooks.Clear();
    }

    [UnmanagedCallersOnly]
    private static void Callback(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        try
        {
            if (hwnd == 0 || idChild != C.CHILDID_SELF) return;
            if (idObject != C.OBJID_WINDOW && ev >= 0x8000) return;
            Handler?.Invoke(ev, hwnd);
        }
        catch (Exception e)
        {
            Log.Error($"WinEvent 0x{ev:X}", e);
        }
    }
}
