using System.Diagnostics;
using Mozaic.Core;
using Mozaic.Core.Config;
using Mozaic.Ipc;
using Mozaic.Native;
using Mozaic.Platform;
using Mozaic.State;
using Mozaic.Status;
using Mozaic.UI;

namespace Mozaic;

/// <summary>
/// The window manager: keeps the i3 tree in sync with real windows. Everything here runs on the main thread.
/// Flow: OS events / commands mutate <see cref="Tree"/> → <see cref="RequestApply"/> → layout is computed and
/// the differences are pushed to the real windows (see WindowManager.Sync.cs).
/// </summary>
internal sealed partial class WindowManager
{
    private sealed record DetachedEntry(ManagedWindow Window, string? Workspace, bool Floating, bool Cloaked);

    private static readonly int[] CheckDelays = { 40, 120, 350, 900, 2500 };

    private readonly Dictionary<IntPtr, ManagedWindow> _windows = new();
    private readonly Dictionary<IntPtr, DetachedEntry> _detached = new();
    private readonly Dictionary<IntPtr, int> _pendingChecks = new();
    private readonly HashSet<IntPtr> _checkScheduled = new();
    private readonly Dictionary<uint, (string Workspace, long Ticks)> _execWorkspaces = new();

    private MainWindow _main = null!;
    private TrayIcon? _tray;
    private FocusSink _sink = null!;
    private FocusFrame _frame = null!;
    private DropPreview _preview = null!;
    private Osd _osd = null!;
    private TabBarPool _tabs = null!;
    private StatusService _status = null!;
    private IpcServer? _ipc;
    private CommandExecutor _exec = null!;
    private HiddenRegistry _hidden = null!;
    private StateStore _state = null!;
    private Launcher? _launcher;
    private nuint _displayTimer;
    private bool _stopping;

    public WindowManager(AppOptions options)
    {
        Options = options;
        Instance = this;
    }

    public static WindowManager Instance { get; private set; } = null!;

    public AppOptions Options { get; }
    public Tree Tree { get; private set; } = new();
    public MozaicConfig Config { get; private set; } = new();
    public string ConfigPath { get; private set; } = "";
    public string Mode { get; private set; } = "default";
    public bool Paused { get; private set; }
    public List<ConfigError> ConfigErrors { get; private set; } = new();
    public IReadOnlyDictionary<IntPtr, ManagedWindow> Windows => _windows;
    public StatusService Status => _status;
    public IntPtr MainHandle => _main.Handle;

    // =====================================================================================
    // Lifecycle
    // =====================================================================================

    public void Start()
    {
        _main = new MainWindow(this);
        Dispatcher.Init(_main.Handle);
        Directory.CreateDirectory(Options.DataDir);
        _hidden = new HiddenRegistry(Path.Combine(Options.DataDir, "hidden-windows.txt"));
        _state = new StateStore(Path.Combine(Options.DataDir, "state.json"));

        LoadConfigFile();
        Tree = new Tree(MakeTreeOptions());
        _sink = new FocusSink();
        _frame = new FocusFrame();
        _preview = new DropPreview();
        _osd = new Osd(this);
        CreateEffects();
        _tabs = new TabBarPool(this);
        _exec = new CommandExecutor(this);
        _status = new StatusService(this);

        RefreshOutputs();
        var hiddenBefore = _hidden.Load();
        if (Config.RememberLayout || Options.Restarted) RestoreState(hiddenBefore);
        RecoverHiddenWindows(hiddenBefore);
        ScanWindows();
        foreach (var o in Tree.Outputs) Tree.EnsureWorkspace(o, PreferredWorkspaceFor(o));
        InitialFocus();

        WinEvents.Handler = OnWinEvent;
        WinEvents.Start();
        InputHooks.OnCommand = RunBoundCommand;
        InputHooks.OnDragStart = OnDragStart;
        InputHooks.OnDragMove = OnDragMove;
        InputHooks.OnDragEnd = OnDragEnd;
        ApplyConfigToHooks();
        InputHooks.Start(keyboard: !Options.NoHotkeys, mouse: true);

        if (!Options.NoTray) _tray = new TrayIcon(this, _main);
        _ipc = new IpcServer(this, IpcProtocol.PipeName(Options.Instance));
        _ipc.Start();
        _status.Start(Config.Bar);

        RunExecs(initial: !Options.Restarted);
        ReportConfigErrors();
        UpdateFocusFollowsMouse();
        _focusDirty = true;
        Apply();
        Log.Info($"started: {_windows.Count} windows, {Tree.Outputs.Count} output(s), config {ConfigPath}");
        if (Config.Welcome && !Options.Restarted && Options.ManageOnly == null)
            Dispatcher.SetTimeout(600, OpenSettings);
    }

    private const int ExitTimeoutMs = 8000;
    private volatile string _exitStep = "";

    private void ExitStep(string step)
    {
        _exitStep = step;
        Log.Debug("exit: " + step);
    }

