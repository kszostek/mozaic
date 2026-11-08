using System.Diagnostics;
using System.Globalization;
using Mozaic.Core;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic.UI;

internal enum LauncherMode { Apps, Windows, Commands }

/// <summary>dmenu/rofi-like popup: fuzzy-search Start-menu apps and PATH programs, or switch to any window.</summary>
internal sealed class Launcher : Win32Window
{
    private sealed record Item(string Name, string Detail, Action Run, string HistoryKey);

    private const int MaxRows = 10;
    private const uint WA_INACTIVE = 0;

    private static volatile List<(string Name, string Path)>? s_apps;
    private static int s_indexing;
    private static long s_indexedAt;
    private static Dictionary<string, int>? s_history;

    private readonly WindowManager _wm;
    private LauncherMode _mode;
    private string _query = "";
    private List<Item> _all = new();
    private List<Item> _shown = new();
    private int _sel;
    private int _dpi = 96;
    private bool _visible;

    public Launcher(WindowManager wm)
    {
        _wm = wm;
        Create(C.WS_EX_TOOLWINDOW | C.WS_EX_TOPMOST, C.WS_POPUP, "Mozaic launcher", new Rect(0, 0, 1, 1));
        WindowOps.SetCornerPreference(Handle, "round");
    }

    private static string HistoryPath => Path.Combine(WindowManager.Instance.Options.DataDir, "launcher-history.txt");

    public void Show(LauncherMode mode, Node? output)
    {
        _mode = mode;
        _query = "";
        _sel = 0;
        var area = output?.LayoutArea ?? new Rect(0, 0, 1280, 720);
        _dpi = output?.Dpi ?? 96;
        if (mode == LauncherMode.Apps && (s_apps == null || Environment.TickCount64 - s_indexedAt > 600_000)) StartIndexing();
        LoadHistory();
        BuildItems();
        Filter();
        int w = Math.Min(S(680), area.Width * 9 / 10);
        int h = RowHeight * (MaxRows + 1) + S(16);
        int x = area.CenterX - w / 2, y = area.Top + Math.Max(S(40), area.Height / 5);
        User32.SetWindowPos(Handle, C.HWND_TOPMOST, x, y, w, h, C.SWP_SHOWWINDOW);
        _visible = true;
        WindowOps.Focus(Handle);
        Invalidate();
    }

    public void Close()
    {
        if (!_visible) return;
        _visible = false;
        User32.ShowWindow(Handle, C.SW_HIDE);
        _wm.RequestFocusSync(false);
    }

    private int S(int v) => (int)Math.Round(v * _dpi / 96.0);
    private int RowHeight => Fonts.LineHeight(_wm.Config.Font.Size + 1, _dpi) + S(10);

    // ---------------------------------------------------------------------------------

    private void BuildItems()
    {
        _all = new List<Item>();
        if (_mode == LauncherMode.Windows)
        {
            foreach (var mw in _wm.Windows.Values)
            {
                var n = mw.Node;
                var ws = Tree.WorkspaceOf(n)?.Name ?? (n.IsScratchpad ? "scratchpad" : "?");
                var target = mw;
                _all.Add(new Item(n.Title.Length > 0 ? n.Title : mw.ProcessName, $"{ws}  ·  {Path.GetFileNameWithoutExtension(mw.ProcessName)}",
                    () => _wm.RunCommand($"[id={(long)target.Hwnd}] focus", fromUser: true), "win:" + mw.ProcessName));
            }
            foreach (var mw in _wm.DetachedWindows)
            {
                var target = mw;
                _all.Add(new Item(mw.Node.Title, $"{L.T("minimized")}  ·  {Path.GetFileNameWithoutExtension(mw.ProcessName)}",
                    () => _wm.RestoreDetached(target), "win:" + mw.ProcessName));
            }
            return;
        }
        if (_mode == LauncherMode.Commands)
        {
            // Every keybinding (a searchable cheat sheet) plus useful commands that have no key.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in _wm.Config.Bindings)
            {
                var cmd = b.Command;
                string keys = b.Combo.ToString() + (b.Mode != "default" ? $"  ({b.Mode})" : "");
                if (b.Mode == "default") seen.Add(cmd);
                _all.Add(new Item(cmd, keys, () => _wm.RunCommand(cmd, fromUser: true), "cmd:" + cmd));
            }
            foreach (var cmd in ExtraCommands)
            {
                if (seen.Contains(cmd)) continue;
                var c = cmd;
                _all.Add(new Item(c, L.T("command"), () => _wm.RunCommand(c, fromUser: true), "cmd:" + c));
            }
            return;
        }
        var apps = s_apps;
        if (apps == null) return;
        foreach (var (name, path) in apps)
        {
            var p = path;
            _all.Add(new Item(name, Describe(p), () => Launch(p), name));
        }
    }

