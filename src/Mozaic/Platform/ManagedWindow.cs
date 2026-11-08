using Mozaic.Core;
using Mozaic.Core.Config;

namespace Mozaic.Platform;

/// <summary>Per-window OS state that the window manager tracks next to the tree node.</summary>
internal sealed class ManagedWindow
{
    public ManagedWindow(WindowSnapshot s, Node node)
    {
        Hwnd = s.Hwnd;
        Pid = s.Pid;
        ClassName = s.ClassName;
        ProcessName = s.ProcessName;
        IsDialog = s.IsDialog;
        Node = node;
        node.Title = s.Title;
    }

    public IntPtr Hwnd { get; }
    public uint Pid { get; }
    public string ClassName { get; }
    public string ProcessName { get; set; }
    public bool IsDialog { get; }
    public Node Node { get; }

    /// <summary>We hid this window (workspace not visible / background tab / scratchpad).</summary>
    public bool HiddenByUs { get; set; }
    public int PendingHides { get; set; }
    public int PendingShows { get; set; }
    public long LastVisibilityOp { get; set; }

    /// <summary>The visible-frame rectangle we last asked the window to occupy.</summary>
    public Rect? LastTarget { get; set; }
    public long LastPlaced { get; set; }

    /// <summary>Invisible resize borders (left, top, right, bottom) between GetWindowRect and the visible frame.</summary>
    public (int L, int T, int R, int B) FrameInsets { get; set; }
    public bool FrameInsetsKnown { get; set; }

    public int EnforceCount { get; set; }
    public long EnforceWindowStart { get; set; }
    public nuint EnforceTimer;

    public bool InMoveSize { get; set; }
    public bool MoveSizeIsResize { get; set; }
    public Rect MoveSizeStartFrame { get; set; }

    public bool NativeMaximized { get; set; }
    public bool NativeFullscreen { get; set; }

    public uint? AppliedBorderColor { get; set; }
    public bool CornerApplied { get; set; }

    /// <summary>When the window last got the focus (higher = more recent); orders windows for inactive_opacity.</summary>
    public long FocusSeq { get; set; }
    /// <summary>The opacity last requested from the <see cref="OpacityWorker"/> (255 = opaque).</summary>
    public byte RequestedAlpha { get; set; } = 255;

    public WindowFacts Facts(bool focused) => new(
        Node.Id, Hwnd, ClassName, Node.Title, ProcessName, Node.InFloating, IsDialog, Node.Urgent, focused,
        Tree.WorkspaceOf(Node)?.Name, Node.Marks);

    public override string ToString() => $"0x{Hwnd:X} {ProcessName} \"{Node.Title}\"";
}