    /// <summary>
    /// Safety net: whatever blocks the main thread while exiting (a window or the shell that stops responding),
    /// the process must not stay behind. After <see cref="ExitTimeoutMs"/> the hidden windows are shown and the
    /// process ends; the log names the step that got stuck.
    /// </summary>
    private void StartExitWatchdog(bool restart)
    {
        var hidden = restart ? Array.Empty<IntPtr>()
            : _windows.Values.Concat(_detached.Values.Select(d => d.Window)).Where(w => w.HiddenByUs).Select(w => w.Hwnd).ToArray();
        var thread = new Thread(() =>
        {
            if (Program.Finished.Wait(ExitTimeoutMs)) return;
            Log.Error($"exit did not finish within {ExitTimeoutMs / 1000} s (stuck at: {_exitStep}); ending the process");
            foreach (var h in hidden)
                if (User32.IsWindow(h)) User32.ShowWindowAsync(h, C.SW_SHOWNA);
            _opacity.ForceOpaqueNow();
            Log.Close();
            Kernel32.TerminateProcess(Kernel32.GetCurrentProcess(), 3);
        }) { IsBackground = true, Name = "exit watchdog" };
        thread.Start();
    }

    /// <summary>Stops managing windows. With <paramref name="restart"/> a fresh instance takes over the layout.</summary>
    public void Exit(bool restart = false)
    {
        if (_stopping) return;
        _stopping = true;
        Log.Info(restart ? "restarting" : "exiting");
        StartExitWatchdog(restart);
        if (!restart)
        {
            // First what matters most: no window may stay hidden (asynchronous, cannot block).
            ExitStep("showing hidden windows");
            foreach (var mw in _windows.Values.Concat(_detached.Values.Select(d => d.Window)))
                if (mw.HiddenByUs && User32.IsWindow(mw.Hwnd)) User32.ShowWindowAsync(mw.Hwnd, C.SW_SHOWNA);
        }
        // Every window back to 100% at once (cannot block); the worker restores the styles later.
        ExitStep("making windows fully visible");
        _opacity.ForceOpaqueNow();
        ExitStep("saving the layout");
        try { SaveStateNow(); } catch (Exception e) { Log.Error("save state", e); }
        ExitStep("stopping the window events and hotkeys");
        WinEvents.Stop();
        InputHooks.Stop();
        ExitStep("stopping IPC and the status bar");
        _ipc?.Stop();
        _status.Stop();
        _launcher?.Close();
        if (Options.TestHangOnExit)
        {
            ExitStep("--test-hang-on-exit");
            Thread.Sleep(Timeout.Infinite);
        }
        ExitStep("restoring window borders and corners");
        if (restart)
        {
            ResetDecorations(showHidden: false);
            try
            {
                // Same command line as this instance (config, instance, filters...), plus --restarted.
                var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                foreach (var a in Options.RawArgs)
                    if (a is not ("--restarted" or "--replace")) psi.ArgumentList.Add(a);
                psi.ArgumentList.Add("--restarted");
                Process.Start(psi);
            }
            catch (Exception e)
            {
                Log.Error("restart failed, showing all windows", e);
                ResetDecorations(showHidden: true);
                _hidden.Delete();
            }
        }
        else
        {
            ResetDecorations(showHidden: true);
            _hidden.Delete();
        }
        ExitStep("making windows opaque again");
        DestroyEffects(1500);
        ExitStep("removing the tray icon");
        _tray?.Dispose();
        ExitStep("closing Mozaic's own windows");
        foreach (var b in _bars.Values) b.Destroy();
        _bars.Clear();
        _tabs.DestroyAll();
        _frame.Destroy();
        _preview.Destroy();
        _osd.Destroy();
        _sink.Destroy();
        _main.Unregister();
        ExitStep("leaving the message loop");
        User32.PostQuitMessage(0);
    }

    /// <summary>Shows hidden windows and restores the native look of every window.</summary>
    private void ResetDecorations(bool showHidden)
    {
        foreach (var mw in _windows.Values.Concat(_detached.Values.Select(d => d.Window)))
        {
            if (!User32.IsWindow(mw.Hwnd)) continue;
            if (showHidden && mw.HiddenByUs) User32.ShowWindowAsync(mw.Hwnd, C.SW_SHOWNA);
            // Changing the frame of a window that does not respond could wait for it; leave those alone.
            if (User32.IsHungAppWindow(mw.Hwnd)) continue;
            if (mw.AppliedBorderColor != null) WindowOps.SetBorderColor(mw, null);
            if (mw.CornerApplied) WindowOps.SetCornerPreference(mw.Hwnd, "default");
        }
    }

    /// <summary>Emergency path for unhandled exceptions: never leave windows hidden.</summary>
    public void EmergencyRestore()
    {
        try
        {
            foreach (var mw in _windows.Values)
                if (mw.HiddenByUs && User32.IsWindow(mw.Hwnd)) User32.ShowWindowAsync(mw.Hwnd, C.SW_SHOWNA);
            _hidden.Delete();
            if (_opacity != null)
            {
                foreach (var mw in _windows.Values) ResetOpacity(mw);
                _opacity.Shutdown(500);
            }
        }
        catch
        {
            // best effort
        }
    }

