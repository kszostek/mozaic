using System.Text.Json;
using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;
using Mozaic.State;

namespace Mozaic;

internal sealed partial class WindowManager
{
    public void SaveStateNow() => _state.Save(this);

    /// <summary>Rebuilds the saved layout for windows that still exist (after restart, crash or re-launch).</summary>
    private void RestoreState(List<HiddenEntry> hiddenBefore)
    {
        using var doc = _state.Load();
        if (doc == null) return;
        var root = doc.RootElement;
        if (!root.TryGetProperty("tree", out var tree) || !tree.TryGetProperty("nodes", out var outputs)) return;
        var hiddenSet = hiddenBefore.Select(h => h.Hwnd).ToHashSet();
        var idMap = new Dictionary<int, Node>();
        int restored = 0;

        try
        {
            foreach (var outEl in outputs.EnumerateArray())
            {
                string outName = outEl.GetProperty("name").GetString() ?? "";
                var output = Tree.FindOutput(outName) ?? Tree.Outputs.FirstOrDefault(o => o.Primary) ?? Tree.Outputs[0];
                string? current = outEl.TryGetProperty("current_workspace", out var cw) ? cw.GetString() : null;
                foreach (var wsEl in outEl.GetProperty("nodes").EnumerateArray())
                {
                    string name = wsEl.GetProperty("name").GetString() ?? "";
                    if (name.Length == 0) continue;
                    var ws = Tree.FindWorkspace(name) ?? Tree.CreateWorkspace(name, output);
                    if (ws.Children.Count > 0 || ws.Floating.Count > 0) continue;
                    ws.Layout = ReadLayout(wsEl, "layout", ws.Layout);
                    ws.LastSplitLayout = ReadLayout(wsEl, "last_split_layout", ws.LastSplitLayout);
                    if (wsEl.TryGetProperty("workspace_layout", out var wl) && Tree.TryParseLayout(wl.GetString() ?? "", out var wlk))
                        ws.WorkspaceLayout = wlk;
                    foreach (var c in wsEl.GetProperty("nodes").EnumerateArray())
                        if (BuildNode(c, idMap, hiddenSet, ref restored) is { } k) Tree.RestoreAttach(k, ws, false);
                    foreach (var f in wsEl.GetProperty("floating_nodes").EnumerateArray())
                        if (BuildNode(f, idMap, hiddenSet, ref restored) is { } k) Tree.RestoreAttach(k, ws, true);
                    Tree.FixPercent(ws);
                    RestoreFocusOrder(ws, wsEl, idMap);
                    if (name == current && ws.Parent == output) Tree.SetVisibleWorkspace(ws);
                }
            }
            if (root.TryGetProperty("scratchpad", out var scratch))
            {
                foreach (var s in scratch.EnumerateArray())
                {
                    if (BuildNode(s, idMap, hiddenSet, ref restored) is not { Kind: NodeKind.Window } n) continue;
                    n.IsScratchpad = true;
                    Tree.Scratchpad.Add(n);
                }
            }
            if (root.TryGetProperty("previous_workspace", out var pw) && pw.ValueKind == JsonValueKind.String)
                Tree.PreviousWorkspaceName = pw.GetString();
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            Log.Warn("saved layout is invalid: " + e.Message);
        }

        Tree.PruneEmptyWorkspaces();
        if (restored > 0)
        {
            if (root.TryGetProperty("focused_window", out var fw) && _windows.TryGetValue((IntPtr)fw.GetInt64(), out var fmw) && fmw.Node.Parent != null)
                Tree.Focus(fmw.Node);
            Log.Info($"restored {restored} window(s) from the saved layout");
        }
    }

    private static LayoutKind ReadLayout(JsonElement e, string prop, LayoutKind fallback) =>
        e.TryGetProperty(prop, out var v) && Tree.TryParseLayout(v.GetString() ?? "", out var l) ? l : fallback;

    private static void RestoreFocusOrder(Node parent, JsonElement e, Dictionary<int, Node> idMap)
    {
        if (!e.TryGetProperty("focus", out var focus)) return;
        var order = new List<Node>();
        foreach (var id in focus.EnumerateArray())
            if (idMap.TryGetValue(id.GetInt32(), out var n) && n.Parent == parent && !order.Contains(n)) order.Add(n);
        foreach (var n in parent.FocusStack)
            if (!order.Contains(n)) order.Add(n);
        parent.FocusStack.Clear();
        parent.FocusStack.AddRange(order);
    }

