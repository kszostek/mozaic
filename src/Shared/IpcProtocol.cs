using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Mozaic.Ipc;

/// <summary>
/// Wire protocol between Mozaic and mozaic-msg: one JSON object per line over a per-user named pipe.
/// Request: {"type":"command","payload":"focus left"}  Response: one JSON line (i3-msg compatible shapes).
/// </summary>
internal static class IpcProtocol
{
    public static readonly string[] Types =
    {
        "command", "get_tree", "get_workspaces", "get_outputs", "get_marks", "get_binding_modes",
        "get_binding_state", "get_version", "get_config", "get_windows", "get_candidates", "subscribe", "send_tick",
    };

    private static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return sb.ToString();
    }

    public static string PipeName(string instance)
    {
        int session;
        using (var p = Process.GetCurrentProcess()) session = p.SessionId;
        var name = $"mozaic-{Sanitize(Environment.UserName)}-{session}";
        return instance.Length > 0 ? name + "-" + Sanitize(instance) : name;
    }

    public static string Request(string type, string payload)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("type", type);
            w.WriteString("payload", payload);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static (string Type, string Payload) ParseRequest(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            string type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
            string payload = root.TryGetProperty("payload", out var p) ? p.GetString() ?? "" : "";
            return (type, payload);
        }
        catch (JsonException)
        {
            // Allow plain text for quick scripting: a bare line is a command.
            return ("command", line);
        }
    }
}