    public void OnEndSession()
    {
        // Let applications show their "save changes?" prompts.
        foreach (var mw in _windows.Values)
            if (mw.HiddenByUs) WindowOps.Show(mw);
        SaveStateNow();
        Dispatcher.SetTimeout(5000, RequestApply);
    }

    // =====================================================================================
    // Configuration
    // =====================================================================================

    private string ResolveConfigPath()
    {
        if (Options.ConfigPath != null) return Options.ConfigPath;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var primary = Path.Combine(home, ".config", "mozaic", "config");
        var alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozaic", "config");
        if (File.Exists(primary)) return primary;
        if (File.Exists(alt)) return alt;
        Directory.CreateDirectory(Path.GetDirectoryName(primary)!);
        File.WriteAllText(primary, DefaultConfig.Text.Replace("\n", Environment.NewLine));
        Log.Info("wrote default config to " + primary);
        return primary;
    }

    private void LoadConfigFile()
    {
        ConfigPath = ResolveConfigPath();
        MozaicConfig cfg;
        try
        {
            cfg = ConfigParser.Parse(File.ReadAllText(ConfigPath), ConfigPath, CharToVk);
        }
        catch (IOException e)
        {
            cfg = ConfigParser.Parse(DefaultConfig.Text, null, CharToVk);
            cfg.Errors.Insert(0, new ConfigError(ConfigPath, 0, "cannot read config (" + e.Message + "), using defaults"));
        }
        Config = cfg;
        ConfigErrors = cfg.Errors;
    }

    /// <summary>Resolves characters to virtual keys using the active keyboard layout (ignoring AltGr characters).</summary>
    private static int? CharToVk(char c)
    {
        var fg = User32.GetForegroundWindow();
        var layout = User32.GetKeyboardLayout(fg != 0 ? WindowQuery.ThreadId(fg) : 0);
        short r = User32.VkKeyScanExW(c, layout);
        if (r == -1) return null;
        int shiftState = (r >> 8) & 0xFF;
        if ((shiftState & 6) != 0) return null;
        return r & 0xFF;
    }

    private TreeOptions MakeTreeOptions() => new()
    {
        DefaultOrientation = Config.DefaultOrientation,
        WorkspaceLayout = Config.WorkspaceLayout,
        FocusWrapping = Config.FocusWrapping,
        FlattenContainers = Config.FlattenContainers,
        Autotiling = Config.Autotiling,
        WorkspaceOutputResolver = ResolveWorkspaceOutput,
    };

    private Node? ResolveWorkspaceOutput(string name)
    {
        if (!Config.WorkspaceOutputs.TryGetValue(name, out var outs))
        {
            int n = WorkspaceNames.ParseNumber(name);
            if (n < 0 || !Config.WorkspaceOutputs.TryGetValue(n.ToString(System.Globalization.CultureInfo.InvariantCulture), out outs))
                return null;
        }
        foreach (var o in outs)
        {
            var output = Tree.FindOutput(o);
            if (output != null) return output;
        }
        return null;
    }

    private string? PreferredWorkspaceFor(Node output)
    {
        foreach (var name in Config.WorkspaceOrder)
        {
            if (Tree.FindWorkspace(name) != null) continue;
            if (Config.WorkspaceOutputs.TryGetValue(name, out var outs) && outs.Any(x => Tree.FindOutput(x) == output)) return name;
        }
        foreach (var name in Config.WorkspaceOrder)
        {
            if (Tree.FindWorkspace(name) != null || Config.WorkspaceOutputs.ContainsKey(name)) continue;
            int n = WorkspaceNames.ParseNumber(name);
            if (n >= 0 && Tree.FindWorkspaceByNumber(n) != null) continue;
            return name;
        }
        return null;
    }

    public void Reload()
    {
        LoadConfigFile();
        Tree.Options = MakeTreeOptions();
        SetMode("default");
        ApplyConfigToHooks();
        RefreshOutputs();
        foreach (var mw in _windows.Values)
        {
            ApplyCorner(mw);
            mw.LastTarget = null;
        }
        _status.Start(Config.Bar);
        RunExecs(initial: false);
        ReportConfigErrors();
        UpdateFocusFollowsMouse();
        RequestApply();
        Log.Info("config reloaded");
    }

    private void ApplyConfigToHooks()
    {
        InputHooks.SetBindings(Config.Bindings);
        InputHooks.SetMode(Mode);
        InputHooks.SetDragModifier(Config.FloatingModifier);
        InputHooks.WatchModifier(Config.GroupLines == "on" ? ModKey : Modifiers.None);
    }