    private Node? BuildNode(JsonElement e, Dictionary<int, Node> idMap, HashSet<IntPtr> hiddenSet, ref int restored)
    {
        string type = e.GetProperty("type").GetString() ?? "";
        int oldId = e.GetProperty("id").GetInt32();
        if (type == "window")
        {
            var hwnd = (IntPtr)e.GetProperty("window").GetInt64();
            if (!User32.IsWindow(hwnd) || _windows.ContainsKey(hwnd) || User32.IsIconic(hwnd)) return null;
            if (!e.TryGetProperty("window_properties", out var props)) return null;
            uint pid = props.GetProperty("pid").GetUInt32();
            string cls = props.GetProperty("class").GetString() ?? "";
            if (WindowQuery.Pid(hwnd) != pid || WindowQuery.ClassName(hwnd) != cls) return null;
            bool visible = User32.IsWindowVisible(hwnd);
            // A window that is invisible but was not hidden by us was hidden by its application: skip it.
            if (!visible && !hiddenSet.Contains(hwnd)) return null;
            if (visible && WindowQuery.IsCloaked(hwnd)) return null;

            bool dialog = props.TryGetProperty("dialog", out var dl) && dl.GetBoolean();
            var snap = new WindowSnapshot(hwnd, pid, cls, WindowQuery.Title(hwnd), WindowQuery.EffectiveProcessName(hwnd, cls),
                WindowQuery.Style(hwnd), WindowQuery.ExStyle(hwnd), WindowQuery.Owner(hwnd), dialog);
            var node = new Node(NodeKind.Window) { Handle = hwnd };
            var mw = new ManagedWindow(snap, node) { HiddenByUs = !visible };
            node.Tag = mw;
            node.Percent = e.GetProperty("percent").GetDouble();
            node.FloatRect = TreeJson.ReadRect(e, "float_rect");
            node.Sticky = e.TryGetProperty("sticky", out var st) && st.GetBoolean();
            node.Fullscreen = e.TryGetProperty("fullscreen", out var fs) && fs.GetBoolean();
            foreach (var m in e.GetProperty("marks").EnumerateArray()) node.Marks.Add(m.GetString() ?? "");
            _windows[hwnd] = mw;
            InputHooks.ManagedHandles[hwnd] = 0;
            if (visible) WindowOps.UpdateFrameInsets(mw);
            ApplyCorner(mw);
            idMap[oldId] = node;
            restored++;
            return node;
        }
        if (type == "con")
        {
            var c = new Node(NodeKind.Container)
            {
                Layout = ReadLayout(e, "layout", LayoutKind.SplitH),
                LastSplitLayout = ReadLayout(e, "last_split_layout", LayoutKind.SplitH),
                Percent = e.GetProperty("percent").GetDouble(),
            };
            foreach (var child in e.GetProperty("nodes").EnumerateArray())
                if (BuildNode(child, idMap, hiddenSet, ref restored) is { } k) Tree.RestoreAttach(k, c, false);
            if (c.Children.Count == 0) return null;
            if (c.Children.Count == 1 && c.IsSplit && Config.FlattenContainers)
            {
                var only = c.Children[0];
                only.Percent = c.Percent;
                only.Parent = null;
                return only;
            }
            Tree.FixPercent(c);
            RestoreFocusOrder(c, e, idMap);
            idMap[oldId] = c;
            return c;
        }
        return null;
    }

    /// <summary>Shows windows that a previous (crashed/killed) Mozaic left hidden and that weren't restored.</summary>
    private void RecoverHiddenWindows(List<HiddenEntry> entries)
    {
        foreach (var e in entries)
        {
            if (_windows.ContainsKey(e.Hwnd) || !User32.IsWindow(e.Hwnd)) continue;
            if (WindowQuery.Pid(e.Hwnd) != e.Pid || WindowQuery.ClassName(e.Hwnd) != e.ClassName) continue;
            if (User32.IsWindowVisible(e.Hwnd)) continue;
            User32.ShowWindowAsync(e.Hwnd, C.SW_SHOWNA);
            ScheduleCheck(e.Hwnd);
            Log.Info("recovered window left hidden by a previous run: " + WindowQuery.Describe(e.Hwnd));
        }
    }
}
