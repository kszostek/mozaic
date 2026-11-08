using System.Globalization;
using System.Text;
using System.Text.Json;
using Mozaic.Platform;

namespace Mozaic.State;

/// <summary>Saves the layout to disk (debounced) so a restart or crash does not lose it.</summary>
internal sealed class StateStore(string path)
{
    private nuint _timer;

    public void ScheduleSave(WindowManager wm)
    {
        if (_timer != 0) return;
        _timer = Dispatcher.SetTimeout(1500, () =>
        {
            _timer = 0;
            Save(wm);
        });
    }

    public void Save(WindowManager wm)
    {
        Dispatcher.ClearTimer(ref _timer);
        try
        {
            using var ms = new MemoryStream();
            using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = false }))
            {
                w.WriteStartObject();
                w.WriteNumber("version", 1);
                w.WriteNumber("saved", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                w.WriteString("previous_workspace", wm.Tree.PreviousWorkspaceName);
                w.WriteNumber("focused_window", (long)(wm.Tree.FocusedWindow?.Handle ?? 0));
                w.WritePropertyName("tree");
                TreeJson.WriteNode(w, wm.Tree.Root, wm.Tree);
                w.WriteStartArray("scratchpad");
                foreach (var s in wm.Tree.Scratchpad) TreeJson.WriteNode(w, s, wm.Tree);
                w.WriteEndArray();
                w.WriteEndObject();
            }
            var tmp = path + ".tmp";
            File.WriteAllBytes(tmp, ms.ToArray());
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warn("saving state failed: " + e.Message);
        }
    }

    public JsonDocument? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonDocument.Parse(File.ReadAllBytes(path));
        }
        catch (Exception e)
        {
            Log.Warn("cannot read saved layout: " + e.Message);
            return null;
        }
    }
}

internal readonly record struct HiddenEntry(IntPtr Hwnd, uint Pid, string ClassName);

/// <summary>
/// Remembers which windows Mozaic has hidden. If Mozaic is killed, the next start shows them again,
/// so no window can ever get "lost".
/// </summary>
internal sealed class HiddenRegistry(string path)
{
    public List<HiddenEntry> Load()
    {
        var list = new List<HiddenEntry>();
        try
        {
            if (!File.Exists(path)) return list;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('\t');
                if (parts.Length < 3) continue;
                if (long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long h) &&
                    uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint pid))
                    list.Add(new HiddenEntry((IntPtr)h, pid, parts[2]));
            }
        }
        catch (Exception e)
        {
            Log.Warn("cannot read hidden window list: " + e.Message);
        }
        return list;
    }

    public void Save(IEnumerable<HiddenEntry> entries)
    {
        try
        {
            var sb = new StringBuilder();
            foreach (var e in entries)
                sb.Append(((long)e.Hwnd).ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Pid.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(e.ClassName).Append('\n');
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, sb.ToString());
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception e)
        {
            Log.Warn("cannot write hidden window list: " + e.Message);
        }
    }

    public void Delete()
    {
        try { File.Delete(path); }
        catch (IOException) { }
    }
}