    private void ReportConfigErrors()
    {
        foreach (var e in ConfigErrors) Log.Warn("config: " + e);
        if (ConfigErrors.Count == 0) return;
        var first = ConfigErrors[0].ToString();
        _tray?.ShowBalloon(L.T("Mozaic config"), ConfigErrors.Count == 1 ? first : L.F("{0} errors. First: {1}", ConfigErrors.Count, first), warning: true);
    }

    private void RunExecs(bool initial)
    {
        if (initial)
            foreach (var cmd in Config.Exec) _exec.Exec(cmd);
        foreach (var cmd in Config.ExecAlways) _exec.Exec(cmd);
    }

    public void RememberExec(uint pid)
    {
        var ws = Tree.FocusedWorkspace;
        if (ws == null || pid == 0) return;
        _execWorkspaces[pid] = (ws.Name, Environment.TickCount64);
    }

    public void SetMode(string mode)
    {
        if (!Config.Modes.Contains(mode)) mode = "default";
        Mode = mode;
        InputHooks.SetMode(mode);
        InvalidateBars();
        _ipc?.Broadcast("mode", mode);
        if (_osd == null) return;
        if (mode != "default" && Config.Osd && Tree.FocusedOutput is { } o) _osd.Show(mode, o, sticky: true);
        else _osd.Hide();
    }

    private void ShowOsd(string text)
    {
        if (Config.Osd && Tree.FocusedOutput is { } o && Mode == "default") _osd.Show(text, o);
    }

    public void TogglePause()
    {
        Paused = !Paused;
        InputHooks.KeyboardEnabled = !Paused;
        if (Paused)
        {
            foreach (var mw in _windows.Values)
            {
                if (mw.HiddenByUs) WindowOps.Show(mw);
                WindowOps.SetBorderColor(mw, null);
                ResetOpacity(mw);
            }
            _frame.Hide();
            _tabs.HideAll();
            _lines.HideAll();
            _cheatsheet.Hide();
        }
        else
        {
            foreach (var mw in _windows.Values) mw.LastTarget = null;
            ScanWindows();
        }
        _tray?.UpdateTip();
        Log.Info(Paused ? "paused" : "resumed");
        ShowOsd(L.T(Paused ? "tiling paused" : "tiling resumed"));
        RequestApply();
    }

    // =====================================================================================
    // Outputs
    // =====================================================================================

    public void OnDisplayChanged(string reason)
    {
        Dispatcher.ClearTimer(ref _displayTimer);
        _displayTimer = Dispatcher.SetTimeout(250, () =>
        {
            _displayTimer = 0;
            Log.Info("refreshing outputs: " + reason);
            RefreshOutputs();
            RequestApply();
        });
    }

    private void RefreshOutputs()
    {
        var mons = Monitors.Enumerate();
        if (mons.Count == 0) return;
        foreach (var m in mons)
        {
            var o = Tree.Outputs.FirstOrDefault(x => x.Name == m.Name);
            if (o == null)
            {
                Tree.AddOutput(m.Name, m.Bounds, m.WorkArea, m.Primary, m.Dpi);
            }
            else
            {
                o.OutputRect = m.Bounds;
                o.WorkArea = m.WorkArea;
                o.Primary = m.Primary;
                o.Dpi = m.Dpi;
            }
        }
        foreach (var o in Tree.Outputs.ToList())
        {
            if (mons.Any(m => m.Name == o.Name) || Tree.Outputs.Count <= 1) continue;
            Log.Info("output removed: " + o.Name);
            Tree.RemoveOutput(o);
        }
        Tree.SortOutputs();
        foreach (var o in Tree.Outputs)
        {
            o.LayoutArea = ComputeLayoutArea(o);
            Tree.EnsureWorkspace(o, PreferredWorkspaceFor(o));
        }
        SyncBars();
        foreach (var mw in _windows.Values) mw.LastTarget = null;
    }

    private Rect ComputeLayoutArea(Node output)
    {
        int h = BarHeightFor(output);
        var w = output.WorkArea;
        if (h <= 0) return w;
        return Config.Bar!.Position == "bottom" ? new Rect(w.Left, w.Top, w.Right, w.Bottom - h) : new Rect(w.Left, w.Top + h, w.Right, w.Bottom);
    }

    public int BarHeightFor(Node output)
    {
        var bar = Config.Bar;
        if (bar == null || bar.Mode == "invisible") return 0;
        if (bar.Outputs.Count > 0 && !bar.Outputs.Any(x => x == "*" || Tree.FindOutput(x) == output)) return 0;
        var font = bar.Font ?? Config.Font;
        int logical = bar.Height > 0 ? bar.Height : Fonts.LineHeight(font.Size, 96) + 8;
        return (int)Math.Round(logical * output.Dpi / 96.0);
    }

    public Node? OutputAt(int x, int y) =>
        Tree.Outputs.FirstOrDefault(o => o.OutputRect.Contains(x, y)) ??
        Tree.Outputs.OrderBy(o => Math.Abs(o.OutputRect.CenterX - x) + Math.Abs(o.OutputRect.CenterY - y)).FirstOrDefault();

    // =====================================================================================
    // Window tracking
    // =====================================================================================

