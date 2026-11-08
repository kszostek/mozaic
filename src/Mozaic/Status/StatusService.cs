using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Mozaic.Core.Commands;
using Mozaic.Core.Config;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.Status;

internal sealed record StatusBlock(string Text, uint? Color = null, uint? Background = null, bool Urgent = false,
    bool Separator = true, int SeparatorWidth = 9);

/// <summary>
/// Produces the status blocks shown on the right of the bar: either built-in blocks
/// (cpu, memory, battery, disk, datetime) or the output of an i3bar-compatible status_command.
/// </summary>
internal sealed unsafe class StatusService(WindowManager wm)
{
    private const uint Good = 0x8BD49C, Degraded = 0xE5C07B, Bad = 0xFF6B6B;

    private nuint _timer;
    private Process? _process;
    private long _prevIdle, _prevKernel, _prevUser;
    private BarConfig? _bar;
    private int _generation;

    public List<StatusBlock> Blocks { get; private set; } = new();

    public void Start(BarConfig? bar)
    {
        Stop();
        _bar = bar;
        if (bar == null) return;
        int gen = ++_generation;
        if (!string.IsNullOrWhiteSpace(bar.StatusCommand))
        {
            StartCommand(bar.StatusCommand!, gen);
            return;
        }
        Tick();
        bool seconds = bar.StatusBlocks.Contains("datetime") && bar.DateTimeFormat.Contains('s');
        _timer = Dispatcher.SetInterval(seconds ? 1000 : bar.StatusInterval * 1000, Tick);
    }

