using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Mozaic.Core;
using Mozaic.Core.Config;

namespace Mozaic.Settings;

public sealed record Option(string Value, string Label);

/// <summary>
/// View model of the whole config. Every property reads from and writes straight into the
/// <see cref="ConfigDocument"/>, so comments and unknown lines in the file are preserved.
/// </summary>
public sealed class SettingsModel : Observable
{
    private bool _dirty;
    private string _selectedMode = "default";
    private string _keyFilter = "";
    private List<string> _outputs = new() { "primary", "DISPLAY1", "DISPLAY2", "DISPLAY3" };

    public SettingsModel(string path)
    {
        Path = path;
        Load();
    }

    public string Path { get; }
    public ConfigDocument Doc { get; private set; } = new("");
    public MozaicConfig Parsed { get; private set; } = new();
    public ObservableCollection<ConfigError> Errors { get; } = new();

    /// <summary>Raised after every change (previews, keyboard, raw editor).</summary>
    public event Action? Changed;

    public bool Dirty
    {
        get => _dirty;
        private set
        {
            if (_dirty == value) return;
            _dirty = value;
            OnPropertyChanged();
        }
    }

    public bool HasErrors => Errors.Count > 0;

    public string ErrorSummary => Errors.Count switch
    {
        0 => "",
        1 => L.T("1 problem in the configuration"),
        _ => L.F("{0} problems in the configuration", Errors.Count),
    };

    // =====================================================================================
    // Loading, saving, change tracking
    // =====================================================================================

    public void Load()
    {
        string text;
        try { text = File.Exists(Path) ? File.ReadAllText(Path) : DefaultConfig.Text; }
        catch (IOException) { text = DefaultConfig.Text; }
        Doc = new ConfigDocument(text);
        Refresh();
        Dirty = !File.Exists(Path);
    }

    /// <summary>Called by the raw text editor.</summary>
    public void ReplaceText(string text)
    {
        if (text == Doc.Text) return;
        Doc = new ConfigDocument(text);
        Dirty = true;
        Refresh();
    }