    private void ScanWindows()
    {
        var found = new List<(WindowSnapshot Snap, bool Floating, Rect Frame)>();
        foreach (var hwnd in WindowQuery.TopLevelWindows())
        {
            if (_windows.ContainsKey(hwnd) || _detached.ContainsKey(hwnd)) continue;
            var v = WindowRules.Classify(hwnd, Config, out var snap);
            if (v is Verdict.Tile or Verdict.Float)
                found.Add((snap!, v == Verdict.Float, WindowQuery.FrameRect(hwnd)));
        }
        // The z-order (top first) is the best guess of how recently each window was used.
        var zIndex = new Dictionary<IntPtr, int>();
        for (int i = 0; i < found.Count; i++) zIndex[found[i].Snap.Hwnd] = i;
        // Keep the existing left-to-right arrangement as far as possible.
        foreach (var (snap, floating, _) in found.OrderBy(f => f.Frame.Left).ThenBy(f => f.Frame.Top))
            Manage(snap, floating, startup: true).FocusSeq = -zIndex[snap.Hwnd];
    }

    private void InitialFocus()
    {
        var fg = User32.GetForegroundWindow();
        if (_windows.TryGetValue(fg, out var mw) && mw.Node.Parent != null)
        {
            Tree.Focus(mw.Node);
            return;
        }
        User32.GetCursorPos(out var pt);
        var output = OutputAt(pt.X, pt.Y) ?? Tree.Outputs[0];
        Tree.Focus(Tree.DescendFocused(Tree.EnsureWorkspace(output)));
    }

    private ManagedWindow Manage(WindowSnapshot snap, bool floating, bool startup)
    {
        var node = new Node(NodeKind.Window) { Handle = snap.Hwnd };
        var mw = new ManagedWindow(snap, node);
        node.Tag = mw;
        _windows[snap.Hwnd] = mw;
        InputHooks.ManagedHandles[snap.Hwnd] = 0;
        _pendingChecks.Remove(snap.Hwnd);
        WindowOps.UpdateFrameInsets(mw);
        if (User32.IsZoomed(snap.Hwnd))
        {
            // Tiling needs restored windows; maximize again with "fullscreen" if you want it big.
            WindowOps.Restore(snap.Hwnd);
        }

        var frame = WindowQuery.FrameRect(snap.Hwnd);
        var ws = TargetWorkspace(mw, frame, startup);
        if (floating) node.FloatRect = FitFloating(frame, ws);
        Tree.AddWindow(node, ws, floating);
        if (floating && node.FloatRect == frame) mw.LastTarget = frame;

        ApplyCorner(mw);
        RunForWindowRules(mw);

        bool noFocus = Config.NoFocus.Any(c => c.Matches(mw.Facts(false)));
        if (!startup && !noFocus && node.Parent != null && Tree.WorkspaceOf(node) == Tree.FocusedWorkspace)
        {
            Tree.Focus(node);
            _focusDirty = User32.GetForegroundWindow() != snap.Hwnd;
        }
        Log.Debug($"manage {mw}{(floating ? " floating" : "")} → ws {Tree.WorkspaceOf(node)?.Name}");
        _ipc?.Broadcast("window", "new");
        RequestApply();
        return mw;
    }

    private Rect FitFloating(Rect frame, Node ws)
    {
        var area = Tree.OutputOf(ws)?.LayoutArea ?? frame;
        if (frame.IsEmpty) return Tree.DefaultFloatRect(ws);
        return frame.IntersectionArea(area) * 2 >= frame.Width * frame.Height ? frame.ClampInto(area) : frame.CenteredIn(area);
    }

    private Node TargetWorkspace(ManagedWindow mw, Rect frame, bool startup)
    {
        var facts = mw.Facts(false);
        foreach (var a in Config.Assigns)
        {
            if (!a.Criteria.Matches(facts)) continue;
            Node? ws = null;
            if (a.ToOutput)
            {
                var o = Tree.FindOutput(a.Target);
                if (o != null) ws = Tree.EnsureWorkspace(o);
            }
            else if (a.ByNumber && int.TryParse(a.Target, out int num))
            {
                ws = Tree.FindWorkspaceByNumber(num) ?? Tree.GetOrCreateWorkspace(a.Target);
            }
            else
            {
                ws = Tree.GetOrCreateWorkspace(a.Target);
            }
            if (ws != null) return ws;
        }
        var owner = WindowQuery.Owner(mw.Hwnd);
        if (owner != 0 && _windows.TryGetValue(owner, out var ownerMw) && Tree.WorkspaceOf(ownerMw.Node) is { } ownerWs)
            return ownerWs;
        if (startup)
        {
            var o = OutputAt(frame.CenterX, frame.CenterY) ?? Tree.Outputs[0];
            return Tree.EnsureWorkspace(o);
        }
        if (_execWorkspaces.TryGetValue(mw.Pid, out var e) && Environment.TickCount64 - e.Ticks < 60_000 &&
            Tree.FindWorkspace(e.Workspace) is { } execWs)
            return execWs;
        return Tree.FocusedWorkspace ?? Tree.EnsureWorkspace(Tree.Outputs[0]);
    }