    public void Stop()
    {
        _generation++;
        Dispatcher.ClearTimer(ref _timer);
        if (_process != null)
        {
            try { if (!_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (Exception) { }
            _process.Dispose();
            _process = null;
        }
    }

    // =====================================================================================
    // Built-in blocks
    // =====================================================================================

    private void Tick()
    {
        var bar = _bar;
        if (bar == null) return;
        var blocks = new List<StatusBlock>();
        foreach (var name in bar.StatusBlocks)
        {
            try
            {
                var b = name switch
                {
                    "cpu" => Cpu(),
                    "memory" or "mem" or "ram" => Memory(),
                    "battery" or "bat" => Battery(),
                    "disk" => Disk(),
                    "datetime" or "time" or "date" or "clock" => new StatusBlock(DateTime.Now.ToString(bar.DateTimeFormat, CultureInfo.CurrentCulture)),
                    _ => new StatusBlock(name),
                };
                if (b != null) blocks.Add(b);
            }
            catch (Exception e)
            {
                Log.Debug($"status block {name}: {e.Message}");
            }
        }
        Blocks = blocks;
        wm.InvalidateBars();
    }

    private StatusBlock? Cpu()
    {
        if (!Kernel32.GetSystemTimes(out long idle, out long kernel, out long user)) return null;
        long di = idle - _prevIdle, dk = kernel - _prevKernel, du = user - _prevUser;
        bool first = _prevKernel == 0;
        _prevIdle = idle;
        _prevKernel = kernel;
        _prevUser = user;
        long total = dk + du;
        if (first || total <= 0) return new StatusBlock("CPU --%");
        int pct = (int)Math.Round(100.0 * (total - di) / total);
        pct = Math.Clamp(pct, 0, 100);
        return new StatusBlock($"CPU {pct,2}%", pct >= 90 ? Bad : pct >= 70 ? Degraded : null);
    }

    private static StatusBlock? Memory()
    {
        MEMORYSTATUSEX m = default;
        m.dwLength = (uint)sizeof(MEMORYSTATUSEX);
        if (!Kernel32.GlobalMemoryStatusEx(&m)) return null;
        double usedGb = (m.ullTotalPhys - m.ullAvailPhys) / 1073741824.0;
        uint pct = m.dwMemoryLoad;
        return new StatusBlock($"RAM {usedGb.ToString("0.0", CultureInfo.InvariantCulture)}G ({pct}%)", pct >= 90 ? Bad : pct >= 75 ? Degraded : null);
    }

    private static StatusBlock? Battery()
    {
        if (!Kernel32.GetSystemPowerStatus(out var s)) return null;
        if ((s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255 || s.BatteryLifePercent == 255) return null; // no battery
        int pct = s.BatteryLifePercent;
        bool charging = s.ACLineStatus == 1;
        string label = charging ? "CHR" : "BAT";
        uint? color = charging ? Good : pct <= 10 ? Bad : pct <= 25 ? Degraded : null;
        return new StatusBlock($"{label} {pct}%", color, Urgent: !charging && pct <= 5);
    }

    private static StatusBlock? Disk()
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        fixed (char* p = root)
        {
            if (!Kernel32.GetDiskFreeSpaceExW(p, out ulong free, out _, out _)) return null;
            double gb = free / 1073741824.0;
            return new StatusBlock($"{root.TrimEnd('\\')} {gb.ToString("0", CultureInfo.InvariantCulture)}G free", gb < 10 ? Bad : null);
        }
    }

    // =====================================================================================
    // status_command (plain text lines or the i3bar JSON protocol)
    // =====================================================================================

    private void StartCommand(string commandLine, int gen)
    {
        var (file, args) = CommandParser.SplitCommandLine(Environment.ExpandEnvironmentVariables(commandLine));
        var psi = new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        try
        {
            _process = Process.Start(psi);
        }
        catch (Exception e)
        {
            Blocks = new List<StatusBlock> { new("status_command failed: " + e.Message, Bad) };
            return;
        }
        var proc = _process!;
        var thread = new Thread(() => ReadLoop(proc, gen)) { IsBackground = true, Name = "status_command" };
        thread.Start();
    }

    private void ReadLoop(Process proc, int gen)
    {
        try
        {
            var reader = proc.StandardOutput;
            string? first = reader.ReadLine();
            if (first == null) { Publish(gen, new List<StatusBlock> { new("status_command exited", Bad) }); return; }
            bool json = first.TrimStart().StartsWith('{') && first.Contains("\"version\"", StringComparison.Ordinal);
            if (!json) Publish(gen, new List<StatusBlock> { new(first) });
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (gen != _generation) return;
                if (!json)
                {
                    Publish(gen, new List<StatusBlock> { new(line) });
                    continue;
                }
                var t = line.Trim();
                if (t.StartsWith(',')) t = t[1..].TrimStart();
                if (t.EndsWith(',')) t = t[..^1];
                if (t.Length == 0 || t == "[") continue;
                var blocks = ParseI3Bar(t);
                if (blocks != null) Publish(gen, blocks);
            }
            proc.WaitForExit(1000);
            Publish(gen, new List<StatusBlock> { new($"status_command exited ({(proc.HasExited ? proc.ExitCode : -1)})", Bad) });
        }
        catch (Exception e)
        {
            if (gen == _generation) Log.Warn("status_command: " + e.Message);
        }
    }

    private static List<StatusBlock>? ParseI3Bar(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var list = new List<StatusBlock>();
            foreach (var b in doc.RootElement.EnumerateArray())
            {
                if (!b.TryGetProperty("full_text", out var ft)) continue;
                uint? color = TryColor(b, "color"), bg = TryColor(b, "background");
                bool urgent = b.TryGetProperty("urgent", out var u) && u.ValueKind == JsonValueKind.True;
                bool sep = !b.TryGetProperty("separator", out var s) || s.ValueKind != JsonValueKind.False;
                int sepw = b.TryGetProperty("separator_block_width", out var sw) && sw.TryGetInt32(out int v) ? v : 9;
                list.Add(new StatusBlock(StripMarkup(ft.GetString() ?? ""), color, bg, urgent, sep, sepw));
            }
            return list;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static uint? TryColor(JsonElement b, string name)
    {
        if (!b.TryGetProperty(name, out var c) || c.ValueKind != JsonValueKind.String) return null;
        try { return ConfigParser.ParseColor(c.GetString() ?? ""); }
        catch (FormatException) { return null; }
    }

    private static string StripMarkup(string s)
    {
        if (s.IndexOf('<') < 0) return s;
        var sb = new StringBuilder();
        bool inTag = false;
        foreach (char ch in s)
        {
            if (ch == '<') inTag = true;
            else if (ch == '>') inTag = false;
            else if (!inTag) sb.Append(ch);
        }
        return sb.ToString().Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">");
    }

    private void Publish(int gen, List<StatusBlock> blocks)
    {
        Dispatcher.Post(() =>
        {
            if (gen != _generation) return;
            Blocks = blocks;
            wm.InvalidateBars();
        });
    }
}
