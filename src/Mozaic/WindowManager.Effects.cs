using Mozaic.Core;
using Mozaic.Core.Config;
using Mozaic.Platform;
using Mozaic.UI;

namespace Mozaic;

/// <summary>Visual effects: group lines, opacity by recent use, the hold-to-show cheat sheet.</summary>
internal sealed partial class WindowManager
{
    private GroupLines _lines = null!;
    private OpacityWorker _opacity = null!;
    private Cheatsheet _cheatsheet = null!;
    private long _focusSeq;
    private Node? _mruLast;
    private (int Vk, Modifiers Mods) _trigger;

    public GroupLines Lines => _lines;

    private void CreateEffects()
    {
        _opacity = new OpacityWorker(Path.Combine(Options.DataDir, "translucent-windows.txt"));
        _opacity.Start();
        _lines = new GroupLines(this);
        _cheatsheet = new Cheatsheet(this);
        InputHooks.OnModifierHeld = held => _lines.SetModifierHeld(held);
    }

    private void DestroyEffects(int opacityTimeoutMs)
    {
        _opacity.Shutdown(opacityTimeoutMs);
        _lines.DestroyAll();
        _cheatsheet.Destroy();
    }

    /// <summary>The key binding that is running right now (for "cheatsheet", which stays while it is held).</summary>
    private void RunBoundCommand(string command, int vk, Modifiers mods)
    {
        _trigger = (vk, mods);
        try { RunCommand(command, fromUser: true); }
        finally { _trigger = default; }
    }

    public Modifiers ModKey =>
        Config.Variables.TryGetValue("$mod", out var m) && KeyNames.TryParseModifier(m, out var mods) ? mods : Modifiers.Alt;

    /// <summary>cheatsheet [show|hide|toggle]: from a key binding it stays while the keys are held.</summary>
    public string? Cheatsheet(string arg)
    {
        switch (arg)
        {
            case "" when _trigger.Vk != 0:
                // From a key binding: visible only while the keys are held (a quick tap shows nothing).
                if (InputHooks.IsPhysicallyDown(_trigger.Vk)) _cheatsheet.ShowHeld(_trigger.Vk, _trigger.Mods);
                return null;
            case "":
                if (_cheatsheet.IsVisible) _cheatsheet.Hide();
                else _cheatsheet.ShowFor();
                return null;
            case "show": _cheatsheet.ShowFor(); return null;
            case "hide": _cheatsheet.Hide(); return null;
            case "toggle":
                if (_cheatsheet.IsVisible) _cheatsheet.Hide();
                else _cheatsheet.ShowFor();
                return null;
            default:
                return "usage: cheatsheet [show|hide|toggle]";
        }
    }

    public bool CheatsheetVisible => _cheatsheet.IsVisible;

    private void UpdateEffects()
    {
        TrackFocusHistory();
        UpdateOpacity();
        UpdateGroupLines();
    }

    private void TrackFocusHistory()
    {
        var w = Tree.FocusedWindow;
        if (w == null || w == _mruLast || w.Tag is not ManagedWindow mw) return;
        mw.FocusSeq = ++_focusSeq;
        _mruLast = w;
    }

    /// <summary>
    /// inactive_opacity: the active window is opaque, the previously used one slightly transparent, and the
    /// longer ago a window was used, the more transparent it is (down to the minimum).
    /// </summary>
    private void UpdateOpacity()
    {
        if (Config.InactiveOpacity is not int first || Paused)
        {
            foreach (var mw in _windows.Values) _opacity.Set(mw, 255);
            return;
        }
        var focused = Tree.Focused;
        var focusedWin = Tree.FocusedWindow;
        HashSet<Node>? activeGroup = focused is { Kind: NodeKind.Container or NodeKind.Workspace }
            ? Tree.WindowsIn(focused).ToHashSet()
            : null;
        int rank = 1;
        foreach (var mw in _windows.Values.Where(w => w.Node.Visible && !w.HiddenByUs && w.Node.Parent != null)
                     .OrderByDescending(w => w.FocusSeq))
        {
            var n = mw.Node;
            bool opaque = n == focusedWin || activeGroup?.Contains(n) == true || n.Fullscreen || mw.NativeFullscreen ||
                          mw.NativeMaximized || (Config.OpacityIgnore.Count > 0 && Config.OpacityIgnore.Any(c => c.Matches(mw.Facts(false))));
            _opacity.Set(mw, opaque ? (byte)255 : OpacityCurve.ToAlpha(OpacityCurve.Percent(rank++, first, Config.InactiveOpacityMin)));
        }
    }

    private void UpdateGroupLines()
    {
        if (Config.GroupLines == "off" || Paused)
        {
            if (_lines.Count > 0) _lines.HideAll();
            return;
        }
        var lines = new List<GroupLines.Line>();
        uint color = Config.GroupLinesColor ?? Config.Focused.Indicator;
        foreach (var o in Tree.Outputs)
        {
            var ws = o.CurrentWorkspace;
            if (ws == null || !ws.Visible) continue;
            bool fullscreen = Tree.WindowsIn(ws).Any(w => w.Visible && (w.Fullscreen ||
                (w.Tag is ManagedWindow m && (m.NativeFullscreen || m.NativeMaximized))));
            if (fullscreen) continue;
            var focus = ws == Tree.FocusedWorkspace ? Tree.Focused : Tree.DescendFocused(ws);
            foreach (var e in GroupEdges.Compute(ws, focus, LayoutSettingsFor(o).InnerGap))
                lines.Add(new GroupLines.Line(e, color, o.Dpi));
        }
        _lines.Update(lines, Tree.Focused, Config.GroupLines == "always");
    }

    /// <summary>Opaque again when a window leaves the layout (minimized, closed, no longer managed).</summary>
    private void ResetOpacity(ManagedWindow mw) => _opacity.Set(mw, 255);

    public void OnSessionLock(bool locked) => _lines.SetSuspended(locked);
}