    public void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        if (File.Exists(Path)) File.Copy(Path, Path + ".bak", overwrite: true);
        File.WriteAllText(Path, Doc.Text, new UTF8Encoding(false));
        Dirty = false;
    }

    private void Refresh()
    {
        Validate();
        RebuildLists();
        OnAllPropertiesChanged();
        Changed?.Invoke();
    }

    internal void Touch(string? prop, bool structural = false)
    {
        Dirty = true;
        Validate();
        if (structural) RebuildLists();
        if (prop != null) OnPropertyChanged(prop);
        Changed?.Invoke();
    }

    private void Validate()
    {
        Parsed = ConfigParser.Parse(Doc.Text, Path);
        Errors.Clear();
        foreach (var e in Parsed.Errors) Errors.Add(e);
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(ErrorSummary));
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    private string? Get(string key, string block = "") => Doc.Get(key, block);

    internal void SetRaw(string key, string? value, string block = "", [CallerMemberName] string? prop = null)
    {
        Doc.Set(key, value, block);
        Touch(prop);
    }

    private bool GetBool(string key, bool def, string block = "")
    {
        var v = Get(key, block);
        if (v == null) return def;
        try { return ConfigParser.ParseBool(v); }
        catch (FormatException) { return def; }
    }

    private void SetBool(string key, bool value, string block = "", string on = "yes", string off = "no", [CallerMemberName] string? prop = null) =>
        SetRaw(key, value ? on : off, block, prop);

    private int GetInt(string key, int def, string block = "")
    {
        var v = Get(key, block)?.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : def;
    }

    public static string Hex(uint rgb) => "#" + rgb.ToString("x6", CultureInfo.InvariantCulture);

    // =====================================================================================
    // General
    // =====================================================================================

    public static List<Option> ModifierOptions { get; } = new()
    {
        new("Alt", "Alt"),
        new("Win", "Win"),
    };

    private static string NormalizeMod(string m) => m switch
    {
        "Mod4" or "Super" or "Win" or "Windows" => "Win",
        "Mod1" or "Alt" or "Meta" => "Alt",
        _ => m,
    };

    public string Modifier
    {
        get => NormalizeMod(Doc.GetVariable("$mod") ?? "Mod1");
        set
        {
            Doc.SetVariable("$mod", value);
            Touch(nameof(Modifier), structural: true);
            OnPropertyChanged(nameof(ModifierName));
        }
    }

    /// <summary>Display name of $mod ("Alt" / "Win").</summary>
    public string ModifierName => Modifier;

    public string Terminal
    {
        get => Doc.GetVariable("$term") ?? "";
        set
        {
            Doc.SetVariable("$term", value);
            Touch(nameof(Terminal));
        }
    }

    public static List<string> FontFamilies { get; } =
        System.Windows.Media.Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(s => s, StringComparer.CurrentCultureIgnoreCase).ToList();

    public string FontFamily
    {
        get => Parsed.Font.Family;
        set => SetFont(value, FontSize);
    }

    public double FontSize
    {
        get => Parsed.Font.Size;
        set => SetFont(FontFamily, Math.Clamp(Math.Round(value), 6, 24));
    }

    private void SetFont(string family, double size)
    {
        if (string.IsNullOrWhiteSpace(family)) return;
        Doc.Set("font", "pango:" + family.Trim() + " " + size.ToString(CultureInfo.InvariantCulture));
        Touch(nameof(FontFamily));
        OnPropertyChanged(nameof(FontSize));
    }

    public bool Autotiling { get => GetBool("autotiling", false); set => SetBool("autotiling", value); }
    public bool FocusFollowsMouse { get => GetBool("focus_follows_mouse", false); set => SetBool("focus_follows_mouse", value); }
    public bool MouseWarping { get => (Get("mouse_warping") ?? "output") != "none"; set => SetRaw("mouse_warping", value ? "output" : "none"); }
    public bool FocusWrapping { get => GetBool("focus_wrapping", true); set => SetBool("focus_wrapping", value); }
    public bool AutoBackAndForth { get => GetBool("workspace_auto_back_and_forth", false); set => SetBool("workspace_auto_back_and_forth", value); }
    public bool TilingDrag { get => Parsed.TilingDrag; set => SetRaw("tiling_drag", value ? "on" : "off"); }
    public bool FloatingModifier { get => Parsed.FloatingModifier != null; set => SetRaw("floating_modifier", value ? "$mod" : null); }
    public bool RememberLayout { get => GetBool("remember_layout", true); set => SetBool("remember_layout", value); }
    public bool FlattenContainers { get => GetBool("flatten_containers", true); set => SetBool("flatten_containers", value); }
    public bool FloatingAlwaysOnTop { get => GetBool("floating_always_on_top", true); set => SetBool("floating_always_on_top", value); }
    public bool Osd { get => GetBool("osd", true); set => SetBool("osd", value); }
    public bool Welcome { get => GetBool("welcome", false); set => SetBool("welcome", value); }

    public static List<Option> ActivationOptions { get; } = new()
    {
        new("smart", L.T("Focus it if visible, otherwise mark it urgent")),
        new("urgent", L.T("Only mark it urgent")),
        new("focus", L.T("Always focus it (switches workspace)")),
        new("none", L.T("Ignore it")),
    };

    public string FocusOnActivation { get => Get("focus_on_window_activation") ?? "smart"; set => SetRaw("focus_on_window_activation", value); }

    public static List<Option> OrientationOptions { get; } = new()
    {
        new("auto", L.T("Automatic (by monitor shape)")),
        new("horizontal", L.T("Side by side")),
        new("vertical", L.T("Stacked vertically")),
    };

    public string DefaultOrientation { get => Get("default_orientation") ?? "auto"; set => SetRaw("default_orientation", value); }

    public static List<Option> WorkspaceLayoutOptions { get; } = new()
    {
        new("default", L.T("Split")),
        new("tabbed", L.T("Tabbed")),
        new("stacking", L.T("Stacked")),
    };

    public string WorkspaceLayout { get => Get("workspace_layout") ?? "default"; set => SetRaw("workspace_layout", value); }

    // =====================================================================================
    // Appearance
    // =====================================================================================

    public int GapsInner { get => GetInt("gaps inner", 0); set => SetRaw("gaps inner", Math.Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture)); }
    public int GapsOuter { get => GetInt("gaps outer", 0); set => SetRaw("gaps outer", Math.Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture)); }
    public bool SmartGaps { get => GetBool("smart_gaps", false); set => SetBool("smart_gaps", value, on: "on", off: "off"); }
    public int BorderWidth { get => Parsed.BorderWidth; set => SetRaw("default_border", "pixel " + Math.Clamp(value, 0, 20).ToString(CultureInfo.InvariantCulture)); }
    public bool WindowBorderColors { get => GetBool("window_border_colors", true); set => SetBool("window_border_colors", value); }

    public static List<Option> CornerOptions { get; } = new()
    {
        new("default", L.T("Windows default")),
        new("round", L.T("Rounded")),
        new("small", L.T("Slightly rounded")),
        new("square", L.T("Square (like i3)")),
    };

    public string CornerStyle { get => Get("corner_style") ?? "default"; set => SetRaw("corner_style", value); }

    public static List<Option> GroupLineOptions { get; } = new()
    {
        new("on", L.T("Flowing after changes and while $mod is held")),
        new("always", L.T("Always flowing")),
        new("off", L.T("Off")),
    };

    public string GroupLines { get => Parsed.GroupLines; set => SetRaw("group_lines", value); }

    public uint GroupLinesColor { get => Parsed.GroupLinesColor ?? Parsed.Focused.Indicator; set => SetRaw("group_lines_color", Hex(value)); }

    public int BorderRadius { get => Parsed.BorderRadius; set => SetRaw("border_radius", Math.Clamp(value, 0, 60).ToString(CultureInfo.InvariantCulture)); }

    // ---- Title bars
    public bool TitleBars { get => Parsed.TitleBars; set => SetBool("titlebars", value); }
    public bool TitleBarIcons { get => Parsed.TitleBarIcons; set => SetBool("titlebar_icons", value); }
    public int TitleBarRadius { get => Parsed.TitleBarRadius; set => SetRaw("titlebar_radius", Math.Clamp(value, 0, 40).ToString(CultureInfo.InvariantCulture)); }
    public bool ButtonSplit { get => HasButton("split"); set => SetButton("split", value); }
    public bool ButtonLayout { get => HasButton("layout"); set => SetButton("layout", value); }
    public bool ButtonFloating { get => HasButton("floating"); set => SetButton("floating", value); }
    public bool ButtonFullscreen { get => HasButton("fullscreen"); set => SetButton("fullscreen", value); }
    public bool ButtonClose { get => HasButton("close"); set => SetButton("close", value); }

    private bool HasButton(string name) => Parsed.TitleBarButtons.Contains(name);

    private void SetButton(string name, bool on, [CallerMemberName] string? prop = null)
    {
        // Keep the standard order: split layout floating fullscreen close.
        var list = MozaicConfig.AllTitleBarButtons.Where(b => b == name ? on : Parsed.TitleBarButtons.Contains(b)).ToList();
        SetRaw("titlebar_buttons", list.Count == 0 ? "none" : string.Join(' ', list), prop: prop);
    }

    public bool InactiveOpacityOn { get => Parsed.InactiveOpacity != null; set => SetOpacity(value ? Parsed.InactiveOpacity ?? 90 : null, Parsed.InactiveOpacityMin); }

    /// <summary>Opacity of the previously used window, in percent.</summary>
    public int OpacityLastUsed { get => Parsed.InactiveOpacity ?? 90; set => SetOpacity(Math.Clamp(value, 30, 100), Math.Min(OpacityMin, value)); }

    /// <summary>Opacity that windows used long ago fade down to, in percent.</summary>
    public int OpacityMin { get => Parsed.InactiveOpacityMin; set => SetOpacity(Math.Max(OpacityLastUsed, value), Math.Clamp(value, 30, 100)); }

    private void SetOpacity(int? first, int min)
    {
        SetRaw("inactive_opacity", first is int f ? f.ToString(CultureInfo.InvariantCulture) + " " + Math.Min(min, f).ToString(CultureInfo.InvariantCulture) : "off",
            prop: nameof(InactiveOpacityOn));
        OnPropertyChanged(nameof(OpacityLastUsed));
        OnPropertyChanged(nameof(OpacityMin));
    }

    public List<ClientColorModel> ClientColors { get; private set; } = new();

    // =====================================================================================
    // Bar
    // =====================================================================================

    private bool HasBar => Doc.HasBlock("bar");

    public bool BarVisible
    {
        get => HasBar && (Get("mode", "bar") ?? "dock") != "invisible";
        set
        {
            if (!HasBar) Doc.Set("position", "top", "bar");
            Doc.Set("mode", value ? null : "invisible", "bar");
            Touch(nameof(BarVisible));
        }
    }

    public static List<Option> PositionOptions { get; } = new()
    {
        new("top", L.T("Top")),
        new("bottom", L.T("Bottom")),
    };

    public string BarPosition { get => Get("position", "bar") ?? "top"; set => SetRaw("position", value, "bar"); }
    public int BarHeight { get => GetInt("height", 0, "bar"); set => SetRaw("height", value <= 0 ? null : value.ToString(CultureInfo.InvariantCulture), "bar"); }

    private static readonly string[] BlockOrder = { "cpu", "memory", "battery", "disk", "datetime" };

    private bool HasBlock(string name) => (Parsed.Bar?.StatusBlocks ?? new List<string>()).Any(b => b == name || (name == "memory" && b is "mem" or "ram") || (name == "datetime" && b is "time" or "date" or "clock"));

    private void SetBlock(string name, bool on, string prop)
    {
        var current = BlockOrder.Where(HasBlock).ToHashSet();
        if (on) current.Add(name); else current.Remove(name);
        var value = string.Join(' ', BlockOrder.Where(current.Contains));
        SetRaw("status_blocks", "\"" + value + "\"", "bar", prop);
    }

    public bool StatusCpu { get => HasBlock("cpu"); set => SetBlock("cpu", value, nameof(StatusCpu)); }
    public bool StatusMemory { get => HasBlock("memory"); set => SetBlock("memory", value, nameof(StatusMemory)); }
    public bool StatusBattery { get => HasBlock("battery"); set => SetBlock("battery", value, nameof(StatusBattery)); }
    public bool StatusDisk { get => HasBlock("disk"); set => SetBlock("disk", value, nameof(StatusDisk)); }
    public bool StatusDateTime { get => HasBlock("datetime"); set => SetBlock("datetime", value, nameof(StatusDateTime)); }

    public string DateTimeFormat
    {
        get => ConfigDocument.Unquote(Get("datetime_format", "bar")) ?? "ddd d MMM  HH:mm";
        set
        {
            SetRaw("datetime_format", "\"" + value.Replace("\"", "") + "\"", "bar");
            OnPropertyChanged(nameof(DateTimePreview));
        }
    }

    public string DateTimePreview
    {
        get
        {
            try { return DateTime.Now.ToString(DateTimeFormat, CultureInfo.CurrentCulture); }
            catch (FormatException) { return L.T("invalid format"); }
        }
    }

    public string StatusCommand { get => Get("status_command", "bar") ?? ""; set => SetRaw("status_command", string.IsNullOrWhiteSpace(value) ? null : value.Trim(), "bar"); }
    public bool ShowTitle { get => GetBool("show_title", true, "bar"); set => SetBool("show_title", value, "bar"); }
    public bool WorkspaceButtons { get => GetBool("workspace_buttons", true, "bar"); set => SetBool("workspace_buttons", value, "bar"); }
    public bool WorkspaceIcons { get => GetBool("workspace_icons", true, "bar"); set => SetBool("workspace_icons", value, "bar"); }
    public bool WorkspaceGroups { get => GetBool("workspace_groups", true, "bar"); set => SetBool("workspace_groups", value, "bar"); }
    public bool BindingModeIndicator { get => GetBool("binding_mode_indicator", true, "bar"); set => SetBool("binding_mode_indicator", value, "bar"); }
    public bool StripWorkspaceNumbers { get => GetBool("strip_workspace_numbers", false, "bar"); set => SetBool("strip_workspace_numbers", value, "bar"); }

    private BarColors BarColorsParsed => Parsed.Bar?.Colors ?? new BarColors();

    public uint BarBackground { get => BarColorsParsed.Background; set => SetRaw("background", Hex(value), "bar.colors"); }
    public uint BarStatusline { get => BarColorsParsed.Statusline; set => SetRaw("statusline", Hex(value), "bar.colors"); }
    public uint BarSeparator { get => BarColorsParsed.Separator; set => SetRaw("separator", Hex(value), "bar.colors"); }

    public List<WorkspaceColorModel> WorkspaceColors { get; private set; } = new();

    public bool TaskbarAutoHide
    {
        get => Native.TaskbarAutoHide;
        set
        {
            Native.TaskbarAutoHide = value;
            OnPropertyChanged();
        }
    }

    // =====================================================================================
    // Keybindings
    // =====================================================================================

    public ObservableCollection<string> Modes { get; } = new();
    public ObservableCollection<BindingItem> Bindings { get; } = new();

    public string SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (value == null || value == _selectedMode) return;
            _selectedMode = value;
            OnPropertyChanged();
            RebuildBindings();
        }
    }

    public string KeyFilter
    {
        get => _keyFilter;
        set
        {
            _keyFilter = value ?? "";
            OnPropertyChanged();
            RebuildBindings();
        }
    }

    /// <summary>"$mod+Shift+q" → "Alt + Shift + Q".</summary>
    public string PrettyKeys(string raw)
    {
        var resolved = Doc.Resolve(raw);
        var parts = resolved.EndsWith("++", StringComparison.Ordinal)
            ? resolved[..^2].Split('+').Append("+").ToArray()
            : resolved.Split('+');
        return string.Join(" + ", parts.Select(PrettyKey));
    }

    public static string PrettyKey(string k) => k switch
    {
        "Mod1" or "Meta" => "Alt",
        "Mod4" or "Super" or "Windows" => "Win",
        "Control" => "Ctrl",
        "Return" => "Enter",
        "space" => L.T("Space"),
        "minus" => "-",
        "equal" => "=",
        "plus" => "+",
        "grave" => "`",
        "comma" => ",",
        "period" => ".",
        "slash" => "/",
        "backslash" => "\\",
        "semicolon" => ";",
        "apostrophe" => "'",
        "bracketleft" => "[",
        "bracketright" => "]",
        "Left" => "←",
        "Right" => "→",
        "Up" => "↑",
        "Down" => "↓",
        "Prior" or "Page_Up" => "PgUp",
        "Next" or "Page_Down" => "PgDn",
        "Escape" => "Esc",
        "BackSpace" => "Backspace",
        _ when k.Length == 1 => k.ToUpperInvariant(),
        _ => k,
    };

    public void AddBinding(string mode, string keys, string command, bool release)
    {
        Doc.AddBinding(mode, keys, command, release);
        Touch(null, structural: true);
    }

    public void UpdateBinding(BindingItem item, string keys, string command, bool release)
    {
        Doc.ReplaceLine(item.Line, ConfigDocument.FormatBinding(keys, command, release, item.IsCode));
        Touch(null, structural: true);
    }

    public void DeleteLine(int line)
    {
        Doc.RemoveLine(line);
        Touch(null, structural: true);
    }

    /// <summary>Resolves a binding's keys to a combo (for conflicts / the keyboard view).</summary>
    public KeyCombo? ComboOf(string rawKeys, bool isCode)
    {
        var resolved = Doc.Resolve(rawKeys);
        return KeyNames.TryParseCombo(resolved, null, isCode, out var c, out _) ? c : null;
    }

    private void RebuildBindings()
    {
        Bindings.Clear();
        var all = Doc.Bindings().Where(b => b.Mode == _selectedMode).ToList();
        var combos = all.Select(b => ComboOf(b.Keys, b.IsCode)).ToList();
        for (int i = 0; i < all.Count; i++)
        {
            var b = all[i];
            var pretty = PrettyKeys(b.Keys);
            if (_keyFilter.Length > 0 &&
                !pretty.Contains(_keyFilter, StringComparison.OrdinalIgnoreCase) &&
                !b.Command.Contains(_keyFilter, StringComparison.OrdinalIgnoreCase))
                continue;
            bool conflict = combos[i] != null && combos.Where((c, j) => j != i && c == combos[i]).Any();
            Bindings.Add(new BindingItem(b.Line, b.Mode, b.Keys, pretty, b.Command, b.Release, b.IsCode, conflict,
                KeyboardLabels.Describe(Doc.Resolve(b.Command)).Category));
        }
    }

    // =====================================================================================
    // Rules, workspaces, startup
    // =====================================================================================

    public ObservableCollection<RuleItem> Rules { get; } = new();
    public ObservableCollection<WorkspaceItem> Workspaces { get; } = new();
    public ObservableCollection<ExecItem> Execs { get; } = new();

    public void AddRule(string kind, string criteria, string action)
    {
        Doc.AddRule(kind, criteria, action);
        Touch(null, structural: true);
    }

    public void UpdateRule(RuleItem item, string kind, string criteria, string action)
    {
        Doc.ReplaceLine(item.Line, ConfigDocument.FormatRule(kind, criteria, action));
        Touch(null, structural: true);
    }

    public List<string> OutputOptions
    {
        get => _outputs;
        set
        {
            _outputs = value;
            OnPropertyChanged();
        }
    }

    internal void RenameWorkspace(WorkspaceItem item, string newName)
    {
        if (item.Variable == null || newName.Trim().Length == 0) return;
        var output = item.Output;
        if (output != null) Doc.SetWorkspaceOutput(item.Name, null);
        Doc.SetVariable(item.Variable, newName.Trim());
        if (output != null) Doc.SetWorkspaceOutput(newName.Trim(), output);
        Touch(null);
    }

    internal void SetWorkspaceOutput(WorkspaceItem item, string? output)
    {
        Doc.SetWorkspaceOutput(item.Name, string.IsNullOrEmpty(output) ? null : output);
        Touch(null);
    }

    public void AddExec(bool always, string command)
    {
        Doc.AddExec(always, command);
        Touch(null, structural: true);
    }

    internal void UpdateExec(ExecItem item)
    {
        Doc.ReplaceLine(item.Line, (item.Always ? "exec_always " : "exec ") + item.Command.Trim());
        Touch(null);
    }

    public bool StartWithWindows
    {
        get => Native.AutostartCommand != null;
        set
        {
            var exe = MozaicExePath;
            Native.AutostartCommand = value && exe != null ? "\"" + exe + "\"" : null;
            OnPropertyChanged();
        }
    }

    /// <summary>Mozaic.exe: reported by the running instance, or next to this program.</summary>
    public string? MozaicExePath { get; set; } =
        File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, "Mozaic.exe")) ? System.IO.Path.Combine(AppContext.BaseDirectory, "Mozaic.exe") : null;

    private void RebuildLists()
    {
        Modes.Clear();
        foreach (var m in Doc.ModeNames()) Modes.Add(m);
        if (!Modes.Contains(_selectedMode)) _selectedMode = "default";
        RebuildBindings();

        Rules.Clear();
        foreach (var r in Doc.Rules()) Rules.Add(new RuleItem(r.Line, r.Kind, r.Criteria, r.Action));

        Execs.Clear();
        foreach (var e in Doc.Execs()) Execs.Add(new ExecItem(this, e.Line, e.Always, e.Command));

        Workspaces.Clear();
        var outputs = Doc.WorkspaceOutputs();
        var vars = Doc.Variables();
        var wsVars = vars.Where(v => v.Key.StartsWith("$ws", StringComparison.Ordinal) && int.TryParse(v.Key[3..], out _))
            .OrderBy(v => int.Parse(v.Key[3..], CultureInfo.InvariantCulture)).ToList();
        if (wsVars.Count > 0)
        {
            foreach (var (name, value) in wsVars)
                Workspaces.Add(new WorkspaceItem(this, name, value, outputs.FirstOrDefault(o => o.Workspace == value).Outputs));
        }
        else
        {
            foreach (var ws in Parsed.WorkspaceOrder)
                Workspaces.Add(new WorkspaceItem(this, null, ws, outputs.FirstOrDefault(o => o.Workspace == ws).Outputs));
        }

        ClientColors = new List<ClientColorModel>
        {
            new(this, "client.focused", L.T("Focused"), c => c.Focused),
            new(this, "client.focused_inactive", L.T("Focused on another monitor"), c => c.FocusedInactive),
            new(this, "client.unfocused", L.T("Unfocused"), c => c.Unfocused),
            new(this, "client.urgent", L.T("Urgent"), c => c.Urgent),
        };
        WorkspaceColors = new List<WorkspaceColorModel>
        {
            new(this, "focused_workspace", L.T("Focused workspace"), c => c.FocusedWorkspace),
            new(this, "active_workspace", L.T("Visible on another monitor"), c => c.ActiveWorkspace),
            new(this, "inactive_workspace", L.T("Hidden workspace"), c => c.InactiveWorkspace),
            new(this, "urgent_workspace", L.T("Urgent workspace"), c => c.UrgentWorkspace),
            new(this, "binding_mode", L.T("Binding mode"), c => c.BindingMode),
        };
        OnPropertyChanged(nameof(ClientColors));
        OnPropertyChanged(nameof(WorkspaceColors));
    }
}

