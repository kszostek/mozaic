using System.Diagnostics;
using System.Globalization;
using System.Text;
using Mozaic.Core;
using Mozaic.Core.Commands;
using Mozaic.Native;
using Mozaic.Platform;
using Mozaic.UI;

namespace Mozaic;

internal sealed record CommandOutcome(List<string> Errors, int Count)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>Executes i3 commands against the tree (plus a few Mozaic extensions).</summary>
internal sealed class CommandExecutor(WindowManager wm)
{
    private Tree T => wm.Tree;

    public CommandOutcome Execute(string text, Node? context, bool fromUser)
    {
        var errors = new List<string>();
        int count = 0;
        List<ParsedCommand> cmds;
        try
        {
            cmds = CommandParser.Parse(text);
        }
        catch (CommandParseException e)
        {
            errors.Add(e.Message);
            return new CommandOutcome(errors, 0);
        }

        var focusBefore = T.Focused;
        var windowBefore = T.FocusedWindow;
        var outputBefore = T.FocusedOutput;
        bool forceFocus = false;
        foreach (var cmd in cmds)
        {
            if (cmd.Tokens.Count == 0) continue;
            List<Node>? targets = null;
            if (cmd.Criteria != null) targets = wm.MatchWindows(cmd.Criteria).Select(m => m.Node).ToList();
            else if (context != null) targets = new List<Node> { context };
            try
            {
                var err = Run(cmd, targets, ref forceFocus);
                if (err != null) errors.Add(err);
                count++;
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                errors.Add(e.Message);
                Log.Error("command '" + cmd.Raw + "'", e);
            }
        }
        if (forceFocus || T.Focused != focusBefore || T.FocusedWindow != windowBefore || T.FocusedOutput != outputBefore)
            wm.RequestFocusSync(fromUser);
        wm.RequestApply();
        return new CommandOutcome(errors, count);
    }

    private IEnumerable<Node> Targets(List<Node>? targets)
    {
        if (targets != null) return targets;
        return T.Focused is { Kind: NodeKind.Window or NodeKind.Container } f ? new[] { f } : Array.Empty<Node>();
    }

    private IEnumerable<Node> TargetWindows(List<Node>? targets)
    {
        if (targets != null) return targets.Where(n => n.Kind == NodeKind.Window);
        return T.FocusedWindow is { } w ? new[] { w } : Array.Empty<Node>();
    }

    private void FocusFirstTarget(List<Node>? targets)
    {
        var t = targets?.LastOrDefault(n => n.Parent != null);
        if (t != null) T.Focus(t);
    }

