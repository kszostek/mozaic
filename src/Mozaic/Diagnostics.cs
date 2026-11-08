using System.Text;
using System.Text.Json;
using Mozaic.Core;
using Mozaic.Core.Config;
using Mozaic.Native;
using Mozaic.Platform;
using Mozaic.State;

namespace Mozaic;

/// <summary>
/// --dry-run: classifies the existing windows and computes the layout Mozaic would apply,
/// without hooking anything or touching a single window. Useful to see what would be managed.
/// </summary>
internal static class DryRun
{
    public static int Run(AppOptions opts)
    {
        var cfgPath = opts.ConfigPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "mozaic", "config");
        var cfg = File.Exists(cfgPath) ? ConfigParser.Parse(File.ReadAllText(cfgPath), cfgPath) : ConfigParser.Parse(DefaultConfig.Text);
        var tree = new Tree(new TreeOptions { DefaultOrientation = cfg.DefaultOrientation, FlattenContainers = cfg.FlattenContainers });
        foreach (var m in Monitors.Enumerate())
        {
            var o = tree.AddOutput(m.Name, m.Bounds, m.WorkArea, m.Primary, m.Dpi);
            tree.EnsureWorkspace(o);
        }
        var report = new List<(IntPtr Hwnd, Verdict Verdict, string Desc)>();
        foreach (var hwnd in WindowQuery.TopLevelWindows())
        {
            if (!User32.IsWindowVisible(hwnd)) continue;
            var v = WindowRules.Classify(hwnd, cfg, out var snap);
            report.Add((hwnd, v, WindowQuery.Describe(hwnd)));
            if (snap == null || v is not (Verdict.Tile or Verdict.Float)) continue;
            var node = new Node(NodeKind.Window) { Handle = hwnd };
            node.Tag = new ManagedWindow(snap, node);
            var frame = WindowQuery.FrameRect(hwnd);
            var output = tree.Outputs.FirstOrDefault(o => o.OutputRect.Contains(frame.CenterX, frame.CenterY)) ?? tree.Outputs[0];
            if (v == Verdict.Float) node.FloatRect = frame;
            tree.AddWindow(node, output.CurrentWorkspace!, v == Verdict.Float);
        }
        LayoutEngine.Compute(tree, o => new LayoutSettings
        {
            InnerGap = cfg.GapsInner * o.Dpi / 96,
            OuterTop = cfg.GapsTop * o.Dpi / 96,
            OuterBottom = cfg.GapsBottom * o.Dpi / 96,
            OuterLeft = cfg.GapsLeft * o.Dpi / 96,
            OuterRight = cfg.GapsRight * o.Dpi / 96,
            SmartGaps = cfg.SmartGaps,
        });

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteStartArray("config_errors");
            foreach (var e in cfg.Errors) w.WriteStringValue(e.ToString());
            w.WriteEndArray();
            w.WriteStartArray("windows");
            foreach (var (hwnd, verdict, desc) in report)
            {
                w.WriteStartObject();
                w.WriteString("verdict", verdict.ToString());
                w.WriteString("window", desc);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WritePropertyName("tree");
            TreeJson.WriteNode(w, tree.Root, tree);
            w.WriteEndObject();
        }
        var json = Encoding.UTF8.GetString(ms.ToArray());
        if (opts.DumpPath != null) File.WriteAllText(opts.DumpPath, json);
        else Log.Info("dry run:\n" + json);
        return 0;
    }
}

/// <summary>--test-window "title": a plain resizable window used by the automated tests.</summary>
internal sealed class TestWindow : Win32Window
{
    private readonly string _title;

    private TestWindow(string title)
    {
        _title = title;
        Create(C.WS_EX_APPWINDOW, C.WS_OVERLAPPEDWINDOW | C.WS_VISIBLE, title, new Rect(200, 200, 700, 600));
    }

    public static unsafe int Run(string title)
    {
        // Mozaic's own class: ignored by every normally running Mozaic, managed only by --manage-only test instances.
        _ = new TestWindow(title);
        MSG msg;
        while (User32.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }
        return 0;
    }

    protected override void OnPaint(Canvas c)
    {
        c.SetFont(Fonts.Get("Segoe UI", 14, (int)User32.GetDpiForWindow(Handle)));
        c.Fill(new Rect(0, 0, c.Width, c.Height), 0x2B2B2B);
        c.Text($"{_title}  {c.Width}x{c.Height}", new Rect(0, 0, c.Width, c.Height), 0xFFFFFF, C.DT_CENTER);
    }

    protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = 0;
        if (msg == C.WM_SIZE) { Invalidate(); return false; }
        if (msg == C.WM_DESTROY) { User32.PostQuitMessage(0); return true; }
        return false;
    }
}