// =========================================================================================
// Item models
// =========================================================================================

public sealed record BindingItem(int Line, string Mode, string Keys, string PrettyKeys, string Command, bool Release, bool IsCode,
    bool Conflict, KeyCategory Category);

public sealed record RuleItem(int Line, string Kind, string Criteria, string Action)
{
    public string KindLabel => Kind switch
    {
        "for_window" => L.T("When it opens"),
        "assign" => L.T("Open on"),
        "ignore" => L.T("Do not manage"),
        "no_focus" => L.T("Do not focus"),
        _ => Kind,
    };

    public string Summary => Kind switch
    {
        "assign" => L.T("workspace") + " " + Action.Replace("workspace", "").Replace("number", "").Replace("→", "").Trim(),
        "ignore" or "no_focus" => "",
        _ => Action,
    };
}

public sealed class WorkspaceItem(SettingsModel model, string? variable, string name, string? output) : Observable
{
    private string _name = name;
    private string? _output = output;

    public string? Variable { get; } = variable;
    public bool Editable => Variable != null;

    public string Name
    {
        get => _name;
        set
        {
            if (value == _name || string.IsNullOrWhiteSpace(value)) return;
            model.RenameWorkspace(this, value);
            _name = value.Trim();
            OnPropertyChanged();
        }
    }