    private void RunForWindowRules(ManagedWindow mw)
    {
        if (Config.ForWindow.Count == 0) return;
        var facts = mw.Facts(false);
        foreach (var rule in Config.ForWindow)
        {
            if (!rule.Criteria.Matches(facts)) continue;
            if (mw.Node.Parent == null && !Tree.Scratchpad.Contains(mw.Node)) break;
            _exec.Execute(rule.Command, mw.Node, fromUser: false);
        }
    }

    private void ApplyCorner(ManagedWindow mw)
    {
        if (Config.CornerStyle == "default" && !mw.CornerApplied) return;
        WindowOps.SetCornerPreference(mw.Hwnd, Config.CornerStyle);
        mw.CornerApplied = Config.CornerStyle != "default";
    }

    private void TryManage(IntPtr hwnd)
    {
        if (Paused || _stopping || _windows.ContainsKey(hwnd) || _detached.ContainsKey(hwnd)) return;
        var v = WindowRules.Classify(hwnd, Config, out var snap);
        switch (v)
        {
            case Verdict.Tile: Manage(snap!, false, false); break;
            case Verdict.Float: Manage(snap!, true, false); break;
            case Verdict.NotYet: ScheduleCheck(hwnd); break;
            default: _pendingChecks.Remove(hwnd); break;
        }
    }

    private void ScheduleCheck(IntPtr hwnd)
    {
        if (_checkScheduled.Contains(hwnd)) return;
        int attempt = _pendingChecks.GetValueOrDefault(hwnd, 0);
        if (attempt >= CheckDelays.Length)
        {
            _pendingChecks.Remove(hwnd);
            return;
        }
        _pendingChecks[hwnd] = attempt + 1;
        _checkScheduled.Add(hwnd);
        Dispatcher.SetTimeout(CheckDelays[attempt], () =>
        {
            _checkScheduled.Remove(hwnd);
            if (_pendingChecks.ContainsKey(hwnd)) TryManage(hwnd);
        });
    }

    private void Unmanage(ManagedWindow mw, string reason)
    {
        if (!_windows.Remove(mw.Hwnd)) return;
        InputHooks.ManagedHandles.TryRemove(mw.Hwnd, out _);
        Dispatcher.ClearTimer(ref mw.EnforceTimer);
        IconCache.Forget(mw.Hwnd);
        if (User32.IsWindow(mw.Hwnd))
        {
            if (mw.AppliedBorderColor != null) WindowOps.SetBorderColor(mw, null);
            if (mw.CornerApplied) WindowOps.SetCornerPreference(mw.Hwnd, "default");
        }
        ResetOpacity(mw);
        bool wasFocused = Tree.FocusedWindow == mw.Node;
        Tree.Remove(mw.Node);
        if (wasFocused) _focusDirty = true;
        if (_drag?.Window == mw) CancelDrag();
        Log.Debug($"unmanage {mw}: {reason}");
        _ipc?.Broadcast("window", "close");
        RequestApply();
    }

    /// <summary>Takes a minimized/cloaked window out of the layout, remembering where it was.</summary>
    private void Detach(ManagedWindow mw, bool cloaked)
    {
        var n = mw.Node;
        if (n.Parent == null) return;
        bool wasFocused = Tree.FocusedWindow == n;
        var ws = Tree.WorkspaceOf(n);
        bool floating = n.InFloating;
        n.Fullscreen = false;
        Tree.Remove(n);
        ResetOpacity(mw);
        _windows.Remove(mw.Hwnd);
        InputHooks.ManagedHandles.TryRemove(mw.Hwnd, out _);
        _detached[mw.Hwnd] = new DetachedEntry(mw, ws?.Name, floating, cloaked);
        if (wasFocused && !cloaked) _focusDirty = true;
        Log.Debug($"detach {mw} ({(cloaked ? "cloaked" : "minimized")})");
        RequestApply();
    }

    private void Reattach(DetachedEntry e)
    {
        var mw = e.Window;
        _detached.Remove(mw.Hwnd);
        if (Paused) return;
        _windows[mw.Hwnd] = mw;
        InputHooks.ManagedHandles[mw.Hwnd] = 0;
        Node ws = e.Workspace != null && Tree.FindWorkspace(e.Workspace) is { } w && w.Parent?.CurrentWorkspace == w
            ? w
            : Tree.FocusedWorkspace ?? Tree.EnsureWorkspace(Tree.Outputs[0]);
        Tree.AddWindow(mw.Node, ws, e.Floating);
        mw.LastTarget = null;
        mw.NativeMaximized = false;
        mw.NativeFullscreen = false;
        if (User32.GetForegroundWindow() == mw.Hwnd || ws == Tree.FocusedWorkspace) Tree.Focus(mw.Node);
        Log.Debug($"reattach {mw}");
        RequestApply();
    }