    private static readonly string[] ExtraCommands =
    {
        "settings", "reload", "restart", "balance", "retile", "pause", "focus last", "layout toggle all", "layout splith",
        "layout splitv", "layout stacking", "layout tabbed", "floating toggle", "sticky toggle", "fullscreen toggle",
        "move position center", "move workspace to output next", "focus output next", "workspace back_and_forth",
        "gaps inner current plus 4", "gaps inner current minus 4", "gaps inner current set 0", "gaps outer current toggle 12",
        "bar mode toggle", "scratchpad show", "move scratchpad", "kill", "launcher windows",
    };

    private static readonly string[] StartMenuRoots =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
    };

    /// <summary>Short, readable origin of a launcher entry.</summary>
    private static string Describe(string path)
    {
        if (path.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)) return L.T("app");
        if (path.EndsWith(':')) return L.T("settings");
        foreach (var root in StartMenuRoots)
        {
            if (root.Length == 0 || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetDirectoryName(path)![root.Length..].Trim('\\');
            if (rel.StartsWith("Programs", StringComparison.OrdinalIgnoreCase)) rel = rel[8..].Trim('\\');
            return rel.Length == 0 ? L.T("start menu") : L.T("start menu") + " › " + rel.Replace("\\", " › ");
        }
        return Path.GetDirectoryName(path) ?? path;
    }

    private const string AppsFolderPrefix = "shell:AppsFolder\\";

    private void Filter()
    {
        var q = _query.Trim();
        var hist = s_history ?? new Dictionary<string, int>();
        _shown = _all
            .Select(i => (Item: i, Score: Score(i.Name, q)))
            .Where(x => x.Score >= 0)
            .OrderByDescending(x => x.Score + (hist.TryGetValue(x.Item.HistoryKey, out int c) ? Math.Min(200, 40 * Math.Log2(c + 1)) : 0))
            .ThenBy(x => x.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRows)
            .Select(x => x.Item)
            .ToList();
        if (_mode != LauncherMode.Windows && q.Length > 0 && !_shown.Any(i => i.Name.Equals(q, StringComparison.OrdinalIgnoreCase)))
        {
            if (_shown.Count == MaxRows) _shown.RemoveAt(MaxRows - 1);
            _shown.Add(_mode == LauncherMode.Commands
                ? new Item(L.T("Run command: ") + q, L.T("command"), () => _wm.RunCommand(q, fromUser: true), "cmd:" + q)
                : new Item(L.T("Run: ") + q, L.T("run as a command"), () => _wm.RunCommand("exec " + q, fromUser: true), "run:" + q));
        }
        _sel = Math.Clamp(_sel, 0, Math.Max(0, _shown.Count - 1));
    }

    private static int Score(string text, string q)
    {
        if (q.Length == 0) return 1;
        int idx = text.IndexOf(q, StringComparison.OrdinalIgnoreCase);
        if (idx == 0) return 1000 - Math.Min(text.Length, 200);
        if (idx > 0) return (text[idx - 1] is ' ' or '-' or '_' or '.' or '(' ? 800 : 600) - Math.Min(idx, 200);
        int ti = 0, gaps = 0;
        foreach (char ch in q)
        {
            if (char.IsWhiteSpace(ch)) continue;
            int pos = -1;
            for (int i = ti; i < text.Length; i++)
            {
                if (char.ToLowerInvariant(text[i]) == char.ToLowerInvariant(ch)) { pos = i; break; }
            }
            if (pos < 0) return -1;
            gaps += pos - ti;
            ti = pos + 1;
        }
        return Math.Max(0, 300 - gaps);
    }

    private void Activate(int index)
    {
        if (index < 0 || index >= _shown.Count) return;
        var item = _shown[index];
        Close();
        RecordHistory(item.HistoryKey);
        try { item.Run(); }
        catch (Exception e) { Log.Error("launcher", e); }
    }

    private void Launch(string path)
    {
        try
        {
            var psi = path.StartsWith(AppsFolderPrefix, StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true }
                : path.Contains("://", StringComparison.Ordinal) || path.EndsWith(':')
                    ? new ProcessStartInfo(path) { UseShellExecute = true }
                    : new ProcessStartInfo(path) { UseShellExecute = true, WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
            using var p = Process.Start(psi);
            if (p != null)
            {
                try { _wm.RememberExec((uint)p.Id); }
                catch (InvalidOperationException) { }
            }
        }
        catch (Exception e)
        {
            _wm.Notify("Could not start " + Path.GetFileName(path), e.Message);
        }
    }

    // ---------------------------------------------------------------------------------

    private static string CachePath => Path.Combine(WindowManager.Instance.Options.DataDir, "launcher-apps.txt");

    private static void StartIndexing()
    {
        if (Interlocked.Exchange(ref s_indexing, 1) == 1) return;
        // Show the cached list immediately; refresh it in the background.
        if (s_apps == null) s_apps = LoadCache();
        var t = new Thread(() =>
        {
            try
            {
                var apps = IndexApps();
                s_indexedAt = Environment.TickCount64;
                s_apps = apps;
                SaveCache(apps);
                Dispatcher.Post(() => WindowManager.Instance.OnLauncherIndexed());
            }
            catch (Exception e)
            {
                Log.Error("indexing apps", e);
            }
            finally
            {
                Interlocked.Exchange(ref s_indexing, 0);
            }
        }) { IsBackground = true, Name = "launcher index", Priority = ThreadPriority.BelowNormal };
        t.Start();
    }

    private static List<(string Name, string Path)>? LoadCache()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            var list = new List<(string, string)>();
            foreach (var line in File.ReadAllLines(CachePath))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0) list.Add((line[..tab], line[(tab + 1)..]));
            }
            return list.Count > 0 ? list : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void SaveCache(List<(string Name, string Path)> apps)
    {
        try { File.WriteAllLines(CachePath, apps.Select(a => a.Name + "\t" + a.Path)); }
        catch (IOException) { }
    }

    /// <summary>Everything the Start menu lists, including Store/UWP apps (via Get-StartApps).</summary>
    private static List<(string Name, string AppId)> StartApps()
    {
        var list = new List<(string, string)>();
        try
        {
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                "Get-StartApps | ForEach-Object { $_.Name + [char]9 + $_.AppID }\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            };
            using var p = Process.Start(psi);
            if (p == null) return list;
            string? line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && tab < line.Length - 1) list.Add((line[..tab].Trim(), line[(tab + 1)..].Trim()));
            }
            p.WaitForExit(10000);
        }
        catch (Exception e)
        {
            Log.Debug("Get-StartApps failed: " + e.Message);
        }
        return list;
    }

    private static List<(string, string)> IndexApps()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, appId) in StartApps())
        {
            if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
            map.TryAdd(name, AppsFolderPrefix + appId);
        }
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                 })
        {
            if (root.Length == 0 || !Directory.Exists(root)) continue;
            foreach (var f in Directory.EnumerateFiles(root, "*", opts))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is not (".lnk" or ".url" or ".appref-ms")) continue;
                var name = Path.GetFileNameWithoutExtension(f);
                if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                map.TryAdd(name, f);
            }
        }
        var pathDirs = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var dirRaw in pathDirs)
        {
            var dir = Environment.ExpandEnvironmentVariables(dirRaw);
            if (!Directory.Exists(dir)) continue;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*.exe", new EnumerationOptions { IgnoreInaccessible = true }))
                    map.TryAdd(Path.GetFileNameWithoutExtension(f), f);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        map.TryAdd("Settings", "ms-settings:");
        return map.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public void Refresh()
    {
        if (!_visible) return;
        BuildItems();
        Filter();
        Invalidate();
    }

    private static void LoadHistory()
    {
        if (s_history != null) return;
        s_history = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(HistoryPath)) return;
            foreach (var line in File.ReadAllLines(HistoryPath))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && int.TryParse(line.AsSpan(0, tab), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c))
                    s_history[line[(tab + 1)..]] = c;
            }
        }
        catch (IOException)
        {
        }
    }

    private static void RecordHistory(string key)
    {
        if (s_history == null) return;
        s_history[key] = s_history.GetValueOrDefault(key) + 1;
        try
        {
            File.WriteAllLines(HistoryPath, s_history.OrderByDescending(kv => kv.Value).Take(500)
                .Select(kv => kv.Value.ToString(CultureInfo.InvariantCulture) + "\t" + kv.Key));
        }
        catch (IOException)
        {
        }
    }

    // ---------------------------------------------------------------------------------

    protected override void OnPaint(Canvas c)
    {
        var cfg = _wm.Config;
        var font = Fonts.Get(cfg.Font.Family, cfg.Font.Size + 1, _dpi);
        var small = Fonts.Get(cfg.Font.Family, Math.Max(7, cfg.Font.Size - 1), _dpi);
        uint bg = 0x1E1E1E, fg = 0xF0F0F0, dim = 0x8A8A8A;
        c.Fill(new Rect(0, 0, c.Width, c.Height), bg);
        c.Frame(new Rect(0, 0, c.Width, c.Height), cfg.Focused.Border, 1);
        int pad = S(12);
        int rh = RowHeight;
        c.SetFont(font);
        var inputRect = new Rect(pad, S(6), c.Width - pad, S(6) + rh);
        string prompt = L.T(_mode switch { LauncherMode.Windows => "window › ", LauncherMode.Commands => "command › ", _ => "run › " });
        int pw = c.MeasureWidth(prompt);
        c.Text(prompt, inputRect, cfg.Focused.Indicator, C.DT_LEFT);
        string q = _query + "▏";
        c.Text(q, new Rect(inputRect.Left + pw, inputRect.Top, inputRect.Right, inputRect.Bottom), fg, C.DT_LEFT);
        c.Fill(new Rect(pad, inputRect.Bottom, c.Width - pad, inputRect.Bottom + 1), 0x3A3A3A);

        if (_shown.Count == 0)
        {
            string msg = L.T(_mode == LauncherMode.Apps && s_apps == null ? "indexing applications…" : "no matches");
            c.Text(msg, new Rect(pad, inputRect.Bottom + S(4), c.Width - pad, inputRect.Bottom + S(4) + rh), dim, C.DT_LEFT);
            return;
        }
        for (int i = 0; i < _shown.Count; i++)
        {
            var item = _shown[i];
            var r = new Rect(S(6), inputRect.Bottom + S(4) + i * rh, c.Width - S(6), inputRect.Bottom + S(4) + (i + 1) * rh);
            if (i == _sel) c.Fill(r, cfg.Focused.Background);
            c.SetFont(font);
            int nameWidth = Math.Min(c.MeasureWidth(item.Name), r.Width * 2 / 3);
            c.Text(item.Name, new Rect(r.Left + S(8), r.Top, r.Left + S(8) + nameWidth, r.Bottom), i == _sel ? cfg.Focused.Text : fg, C.DT_LEFT);
            c.SetFont(small);
            c.Text(item.Detail, new Rect(r.Left + S(20) + nameWidth, r.Top, r.Right - S(8), r.Bottom), i == _sel ? cfg.Focused.Text : dim, C.DT_RIGHT);
        }
    }

    protected override bool WndProc(uint msg, IntPtr wParam, IntPtr lParam, out IntPtr result)
    {
        result = 0;
        switch (msg)
        {
            case C.WM_ACTIVATE:
                if (((long)wParam & 0xFFFF) == WA_INACTIVE && _visible) Dispatcher.Post(Close);
                return false;
            case C.WM_KEYDOWN:
            case C.WM_SYSKEYDOWN:
                switch ((int)wParam)
                {
                    case C.VK_ESCAPE: Close(); return true;
                    case C.VK_RETURN: Activate(_sel); return true;
                    case C.VK_DOWN: case C.VK_TAB: _sel = _shown.Count == 0 ? 0 : (_sel + 1) % _shown.Count; Invalidate(); return true;
                    case C.VK_UP: _sel = _shown.Count == 0 ? 0 : (_sel - 1 + _shown.Count) % _shown.Count; Invalidate(); return true;
                }
                return false;
            case C.WM_CHAR:
            {
                char ch = (char)(long)wParam;
                if (ch == '\b')
                {
                    if (_query.Length > 0) _query = _query[..^1];
                }
                else if (ch == 0x7F || ch == 0x17)
                {
                    // Ctrl+Backspace / Ctrl+W: delete the last word
                    _query = _query.TrimEnd();
                    int sp = _query.LastIndexOf(' ');
                    _query = sp < 0 ? "" : _query[..(sp + 1)];
                }
                else if (ch >= 32)
                {
                    _query += ch;
                }
                else
                {
                    return true;
                }
                _sel = 0;
                Filter();
                Invalidate();
                return true;
            }
            case C.WM_LBUTTONDOWN:
            {
                int y = HiWord(lParam);
                int top = S(6) + RowHeight + S(4);
                int idx = (y - top) / Math.Max(1, RowHeight);
                if (y >= top) Activate(idx);
                return true;
            }
            case C.WM_DPICHANGED:
                return true;
        }
        return false;
    }
}
