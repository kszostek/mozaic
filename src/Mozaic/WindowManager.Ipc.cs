using System.Text;
using System.Text.Json;
using Mozaic.Core;
using Mozaic.Platform;
using Mozaic.State;

namespace Mozaic;

internal sealed partial class WindowManager
{
    /// <summary>Answers an IPC request (main thread). Shapes follow i3's IPC replies where they exist.</summary>
    public string HandleIpc(string type, string payload)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            switch (type)
            {
                case "command":
                {
                    var outcome = RunCommand(payload, fromUser: true);
                    w.WriteStartArray();
                    w.WriteStartObject();
                    w.WriteBoolean("success", outcome.Success);
                    if (!outcome.Success) w.WriteString("error", string.Join("; ", outcome.Errors));
                    w.WriteEndObject();
                    w.WriteEndArray();
                    break;
                }
                case "get_tree":
                    TreeJson.WriteNode(w, Tree.Root, Tree);
                    break;
                case "get_workspaces":
                    w.WriteStartArray();
                    foreach (var ws in Tree.OrderedWorkspaces())
                    {
                        w.WriteStartObject();
                        w.WriteNumber("id", ws.Id);
                        w.WriteNumber("num", ws.Number);
                        w.WriteString("name", ws.Name);
                        w.WriteBoolean("visible", ws.Parent?.CurrentWorkspace == ws);
                        w.WriteBoolean("focused", Tree.FocusedWorkspace == ws);
                        w.WriteBoolean("urgent", Tree.WindowsIn(ws).Any(x => x.Urgent));
                        w.WriteString("output", ws.Parent?.Name);
                        TreeJson.WriteRect(w, "rect", ws.Parent?.LayoutArea ?? default);
                        w.WriteNumber("windows", Tree.WindowsIn(ws).Count());
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    break;
                case "get_outputs":
                    w.WriteStartArray();
                    foreach (var o in Tree.Outputs)
                    {
                        w.WriteStartObject();
                        w.WriteString("name", o.Name);
                        w.WriteBoolean("active", true);
                        w.WriteBoolean("primary", o.Primary);
                        w.WriteString("current_workspace", o.CurrentWorkspace?.Name);
                        w.WriteNumber("dpi", o.Dpi);
                        TreeJson.WriteRect(w, "rect", o.OutputRect);
                        TreeJson.WriteRect(w, "work_area", o.WorkArea);
                        TreeJson.WriteRect(w, "layout_area", o.LayoutArea);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    break;
                case "get_marks":
                    w.WriteStartArray();
                    foreach (var m in Tree.AllNodes().SelectMany(n => n.Marks).Distinct()) w.WriteStringValue(m);
                    w.WriteEndArray();
                    break;
                case "get_binding_modes":
                    w.WriteStartArray();
                    foreach (var m in Config.Modes) w.WriteStringValue(m);
                    w.WriteEndArray();
                    break;
                case "get_binding_state":
                    w.WriteStartObject();
                    w.WriteString("name", Mode);
                    w.WriteEndObject();
                    break;
                case "get_version":
                {
                    var v = typeof(WindowManager).Assembly.GetName().Version ?? new Version(1, 0, 0);
                    w.WriteStartObject();
                    w.WriteNumber("major", v.Major);
                    w.WriteNumber("minor", v.Minor);
                    w.WriteNumber("patch", Math.Max(0, v.Build));
                    w.WriteString("human_readable", $"Mozaic {v.ToString(3)}");
                    w.WriteString("loaded_config_file_name", ConfigPath);
                    w.WriteString("executable", Environment.ProcessPath);
                    w.WriteBoolean("paused", Paused);
                    w.WriteNumber("windows", _windows.Count);
                    w.WriteEndObject();
                    break;
                }
                case "get_candidates":
                    WriteCandidates(w);
                    break;
                case "get_config":
                    w.WriteStartObject();
                    w.WriteString("config", File.Exists(ConfigPath) ? File.ReadAllText(ConfigPath) : "");
                    w.WriteStartArray("errors");
                    foreach (var e in ConfigErrors) w.WriteStringValue(e.ToString());
                    w.WriteEndArray();
                    w.WriteEndObject();
                    break;
                case "get_windows":
                    w.WriteStartArray();
                    foreach (var mw in _windows.Values)
                    {
                        var n = mw.Node;
                        w.WriteStartObject();
                        w.WriteNumber("con_id", n.Id);
                        w.WriteNumber("window", (long)mw.Hwnd);
                        w.WriteString("title", n.Title);
                        w.WriteString("class", mw.ClassName);
                        w.WriteString("process", mw.ProcessName);
                        w.WriteString("workspace", Tree.WorkspaceOf(n)?.Name);
                        w.WriteBoolean("floating", n.InFloating);
                        w.WriteBoolean("focused", Tree.FocusedWindow == n);
                        w.WriteBoolean("visible", n.Visible && !mw.HiddenByUs);
                        w.WriteBoolean("scratchpad", n.IsScratchpad);
                        TreeJson.WriteRect(w, "rect", n.Visible ? n.WindowRect : default);
                        TreeJson.WriteRect(w, "actual_rect", WindowQuery.FrameRect(mw.Hwnd));
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    break;
                case "send_tick":
                    w.WriteStartObject();
                    w.WriteBoolean("success", true);
                    w.WriteEndObject();
                    break;
                default:
                    w.WriteStartObject();
                    w.WriteBoolean("success", false);
                    w.WriteString("error", $"unknown message type '{type}'");
                    w.WriteEndObject();
                    break;
            }
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>Every visible top-level window with Mozaic's decision about it ("why isn't X tiled?").</summary>
    private void WriteCandidates(Utf8JsonWriter w)
    {
        w.WriteStartArray();
        foreach (var hwnd in WindowQuery.TopLevelWindows())
        {
            _windows.TryGetValue(hwnd, out var mw);
            _detached.TryGetValue(hwnd, out var det);
            if (mw == null && det == null && !Native.User32.IsWindowVisible(hwnd)) continue;
            string verdict, reason;
            string cls = WindowQuery.ClassName(hwnd);
            string title = WindowQuery.Title(hwnd);
            string process;
            if (mw != null)
            {
                verdict = mw.Node.IsScratchpad && mw.Node.Parent == null ? "scratchpad" : mw.Node.InFloating ? "floating" : "tiled";
                reason = "";
                process = mw.ProcessName;
            }
            else if (det != null)
            {
                verdict = "minimized";
                reason = "";
                process = det.Window.ProcessName;
            }
            else
            {
                var v = WindowRules.Classify(hwnd, Config, out var snap, out reason);
                verdict = v switch
                {
                    Verdict.Ignore => "ignored",
                    Verdict.NotYet => "pending",
                    Verdict.Float => "floating",
                    _ => "tiled",
                };
                process = snap?.ProcessName ?? WindowQuery.EffectiveProcessName(hwnd, cls);
                // Hide the noise (shell helpers, own windows) unless it has a title.
                if (v == Verdict.Ignore && title.Length == 0) continue;
            }
            w.WriteStartObject();
            w.WriteNumber("window", (long)hwnd);
            w.WriteString("title", title);
            w.WriteString("class", cls);
            w.WriteString("process", process);
            w.WriteString("verdict", verdict);
            w.WriteString("reason", reason);
            w.WriteBoolean("managed", mw != null);
            w.WriteString("workspace", mw != null ? Tree.WorkspaceOf(mw.Node)?.Name : null);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