    public void OnWindowFlash(IntPtr hwnd)
    {
        if (!_windows.TryGetValue(hwnd, out var mw) || Tree.FocusedWindow == mw.Node) return;
        mw.Node.Urgent = true;
        RequestApply();
    }

    public IEnumerable<ManagedWindow> DetachedWindows => _detached.Values.Select(d => d.Window);

    public void RestoreDetached(ManagedWindow mw) => WindowOps.Focus(mw.Hwnd);

    // =====================================================================================
    // OS events
    // =====================================================================================

    private void OnWinEvent(uint ev, IntPtr hwnd)
    {
        if (_stopping) return;
        _windows.TryGetValue(hwnd, out var mw);
        switch (ev)
        {
            case C.EVENT_OBJECT_SHOW:
                if (mw == null) { TryManage(hwnd); break; }
                if (mw.PendingShows > 0) { mw.PendingShows--; break; }
                if (mw.HiddenByUs && !Paused)
                {
                    // The application showed a window we keep hidden (e.g. on another workspace).
                    WindowOps.Hide(mw);
                    if (Config.FocusOnWindowActivation is "smart" or "urgent") mw.Node.Urgent = true;
                    RequestApply();
                }
                break;

            case C.EVENT_OBJECT_UNCLOAKED:
                if (mw != null) break;
                if (_detached.TryGetValue(hwnd, out var de) && de.Cloaked) Reattach(de);
                else TryManage(hwnd);
                break;

            case C.EVENT_OBJECT_HIDE:
                if (mw == null) break;
                if (mw.PendingHides > 0) { mw.PendingHides--; break; }
                if (mw.HiddenByUs || User32.IsWindowVisible(hwnd)) break;
                if (User32.IsHungAppWindow(hwnd)) break; // replaced by a "ghost" window while hung
                Unmanage(mw, "hidden by application");
                break;

            case C.EVENT_OBJECT_CLOAKED:
                if (mw != null && !mw.HiddenByUs && mw.Node.Parent != null) Detach(mw, cloaked: true);
                break;

            case C.EVENT_OBJECT_DESTROY:
                if (mw != null) Unmanage(mw, "destroyed");
                _detached.Remove(hwnd);
                _pendingChecks.Remove(hwnd);
                break;

            case C.EVENT_SYSTEM_MINIMIZESTART:
                if (mw != null && mw.Node.Parent != null) Detach(mw, cloaked: false);
                break;

            case C.EVENT_SYSTEM_MINIMIZEEND:
                if (_detached.TryGetValue(hwnd, out var me)) Reattach(me);
                else if (mw == null) TryManage(hwnd);
                break;

            case C.EVENT_SYSTEM_FOREGROUND:
                OnForeground(hwnd);
                break;

            case C.EVENT_SYSTEM_MOVESIZESTART:
                if (mw != null) OnMoveSizeStart(mw);
                break;

            case C.EVENT_SYSTEM_MOVESIZEEND:
                if (mw != null) OnMoveSizeEnd(mw);
                break;

            case C.EVENT_OBJECT_LOCATIONCHANGE:
                if (mw != null) OnLocationChange(mw);
                break;

            case C.EVENT_OBJECT_NAMECHANGE:
                if (mw != null)
                {
                    var title = WindowQuery.Title(hwnd);
                    if (title != mw.Node.Title)
                    {
                        mw.Node.Title = title;
                        if (mw.ProcessName == "ApplicationFrameHost.exe")
                            mw.ProcessName = WindowQuery.EffectiveProcessName(hwnd, mw.ClassName);
                        InvalidateDecorations();
                        _ipc?.Broadcast("window", "title");
                    }
                }
                else if (_pendingChecks.ContainsKey(hwnd))
                {
                    TryManage(hwnd);
                }
                break;
        }
    }

    private IntPtr _expectedFg;
    private long _expectedFgAt;
    private nuint _reconcileTimer;
    private long _snapAssistGuardUntil;

