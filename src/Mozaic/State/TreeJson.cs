using System.Text.Json;
using Mozaic.Core;
using Mozaic.Platform;

namespace Mozaic.State;

/// <summary>Serializes the tree (i3 get_tree style). Used by IPC and for saving the layout.</summary>
internal static class TreeJson
{
    public static string TypeName(NodeKind k) => k switch
    {
        NodeKind.Root => "root",
        NodeKind.Output => "output",
        NodeKind.Workspace => "workspace",
        NodeKind.Container => "con",
        _ => "window",
    };

    public static void WriteRect(Utf8JsonWriter w, string name, Rect r)
    {
        w.WriteStartObject(name);
        w.WriteNumber("x", r.Left);
        w.WriteNumber("y", r.Top);
        w.WriteNumber("width", r.Width);
        w.WriteNumber("height", r.Height);
        w.WriteEndObject();
    }

    public static Rect? ReadRect(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var r) || r.ValueKind != JsonValueKind.Object) return null;
        return Rect.FromSize(r.GetProperty("x").GetInt32(), r.GetProperty("y").GetInt32(),
            r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32());
    }

    public static void WriteNode(Utf8JsonWriter w, Node n, Tree tree)
    {
        w.WriteStartObject();
        w.WriteNumber("id", n.Id);
        w.WriteString("type", TypeName(n.Kind));
        w.WriteString("name", n.Kind == NodeKind.Window ? n.Title : n.Name);
        if (n.Kind is NodeKind.Workspace or NodeKind.Container)
        {
            w.WriteString("layout", Tree.LayoutName(n.Layout));
            w.WriteString("last_split_layout", Tree.LayoutName(n.LastSplitLayout));
        }
        if (n.Kind == NodeKind.Workspace)
        {
            w.WriteNumber("num", n.Number);
            w.WriteBoolean("visible", n.Parent?.CurrentWorkspace == n);
            if (n.WorkspaceLayout is LayoutKind wl) w.WriteString("workspace_layout", Tree.LayoutName(wl));
        }
        if (n.Kind == NodeKind.Output)
        {
            w.WriteBoolean("primary", n.Primary);
            w.WriteString("current_workspace", n.CurrentWorkspace?.Name);
            w.WriteNumber("dpi", n.Dpi);
            WriteRect(w, "work_area", n.WorkArea);
        }
        w.WriteNumber("percent", n.Percent);
        w.WriteBoolean("focused", tree.Focused == n);
        w.WriteBoolean("urgent", n.Kind == NodeKind.Window ? n.Urgent : Tree.WindowsIn(n).Any(x => x.Urgent));
        WriteRect(w, "rect", n.Kind switch
        {
            NodeKind.Output => n.OutputRect,
            NodeKind.Window => n.WindowRect,
            _ => n.Rect,
        });
        if (n.Kind == NodeKind.Window)
        {
            w.WriteNumber("window", (long)n.Handle);
            w.WriteBoolean("floating", n.InFloating);
            w.WriteBoolean("fullscreen", n.Fullscreen);
            w.WriteBoolean("sticky", n.Sticky);
            w.WriteBoolean("scratchpad", n.IsScratchpad);
            w.WriteBoolean("visible", n.Visible);
            if (n.FloatRect is Rect fr) WriteRect(w, "float_rect", fr);
            if (n.Tag is ManagedWindow mw)
            {
                w.WriteStartObject("window_properties");
                w.WriteString("class", mw.ClassName);
                w.WriteString("process", mw.ProcessName);
                w.WriteString("title", n.Title);
                w.WriteNumber("pid", mw.Pid);
                w.WriteBoolean("dialog", mw.IsDialog);
                w.WriteEndObject();
            }
        }
        w.WriteStartArray("marks");
        foreach (var m in n.Marks) w.WriteStringValue(m);
        w.WriteEndArray();
        w.WriteStartArray("focus");
        foreach (var f in n.FocusStack) w.WriteNumberValue(f.Id);
        w.WriteEndArray();
        w.WriteStartArray("nodes");
        foreach (var c in n.Children) WriteNode(w, c, tree);
        w.WriteEndArray();
        w.WriteStartArray("floating_nodes");
        foreach (var f in n.Floating) WriteNode(w, f, tree);
        w.WriteEndArray();
        w.WriteEndObject();
    }
}