    private string? Run(ParsedCommand cmd, List<Node>? targets, ref bool forceFocus)
    {
        var t = cmd.Tokens.ToList();
        string Arg(int i) => i < t.Count ? t[i].ToLowerInvariant() : "";
        switch (cmd.Verb)
        {
            case "nop":
                return null;
            case "exec":
                Exec(cmd.RawArgs);
                return null;
            case "exit":
                wm.Exit();
                return null;
            case "restart":
                wm.Exit(restart: true);
                return null;
            case "reload":
                wm.Reload();
                return null;
            case "kill":
                foreach (var n in Targets(targets).ToList())
                    foreach (var w in Tree.WindowsIn(n)) WindowOps.Close(w.Handle);
                return null;
            case "focus":
                return Focus(t, targets, ref forceFocus);
            case "move":
                return Move(t, targets);
            case "workspace":
                return Workspace(t.Skip(1).ToList());
            case "rename":
                return Rename(t);
            case "split":
            {
                FocusFirstTarget(targets);
                var parent = T.Focused?.Parent;
                Orientation o = Arg(1) switch
                {
                    "h" or "horizontal" => Orientation.Horizontal,
                    "v" or "vertical" => Orientation.Vertical,
                    "t" or "toggle" => parent?.Orientation == Orientation.Horizontal ? Orientation.Vertical : Orientation.Horizontal,
                    _ => Orientation.None,
                };
                if (o == Orientation.None) return "usage: split h|v|toggle";
                T.Split(o);
                return null;
            }
            case "layout":
                return Layout(t, targets);
            case "fullscreen":
            {
                foreach (var n in Targets(targets).ToList())
                {
                    var w = n.Kind == NodeKind.Window ? n : Tree.DescendFocused(n);
                    if (w.Kind != NodeKind.Window) continue;
                    bool on = Toggle(Arg(1), w.Fullscreen);
                    T.SetFullscreen(w, on);
                    if (on && w.Parent != null) T.Focus(w);
                    if (w.Tag is ManagedWindow mw) mw.LastTarget = null;
                }
                forceFocus = true;
                return null;
            }
            case "floating":
            {
                foreach (var w in TargetWindows(targets).ToList())
                {
                    bool on = Toggle(Arg(1), w.InFloating);
                    if (on == w.InFloating || w.Parent == null) continue;
                    if (w.Tag is ManagedWindow mw) mw.LastTarget = null;
                    T.SetFloating(w, on);
                }
                return null;
            }
            case "sticky":
                foreach (var w in TargetWindows(targets)) w.Sticky = Toggle(Arg(1), w.Sticky);
                return null;
            case "scratchpad":
                if (Arg(1) != "show") return "usage: scratchpad show";
                if (targets != null)
                {
                    if (targets.Count == 0) return "no window matches";
                    foreach (var n in targets) T.ScratchpadShow(n);
                }
                else if (!T.ScratchpadShow())
                {
                    return "the scratchpad is empty";
                }
                forceFocus = true;
                return null;
            case "resize":
                return Resize(t, targets);
            case "mode":
            {
                var name = t.Count > 1 ? string.Join(' ', t.Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal))) : "default";
                if (!wm.Config.Modes.Contains(name)) return $"unknown mode '{name}'";
                wm.SetMode(name);
                return null;
            }
            case "mark":
            {
                bool add = t.Contains("--add"), toggle = t.Contains("--toggle");
                var name = t.Skip(1).LastOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal));
                if (name == null) return "usage: mark [--add|--replace] [--toggle] <name>";
                foreach (var n in Targets(targets)) T.Mark(n, name, add, toggle);
                wm.InvalidateBars();
                return null;
            }
            case "unmark":
                T.Unmark(t.Count > 1 ? t[1] : null);
                return null;
            case "swap":
                return Swap(t, targets);
            case "gaps":
                return Gaps(t);
            case "bar":
                return Bar(t);
            case "border":
            case "title_format":
            case "title_window_icon":
            case "shmlog":
            case "open":
                return null; // X11/decoration specifics: accepted for config compatibility
            case "debuglog":
                Log.DebugEnabled = Toggle(Arg(1), Log.DebugEnabled);
                return null;
            // ---- Mozaic extensions
            case "balance":
                if (T.FocusedWorkspace is { } bws) Tree.Balance(bws);
                return null;
            case "launcher":
                wm.ShowLauncher(Arg(1) switch
                {
                    "windows" or "window" => LauncherMode.Windows,
                    "commands" or "command" or "keys" => LauncherMode.Commands,
                    _ => LauncherMode.Apps,
                });
                return null;
            case "settings":
                wm.OpenSettings();
                return null;
            case "cheatsheet":
                return wm.Cheatsheet(Arg(1));
            case "hotkeys":
            {
                // Used by the settings app while it records a shortcut; re-enabled automatically after 2 minutes.
                bool on = Toggle(Arg(1), InputHooks.KeyboardEnabled);
                InputHooks.KeyboardEnabled = on && !wm.Paused;
                if (!on) Dispatcher.SetTimeout(120_000, () => InputHooks.KeyboardEnabled = !wm.Paused);
                return null;
            }
            case "confirm":
                return Confirm(t);
            case "pause":
            case "tiling":
            {
                bool pause = Arg(1) switch { "on" or "enable" => true, "off" or "disable" => false, _ => !wm.Paused };
                if (pause != wm.Paused) wm.TogglePause();
                return null;
            }
            case "retile":
                foreach (var mw in wm.Windows.Values) mw.LastTarget = null;
                return null;
            default:
                return $"unknown command '{t[0]}'";
        }
    }

    private static bool Toggle(string arg, bool current) => arg switch
    {
        "enable" or "on" or "yes" or "true" => true,
        "disable" or "off" or "no" or "false" => false,
        _ => !current,
    };

    // =====================================================================================

    public void Exec(string commandLine)
    {
        var s = commandLine.Trim();
        while (s.StartsWith("--", StringComparison.Ordinal))
        {
            int sp = s.IndexOf(' ');
            s = sp < 0 ? "" : s[(sp + 1)..].TrimStart();
        }
        string file, args;
        // exec "notepad foo.txt" (one quoted string) is a whole command line, like i3's sh -c,
        // unless the quoted string is a path to an existing file.
        if (s.Length > 1 && s[0] == '"' && s[^1] == '"' && s.IndexOf('"', 1) == s.Length - 1 &&
            !File.Exists(Environment.ExpandEnvironmentVariables(s[1..^1])))
            s = s[1..^1];
        if (s.Length == 0) return;
        (file, args) = CommandParser.SplitCommandLine(s);
        file = Environment.ExpandEnvironmentVariables(file);
        args = Environment.ExpandEnvironmentVariables(args);
        try
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = true,
                WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                try { wm.RememberExec((uint)p.Id); }
                catch (InvalidOperationException) { }
            }
            Log.Debug("exec " + s);
        }
        catch (Exception e)
        {
            Log.Warn($"exec '{s}' failed: {e.Message}");
            wm.Notify(L.F("Could not run: {0}", s), e.Message);
        }
    }

    // =====================================================================================

    private string? Focus(List<string> t, List<Node>? targets, ref bool forceFocus)
    {
        if (t.Count == 1)
        {
            if (targets == null) return "focus needs criteria or an argument";
            var target = targets.LastOrDefault();
            if (target == null) return "no window matches";
            if (target.Parent == null || target.IsScratchpad && target.Parent == null) T.ScratchpadShow(target);
            else if (target.IsScratchpad && Tree.WorkspaceOf(target) != T.FocusedWorkspace) T.ScratchpadShow(target);
            else T.Focus(target);
            forceFocus = true;
            return null;
        }
        FocusFirstTarget(targets);
        string a = t[1].ToLowerInvariant();
        if (DirectionExtensions.TryParse(a, out var dir))
        {
            T.FocusDirection(dir);
            forceFocus = true;
            return null;
        }
        switch (a)
        {
            case "last":
                T.FocusLast();
                forceFocus = true;
                return null;
            case "parent": T.FocusParent(); return null;
            case "child": T.FocusChild(); return null;
            case "floating": T.FocusModeToggle(true); return null;
            case "tiling": T.FocusModeToggle(false); return null;
            case "mode_toggle": T.FocusModeToggle(); return null;
            case "next":
            case "prev":
                T.FocusSibling(a == "next", t.Count > 2 && t[2].Equals("sibling", StringComparison.OrdinalIgnoreCase));
                return null;
            case "output":
            {
                if (t.Count < 3) return "usage: focus output <direction|name>";
                var o = ResolveOutput(t[2]);
                if (o == null) return $"no output '{t[2]}'";
                T.FocusOutput(o);
                return null;
            }
            default:
                return $"unknown focus target '{t[1]}'";
        }
    }

    private Node? ResolveOutput(string spec)
    {
        var cur = T.FocusedOutput;
        if (cur != null && DirectionExtensions.TryParse(spec, out var d)) return T.OutputInDirection(cur, d);
        var list = T.Outputs;
        if (cur != null && spec is "next" or "prev")
        {
            int i = list.ToList().IndexOf(cur);
            return list[(i + (spec == "next" ? 1 : -1) + list.Count) % list.Count];
        }
        return T.FindOutput(spec);
    }

    /// <summary>Resolves a workspace argument list (after the "workspace" keyword).</summary>
    private Node? ResolveWorkspace(List<string> args, bool forMove, out string? error)
    {
        error = null;
        var a = args.Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList();
        bool noAutoBaf = args.Contains("--no-auto-back-and-forth");
        if (a.Count == 0) { error = "missing workspace name"; return null; }
        var focusedWs = T.FocusedWorkspace;
        string first = a[0].ToLowerInvariant();
        switch (first)
        {
            case "next":
            case "prev":
            {
                var all = T.OrderedWorkspaces();
                if (focusedWs == null || all.Count == 0) return null;
                int i = all.IndexOf(focusedWs);
                return all[(i + (first == "next" ? 1 : -1) + all.Count) % all.Count];
            }
            case "next_on_output":
            case "prev_on_output":
            {
                var o = T.FocusedOutput;
                if (o == null || focusedWs == null || o.Children.Count == 0) return null;
                int i = o.Children.IndexOf(focusedWs);
                return o.Children[(i + (first == "next_on_output" ? 1 : -1) + o.Children.Count) % o.Children.Count];
            }
            case "back_and_forth":
                return T.PreviousWorkspaceName is { } prev ? T.GetOrCreateWorkspace(prev) : null;
            case "current":
                return focusedWs;
        }

        Node? ws;
        if (first == "number")
        {
            if (a.Count < 2) { error = "workspace number needs a number"; return null; }
            string rest = string.Join(' ', a.Skip(1));
            int num = WorkspaceNames.ParseNumber(rest);
            if (num < 0) { error = $"'{rest}' does not start with a number"; return null; }
            ws = T.FindWorkspaceByNumber(num) ?? T.GetOrCreateWorkspace(rest);
        }
        else
        {
            ws = T.GetOrCreateWorkspace(string.Join(' ', a));
        }
        if (!forMove && !noAutoBaf && wm.Config.WorkspaceAutoBackAndForth && ws == focusedWs && T.PreviousWorkspaceName is { } p)
            ws = T.GetOrCreateWorkspace(p);
        return ws;
    }

    private string? Workspace(List<string> args)
    {
        var ws = ResolveWorkspace(args, false, out var err);
        if (ws == null) return err;
        T.ShowWorkspace(ws);
        return null;
    }

    private string? Rename(List<string> t)
    {
        // rename workspace [<old>] to <new>
        if (t.Count < 4 || !t[1].Equals("workspace", StringComparison.OrdinalIgnoreCase)) return "usage: rename workspace [<old>] to <new>";
        int to = t.FindIndex(2, x => x.Equals("to", StringComparison.OrdinalIgnoreCase));
        if (to < 0 || to == t.Count - 1) return "usage: rename workspace [<old>] to <new>";
        var ws = to == 2 ? T.FocusedWorkspace : T.FindWorkspace(string.Join(' ', t.Skip(2).Take(to - 2)));
        if (ws == null) return "no such workspace";
        var name = string.Join(' ', t.Skip(to + 1));
        if (T.FindWorkspace(name) is { } other && other != ws) return $"workspace '{name}' already exists";
        T.RenameWorkspace(ws, name);
        return null;
    }

    private string? Layout(List<string> t, List<Node>? targets)
    {
        FocusFirstTarget(targets);
        if (t.Count < 2) return "usage: layout default|stacking|tabbed|splith|splitv|toggle ...";
        string a = t[1].ToLowerInvariant();
        if (a == "default") { T.SetLayoutDefault(); return null; }
        if (a == "toggle") { T.ToggleLayout(t.Skip(2).Select(x => x.ToLowerInvariant()).ToList()); return null; }
        if (!Tree.TryParseLayout(a, out var layout)) return $"unknown layout '{t[1]}'";
        T.SetLayout(layout);
        return null;
    }

    private string? Move(List<string> t, List<Node>? targets)
    {
        var a = t.Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (a.Count > 0 && a[0].ToLowerInvariant() is "window" or "container") a.RemoveAt(0);
        if (a.Count > 0 && a[0].Equals("to", StringComparison.OrdinalIgnoreCase)) a.RemoveAt(0);
        if (a.Count == 0) return "usage: move <direction|workspace|output|position|scratchpad|mark>";
        string what = a[0].ToLowerInvariant();

        if (DirectionExtensions.TryParse(what, out var dir))
        {
            int px = a.Count > 1 && int.TryParse(a[1], out var v) ? v : 10;
            var targetsList = targets ?? (T.Focused != null ? new List<Node> { T.Focused } : new List<Node>());
            foreach (var n in targetsList)
            {
                if (n.InFloating)
                {
                    if (n.FloatRect is Rect r && Tree.OutputOf(n) is { } o)
                    {
                        int d = (int)Math.Round(px * o.Dpi / 96.0);
                        n.FloatRect = dir switch
                        {
                            Direction.Left => r.Offset(-d, 0),
                            Direction.Right => r.Offset(d, 0),
                            Direction.Up => r.Offset(0, -d),
                            _ => r.Offset(0, d),
                        };
                    }
                    continue;
                }
                if (n.Parent == null) continue;
                if (T.Focused != n) T.Focus(n);
                T.MoveDirection(dir);
            }
            return null;
        }

        switch (what)
        {
            case "workspace":
            {
                if (a.Count > 1 && a[1].Equals("to", StringComparison.OrdinalIgnoreCase)) a.RemoveAt(1);
                if (a.Count > 2 && a[1].Equals("output", StringComparison.OrdinalIgnoreCase))
                {
                    // move workspace to output <x>
                    var output = ResolveOutput(a[2]);
                    if (output == null) return $"no output '{a[2]}'";
                    if (T.FocusedWorkspace is { } fws) T.MoveWorkspaceToOutput(fws, output);
                    return null;
                }
                var ws = ResolveWorkspace(t.Skip(t.FindIndex(x => x.Equals("workspace", StringComparison.OrdinalIgnoreCase)) + 1).ToList(), true, out var err);
                if (ws == null) return err;
                foreach (var n in Targets(targets).ToList()) MoveNode(n, ws);
                return null;
            }
            case "output":
            {
                if (a.Count < 2) return "usage: move container to output <direction|name>";
                var output = ResolveOutput(a[1]);
                if (output == null) return $"no output '{a[1]}'";
                var ws = T.EnsureWorkspace(output);
                foreach (var n in Targets(targets).ToList()) MoveNode(n, ws);
                return null;
            }
            case "scratchpad":
                foreach (var w in TargetWindows(targets).ToList()) T.MoveToScratchpad(w);
                return null;
            case "absolute":
            case "position":
                return MovePosition(a, targets);
            case "mark":
            {
                if (a.Count < 2) return "usage: move container to mark <mark>";
                var target = T.FindMark(a[1]);
                if (target == null) return $"no container is marked '{a[1]}'";
                foreach (var n in Targets(targets).ToList())
                {
                    if (n == target || target.Parent == null) continue;
                    var side = target.Parent.Orientation == Orientation.Vertical ? Direction.Down : Direction.Right;
                    if (target.Kind == NodeKind.Workspace) MoveNode(n, target);
                    else T.MoveNextTo(n, target, side);
                }
                return null;
            }
            default:
                return $"cannot move '{a[0]}'";
        }
    }

    private void MoveNode(Node n, Node ws)
    {
        if (n.Tag is ManagedWindow mw) mw.LastTarget = null;
        T.MoveToWorkspace(n, ws);
    }

    private string? MovePosition(List<string> a, List<Node>? targets)
    {
        var args = a.Where(x => !x.Equals("absolute", StringComparison.OrdinalIgnoreCase) && !x.Equals("position", StringComparison.OrdinalIgnoreCase)
                                && !x.Equals("px", StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var w in TargetWindows(targets))
        {
            if (!w.InFloating || w.FloatRect is not Rect r) continue;
            var output = Tree.OutputOf(w);
            if (output == null) continue;
            if (args.Count >= 1 && args[0].Equals("center", StringComparison.OrdinalIgnoreCase))
            {
                w.FloatRect = r.CenteredIn(output.LayoutArea);
            }
            else if (args.Count >= 1 && args[0].Equals("mouse", StringComparison.OrdinalIgnoreCase))
            {
                User32.GetCursorPos(out var pt);
                w.FloatRect = Rect.FromSize(pt.X - r.Width / 2, pt.Y - r.Height / 2, r.Width, r.Height).ClampInto(output.LayoutArea);
            }
            else if (args.Count >= 2 && int.TryParse(args[0], out int x) && int.TryParse(args[1], out int y))
            {
                double s = output.Dpi / 96.0;
                w.FloatRect = Rect.FromSize(output.OutputRect.Left + (int)(x * s), output.OutputRect.Top + (int)(y * s), r.Width, r.Height);
            }
            else
            {
                return "usage: move position center|mouse|<x> <y>";
            }
        }
        return null;
    }

    private string? Resize(List<string> t, List<Node>? targets)
    {
        if (t.Count < 3) return "usage: resize grow|shrink <direction> [<px> px] [or <ppt> ppt] | resize set <w> <h>";
        string op = t[1].ToLowerInvariant();
        if (op == "set") return ResizeSet(t.Skip(2).ToList(), targets);
        if (op is not ("grow" or "shrink")) return "usage: resize grow|shrink ...";
        string which = t[2].ToLowerInvariant();
        int? px = null;
        double? ppt = null;
        for (int i = 3; i < t.Count; i++)
        {
            if (!double.TryParse(t[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) continue;
            string unit = i + 1 < t.Count ? t[i + 1].ToLowerInvariant() : "px";
            if (unit == "ppt") ppt = num; else px = (int)num;
        }
        if (px == null && ppt == null) { px = 10; ppt = 10; }
        int sign = op == "grow" ? 1 : -1;

        foreach (var n in Targets(targets).ToList())
        {
            var output = Tree.OutputOf(n);
            double scale = output != null ? output.Dpi / 96.0 : 1;
            if (n.InFloating)
            {
                if (n.FloatRect is not Rect r) continue;
                int d = sign * (int)Math.Round((px ?? 10) * scale);
                n.FloatRect = which switch
                {
                    "width" or "right" => new Rect(r.Left, r.Top, Math.Max(r.Left + 60, r.Right + d), r.Bottom),
                    "left" => new Rect(Math.Min(r.Right - 60, r.Left - d), r.Top, r.Right, r.Bottom),
                    "height" or "down" => new Rect(r.Left, r.Top, r.Right, Math.Max(r.Top + 40, r.Bottom + d)),
                    "up" => new Rect(r.Left, Math.Min(r.Bottom - 40, r.Top - d), r.Right, r.Bottom),
                    _ => r,
                };
                continue;
            }
            bool ok = which switch
            {
                "width" or "height" => T.ResizeTiling(n, which == "width", ppt.HasValue ? sign * ppt.Value / 100.0 : null,
                    px.HasValue ? sign * (int)Math.Round(px.Value * scale) : null),
                "left" or "right" or "up" or "down" => DirectionExtensions.TryParse(which, out var dir) &&
                    T.ResizeEdge(n, dir, sign * (int)Math.Round((px ?? 10) * scale), ppt.HasValue ? sign * ppt.Value / 100.0 : null),
                _ => false,
            };
            if (!ok && which is not ("width" or "height" or "left" or "right" or "up" or "down"))
                return $"unknown resize direction '{t[2]}'";
        }
        return null;
    }

    private string? ResizeSet(List<string> a, List<Node>? targets)
    {
        // resize set [width] <w> [px|ppt] [[height] <h> [px|ppt]]
        double? w = null, h = null;
        bool wPpt = false, hPpt = false;
        int i = 0;
        bool nextIsHeight = false;
        while (i < a.Count)
        {
            var tok = a[i].ToLowerInvariant();
            if (tok == "width") { nextIsHeight = false; i++; continue; }
            if (tok == "height") { nextIsHeight = true; i++; continue; }
            if (tok is "px" or "ppt") { i++; continue; }
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
            {
                bool ppt = i + 1 < a.Count && a[i + 1].Equals("ppt", StringComparison.OrdinalIgnoreCase);
                if (!nextIsHeight && w == null) { w = num; wPpt = ppt; nextIsHeight = true; }
                else { h = num; hPpt = ppt; }
            }
            i++;
        }
        if (w == null && h == null) return "usage: resize set <width> [px|ppt] <height> [px|ppt]";
        foreach (var n in Targets(targets).ToList())
        {
            var output = Tree.OutputOf(n);
            if (output == null) continue;
            double s = output.Dpi / 96.0;
            if (n.InFloating && n.FloatRect is Rect r)
            {
                int nw = w is double ww && ww > 0 ? (wPpt ? (int)(output.LayoutArea.Width * ww / 100) : (int)(ww * s)) : r.Width;
                int nh = h is double hh && hh > 0 ? (hPpt ? (int)(output.LayoutArea.Height * hh / 100) : (int)(hh * s)) : r.Height;
                n.FloatRect = Rect.FromSize(r.Left, r.Top, nw, nh);
                continue;
            }
            if (w is double tw && tw > 0) SetTiled(n, true, tw, wPpt, s);
            if (h is double th && th > 0) SetTiled(n, false, th, hPpt, s);
        }
        return null;
    }

    private void SetTiled(Node n, bool width, double value, bool ppt, double scale)
    {
        var (first, _) = Tree.FindResizeParticipants(n, width ? Direction.Left : Direction.Down, true);
        if (first?.Parent == null) return;
        double pct = ppt ? value / 100.0 : value * scale / Math.Max(1, width ? first.Parent.Rect.Width : first.Parent.Rect.Height);
        T.ResizeSet(n, width, Math.Clamp(pct, 0.05, 0.95));
    }

    private string? Swap(List<string> t, List<Node>? targets)
    {
        // swap container with id|con_id|mark <arg>
        int with = t.FindIndex(x => x.Equals("with", StringComparison.OrdinalIgnoreCase));
        if (with < 0 || with + 2 >= t.Count) return "usage: swap container with id|con_id|mark <arg>";
        string kind = t[with + 1].ToLowerInvariant(), arg = t[with + 2];
        Node? other = kind switch
        {
            "mark" => T.FindMark(arg),
            "con_id" => int.TryParse(arg, out int cid) ? T.AllNodes().FirstOrDefault(x => x.Id == cid) : null,
            "id" => long.TryParse(arg, out long h) ? T.AllWindows().FirstOrDefault(x => (long)x.Handle == h) : null,
            _ => null,
        };
        if (other == null) return "swap target not found";
        var me = Targets(targets).FirstOrDefault();
        if (me == null) return "nothing to swap";
        foreach (var w in Tree.WindowsIn(me).Concat(Tree.WindowsIn(other)))
            if (w.Tag is ManagedWindow mw) mw.LastTarget = null;
        return T.Swap(me, other) ? null : "cannot swap these containers";
    }

    private string? Gaps(List<string> t)
    {
        // gaps inner|outer|horizontal|vertical|top|right|bottom|left current|all set|plus|minus|toggle <px>
        if (t.Count < 5) return "usage: gaps <type> current|all set|plus|minus|toggle <px>";
        string type = t[1].ToLowerInvariant(), op = t[3].ToLowerInvariant();
        if (!int.TryParse(t[4].Replace("px", "", StringComparison.OrdinalIgnoreCase), out int v)) return "gaps needs a number";
        var c = wm.Config;
        int Apply(int cur) => op switch
        {
            "set" => v,
            "plus" => cur + v,
            "minus" => cur - v,
            "toggle" => cur == 0 ? v : 0,
            _ => cur,
        };
        switch (type)
        {
            case "inner": c.GapsInner = Math.Max(0, Apply(c.GapsInner)); break;
            case "outer":
                c.GapsTop = Apply(c.GapsTop); c.GapsBottom = Apply(c.GapsBottom);
                c.GapsLeft = Apply(c.GapsLeft); c.GapsRight = Apply(c.GapsRight);
                break;
            case "horizontal": c.GapsLeft = Apply(c.GapsLeft); c.GapsRight = Apply(c.GapsRight); break;
            case "vertical": c.GapsTop = Apply(c.GapsTop); c.GapsBottom = Apply(c.GapsBottom); break;
            case "top": c.GapsTop = Apply(c.GapsTop); break;
            case "bottom": c.GapsBottom = Apply(c.GapsBottom); break;
            case "left": c.GapsLeft = Apply(c.GapsLeft); break;
            case "right": c.GapsRight = Apply(c.GapsRight); break;
            default: return $"unknown gaps type '{t[1]}'";
        }
        foreach (var mw in wm.Windows.Values) mw.LastTarget = null;
        return null;
    }

    private string? Bar(List<string> t)
    {
        if (wm.Config.Bar == null) return "no bar configured";
        if (t.Count < 3 || !t[1].Equals("mode", StringComparison.OrdinalIgnoreCase)) return null; // hidden_state etc: ignored
        string mode = t[2].ToLowerInvariant();
        wm.Config.Bar.Mode = mode switch
        {
            "toggle" => wm.Config.Bar.Mode == "invisible" ? "dock" : "invisible",
            "invisible" or "hide" => "invisible",
            _ => "dock",
        };
        wm.OnDisplayChanged("bar mode");
        return null;
    }

    private string? Confirm(List<string> t)
    {
        if (t.Count < 3) return "usage: confirm \"question\" <command>";
        string question = t[1];
        var sb = new StringBuilder();
        foreach (var tok in t.Skip(2))
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(tok.Contains(' ') || tok.Contains(',') || tok.Contains(';') ? "\"" + tok.Replace("\"", "\\\"") + "\"" : tok);
        }
        string command = sb.ToString();
        var owner = wm.MainHandle;
        var thread = new Thread(() =>
        {
            // A no-op input event lets this process take the foreground, so the question gets the keyboard
            // (Enter = Yes) instead of appearing behind the focused window.
            WindowOps.AllowForeground();
            int r = User32.MessageBoxW(0, question, "Mozaic", C.MB_YESNO | C.MB_ICONQUESTION | C.MB_TOPMOST | C.MB_SETFOREGROUND);
            if (r == C.IDYES) Dispatcher.Post(() => wm.RunCommand(command, true));
        }) { IsBackground = true, Name = "confirm" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return null;
    }
}