    private void OnForeground(IntPtr hwnd)
    {
        if (Paused) return;
        // Right after we focused a window ourselves, stale events for windows focused in between are ignored;
        // a delayed reconcile picks up whatever really is in the foreground.
        if (_expectedFg != 0 && hwnd != _expectedFg && Environment.TickCount64 - _expectedFgAt < 250)
        {
            Dispatcher.ClearTimer(ref _reconcileTimer);
            _reconcileTimer = Dispatcher.SetTimeout(300, () =>
            {
                _reconcileTimer = 0;
                _expectedFg = 0;
                OnForeground(User32.GetForegroundWindow());
            });
            return;
        }

        if (!_windows.TryGetValue(hwnd, out var mw))
        {
            // Dropping a dragged window near a screen edge makes Windows snap it and open Snap Assist.
            // Mozaic already re-tiled the window, so close Snap Assist by taking the focus back.
            if (Environment.TickCount64 < _snapAssistGuardUntil && WindowQuery.ClassName(hwnd) == "XamlExplorerHostIslandWindow")
            {
                Log.Debug("dismissing Snap Assist after a tiled drag");
                _snapAssistGuardUntil = 0;
                User32.PostMessageW(hwnd, C.WM_KEYDOWN, C.VK_ESCAPE, 0);
                RequestFocusSync(false);
                return;
            }
            if (!_detached.ContainsKey(hwnd)) TryManage(hwnd);
            if (!_windows.TryGetValue(hwnd, out mw)) return;
        }
        var node = mw.Node;
        if (node.Parent == null) return;
        var ws = Tree.WorkspaceOf(node)!;
        if (ws.Parent?.CurrentWorkspace != ws)
        {
            // An application activated a window on a hidden workspace.
            switch (Config.FocusOnWindowActivation)
            {
                case "focus":
                    Tree.Focus(node);
                    break;
                case "none":
                    _focusDirty = true;
                    break;
                default:
                    node.Urgent = true;
                    _focusDirty = true;
                    break;
            }
            RequestApply();
            return;
        }
        if (Tree.Focused == node) return;
        if (Tree.Focused != null && Tree.Focused.Kind == NodeKind.Container && Tree.IsAncestor(Tree.Focused, node)) return;
        // Focusing another window of the same workspace ends fullscreen mode (like leaving it in i3).
        foreach (var w in Tree.WindowsIn(ws))
            if (w.Fullscreen && w != node && !node.InFloating) w.Fullscreen = false;
        Tree.Focus(node);
        _ipc?.Broadcast("window", "focus");
        RequestApply();
    }

    // =====================================================================================
    // Commands
    // =====================================================================================

    /// <summary>Runs an i3 command string. <paramref name="fromUser"/> enables mouse warping.</summary>
    public CommandOutcome RunCommand(string text, bool fromUser)
    {
        var wsBefore = Tree.FocusedWorkspace;
        var outcome = _exec.Execute(text, null, fromUser);
        foreach (var err in outcome.Errors) Log.Warn($"command '{text}': {err}");
        if (fromUser && !_stopping && Tree.FocusedWorkspace is { } ws && ws != wsBefore) ShowOsd(ws.Name);
        return outcome;
    }

    /// <summary>Opens MozaicSettings.exe (next to Mozaic.exe, or the development build).</summary>
    public void OpenSettings()
    {
        var path = FindSettingsExe();
        if (path == null)
        {
            Notify("Mozaic", L.T("The settings app (MozaicSettings.exe) was not found next to Mozaic.exe."));
            return;
        }
        try
        {
            var psi = new ProcessStartInfo(path) { UseShellExecute = false };
            psi.ArgumentList.Add("--config");
            psi.ArgumentList.Add(ConfigPath);
            if (Options.Instance.Length > 0)
            {
                psi.ArgumentList.Add("--instance");
                psi.ArgumentList.Add(Options.Instance);
            }
            Process.Start(psi);
        }
        catch (Exception e)
        {
            Log.Error("open settings", e);
        }
    }

    private static string? FindSettingsExe()
    {
        var dir = AppContext.BaseDirectory;
        var next = Path.Combine(dir, "MozaicSettings.exe");
        if (File.Exists(next)) return next;
        // Development layout: src\Mozaic\bin\<cfg>\<tfm>\ → src\MozaicSettings\bin\<cfg>\<tfm>\
        var d = new DirectoryInfo(dir);
        for (int i = 0; i < 6 && d != null; i++, d = d.Parent)
        {
            if (d.Name != "src") continue;
            foreach (var cfg in new[] { "Debug", "Release" })
            {
                var p = Path.Combine(d.FullName, "MozaicSettings", "bin", cfg, "net10.0-windows", "MozaicSettings.exe");
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    public void ShowLauncher(LauncherMode mode)
    {
        _launcher ??= new Launcher(this);
        _launcher.Show(mode, Tree.FocusedOutput);
    }

    public void OnLauncherIndexed() => _launcher?.Refresh();

    public void Notify(string title, string text)
    {
        Log.Info($"notify: {title}: {text}");
        _tray?.ShowBalloon(title, text, warning: true);
    }

    public IEnumerable<ManagedWindow> MatchWindows(Criteria c)
    {
        var focused = Tree.FocusedWindow;
        foreach (var mw in _windows.Values)
            if (c.Matches(mw.Facts(mw.Node == focused))) yield return mw;
        foreach (var n in Tree.Scratchpad)
            if (n.Tag is ManagedWindow s && !_windows.ContainsKey(s.Hwnd) && c.Matches(s.Facts(false))) yield return s;
    }

    public void OpenConfig()
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + ConfigPath + "\"") { UseShellExecute = true }); }
        catch (Exception e) { Log.Error("open config", e); }
    }

    public void OpenLog()
    {
        if (Log.FilePath == null) return;
        try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Log.FilePath + "\"") { UseShellExecute = true }); }
        catch (Exception e) { Log.Error("open log", e); }
    }

    public IpcServer? Ipc => _ipc;
}