    public string Output
    {
        get => _output ?? "";
        set
        {
            model.SetWorkspaceOutput(this, value);
            _output = string.IsNullOrEmpty(value) ? null : value;
            OnPropertyChanged();
        }
    }

    public List<string> OutputOptions => new List<string> { "" }.Concat(model.OutputOptions).ToList();
}

public sealed class ExecItem(SettingsModel model, int line, bool always, string command) : Observable
{
    private bool _always = always;
    private string _command = command;

    public int Line { get; } = line;

    public bool Always
    {
        get => _always;
        set
        {
            _always = value;
            model.UpdateExec(this);
            OnPropertyChanged();
        }
    }

    public string Command
    {
        get => _command;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _command = value;
            model.UpdateExec(this);
            OnPropertyChanged();
        }
    }
}

public sealed class ClientColorModel(SettingsModel model, string key, string label, Func<MozaicConfig, ClientColor> get) : Observable
{
    public string Label { get; } = label;
    private ClientColor C => get(model.Parsed);

    public uint Border { get => C.Border; set => Write(C with { Border = value }); }
    public uint Background { get => C.Background; set => Write(C with { Background = value }); }
    public uint Text { get => C.Text; set => Write(C with { Text = value }); }
    public uint Indicator { get => C.Indicator; set => Write(C with { Indicator = value }); }
    public uint ChildBorder { get => C.ChildBorder; set => Write(C with { ChildBorder = value }); }

    private void Write(ClientColor c)
    {
        model.SetRaw(key, $"{SettingsModel.Hex(c.Border)} {SettingsModel.Hex(c.Background)} {SettingsModel.Hex(c.Text)} {SettingsModel.Hex(c.Indicator)} {SettingsModel.Hex(c.ChildBorder)}", "", null);
        OnAllPropertiesChanged();
    }
}

public sealed class WorkspaceColorModel(SettingsModel model, string key, string label, Func<BarColors, WorkspaceColor> get) : Observable
{
    public string Label { get; } = label;
    private WorkspaceColor C => get(model.Parsed.Bar?.Colors ?? new BarColors());

    public uint Border { get => C.Border; set => Write(C with { Border = value }); }
    public uint Background { get => C.Background; set => Write(C with { Background = value }); }
    public uint Text { get => C.Text; set => Write(C with { Text = value }); }

    private void Write(WorkspaceColor c)
    {
        model.SetRaw(key, $"{SettingsModel.Hex(c.Border)} {SettingsModel.Hex(c.Background)} {SettingsModel.Hex(c.Text)}", "bar.colors", null);
        OnAllPropertiesChanged();
    }
}
