using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Mozaic.Ipc;

namespace Mozaic.Msg;

/// <summary>mozaic-msg: send commands to / query a running Mozaic (like i3-msg).</summary>
internal static class Program
{
    private const string Usage = """
Usage: mozaic-msg [-t <type>] [-q] [-r] [--instance <name>] [message]

Types: command (default), get_tree, get_workspaces, get_outputs, get_windows, get_marks,
       get_binding_modes, get_binding_state, get_version, get_config, send_tick, subscribe

Examples:
  mozaic-msg workspace 3
  mozaic-msg "[process=firefox] focus"
  mozaic-msg -t get_workspaces
  mozaic-msg -t subscribe '["window","workspace","mode"]'
Options:
  -q, --quiet     only report errors (exit code 1 if a command failed)
  -r, --raw       print the JSON reply without pretty-printing
""";

    private static int Main(string[] args)
    {
        string type = "command";
        bool quiet = false, raw = false;
        string instance = "";
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-t":
                case "--type":
                    if (++i >= args.Length) return Fail("missing value for -t");
                    type = args[i];
                    break;
                case "-q":
                case "--quiet":
                    quiet = true;
                    break;
                case "-r":
                case "--raw":
                    raw = true;
                    break;
                case "--instance":
                    if (++i >= args.Length) return Fail("missing value for --instance");
                    instance = args[i];
                    break;
                case "-h":
                case "--help":
                    Console.WriteLine(Usage);
                    return 0;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }
        if (Array.IndexOf(IpcProtocol.Types, type) < 0) return Fail($"unknown message type '{type}'\n\n{Usage}");
        string payload = string.Join(' ', rest);
        if (type == "command" && payload.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName(instance), PipeDirection.InOut);
            pipe.Connect(3000);
            var utf8 = new UTF8Encoding(false);
            using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
            writer.WriteLine(IpcProtocol.Request(type, payload));
            Console.OutputEncoding = utf8;
            if (type == "subscribe")
            {
                string? line;
                while ((line = reader.ReadLine()) != null) Console.WriteLine(line);
                return 0;
            }
            var reply = reader.ReadLine();
            if (reply == null)
            {
                // e.g. "exit"/"restart": the window manager went away before answering.
                return 0;
            }
            bool ok = Succeeded(reply);
            if (!quiet || !ok) Console.WriteLine(raw ? reply : Pretty(reply));
            return ok ? 0 : 1;
        }
        catch (TimeoutException)
        {
            return Fail("Mozaic is not running (could not connect).");
        }
        catch (IOException e)
        {
            return Fail("connection error: " + e.Message);
        }
    }

    private static bool Succeeded(string reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply);
            var r = doc.RootElement;
            if (r.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in r.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                        return false;
                return true;
            }
            return !(r.ValueKind == JsonValueKind.Object && r.TryGetProperty("success", out var s2) && s2.ValueKind == JsonValueKind.False);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string Pretty(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
                doc.WriteTo(w);
            return Encoding.UTF8.GetString(ms.ToArray());
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static int Fail(string msg)
    {
        Console.Error.WriteLine("mozaic-msg: " + msg);
        return 1;
    }
}
