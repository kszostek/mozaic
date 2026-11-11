namespace Mozaic.Core.Config;

public readonly record struct ClientColor(uint Border, uint Background, uint Text, uint Indicator, uint ChildBorder);

public readonly record struct WorkspaceColor(uint Border, uint Background, uint Text);

public sealed record FontSpec(string Family, double Size)
{
    public static readonly FontSpec Default = new("Segoe UI", 10);
}

public sealed record ConfigError(string File, int Line, string Message)
{
    public override string ToString() => Line > 0 ? $"{Path.GetFileName(File)}:{Line}: {Message}" : Message;
}

/// <param name="Implicit">Added by Mozaic because the config doesn't bind it (not a line in the file).</param>
public sealed record Binding(string Mode, KeyCombo Combo, string Command, bool OnRelease, bool Implicit = false);

public sealed record WindowRule(Criteria Criteria, string Command);

public sealed record AssignRule(Criteria Criteria, string Target, bool ToOutput, bool ByNumber);

public sealed class BarColors
{
    public uint Background { get; set; } = 0x000000;
    public uint Statusline { get; set; } = 0xFFFFFF;
    public uint Separator { get; set; } = 0x666666;
    public WorkspaceColor FocusedWorkspace { get; set; } = new(0x4C7899, 0x285577, 0xFFFFFF);
    public WorkspaceColor ActiveWorkspace { get; set; } = new(0x333333, 0x5F676A, 0xFFFFFF);
    public WorkspaceColor InactiveWorkspace { get; set; } = new(0x333333, 0x222222, 0x888888);
    public WorkspaceColor UrgentWorkspace { get; set; } = new(0x2F343A, 0x900000, 0xFFFFFF);
    public WorkspaceColor BindingMode { get; set; } = new(0x2F343A, 0x900000, 0xFFFFFF);
}

public sealed class BarConfig
{
    public string Position { get; set; } = "top";
    public string? StatusCommand { get; set; }
    public FontSpec? Font { get; set; }
    /// <summary>Bar height in logical pixels (0 = derived from the font).</summary>
    public int Height { get; set; }
    public bool WorkspaceButtons { get; set; } = true;
    public bool BindingModeIndicator { get; set; } = true;
    public bool StripWorkspaceNumbers { get; set; }
    public bool StripWorkspaceName { get; set; }
    public bool ShowTitle { get; set; } = true;
    public List<string> Outputs { get; } = new();
    public string Mode { get; set; } = "dock";
    public string? SeparatorSymbol { get; set; }
    public List<string> StatusBlocks { get; set; } = new() { "cpu", "memory", "battery", "datetime" };
    public string DateTimeFormat { get; set; } = "ddd d MMM  HH:mm";
    public int StatusInterval { get; set; } = 2;
    public int WorkspaceMinWidth { get; set; }
    /// <summary>Draw the icons of the applications on each workspace button.</summary>
    public bool WorkspaceIcons { get; set; } = true;
    /// <summary>Outline the icons of windows that share a tile on the workspace buttons.</summary>
    public bool WorkspaceGroups { get; set; } = true;
    public BarColors Colors { get; } = new();
}

/// <summary>The parsed configuration (i3 syntax + a few Windows-specific extensions).</summary>
public sealed class MozaicConfig
{
    public string? SourcePath { get; set; }
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
    public List<Binding> Bindings { get; } = new();
    public HashSet<string> Modes { get; } = new(StringComparer.Ordinal) { "default" };
    public List<string> Exec { get; } = new();
    public List<string> ExecAlways { get; } = new();

    public FontSpec Font { get; set; } = FontSpec.Default;
    public Modifiers? FloatingModifier { get; set; }

    public int GapsInner { get; set; }
    public int GapsTop { get; set; }
    public int GapsRight { get; set; }
    public int GapsBottom { get; set; }
    public int GapsLeft { get; set; }
    public bool SmartGaps { get; set; }
    public int BorderWidth { get; set; }

    public string DefaultOrientation { get; set; } = "auto";
    public LayoutKind? WorkspaceLayout { get; set; }
    public Dictionary<string, List<string>> WorkspaceOutputs { get; } = new(StringComparer.Ordinal);
    /// <summary>Workspace names in the order they appear in the config ("workspace X output Y" and bindings).</summary>
    public List<string> WorkspaceOrder { get; } = new();

    public List<AssignRule> Assigns { get; } = new();
    public List<WindowRule> ForWindow { get; } = new();
    public List<Criteria> NoFocus { get; } = new();
    public List<Criteria> Ignore { get; } = new();

    public bool FocusFollowsMouse { get; set; }
    public string MouseWarping { get; set; } = "output";
    public bool FocusWrapping { get; set; } = true;
    public bool WorkspaceAutoBackAndForth { get; set; }
    public string FocusOnWindowActivation { get; set; } = "smart";

    public ClientColor Focused { get; set; } = new(0x4C7899, 0x285577, 0xFFFFFF, 0x2E9EF4, 0x285577);
    public ClientColor FocusedInactive { get; set; } = new(0x333333, 0x5F676A, 0xFFFFFF, 0x484E50, 0x5F676A);
    public ClientColor Unfocused { get; set; } = new(0x333333, 0x222222, 0x888888, 0x292D2E, 0x222222);
    public ClientColor Urgent { get; set; } = new(0x2F343A, 0x900000, 0xFFFFFF, 0x900000, 0x900000);

    /// <summary>The bar configuration, or null when the config has no bar block.</summary>
    public BarConfig? Bar { get; set; }

    // ---- Windows-specific extensions
    public bool WindowBorderColors { get; set; } = true;
    public string CornerStyle { get; set; } = "default";
    public bool FloatingAlwaysOnTop { get; set; } = true;
    public bool FlattenContainers { get; set; } = true;
    public bool TilingDrag { get; set; } = true;
    public bool RememberLayout { get; set; } = true;
    public bool Autotiling { get; set; }
    /// <summary>Show the workspace name / binding mode in the middle of the screen when it changes.</summary>
    public bool Osd { get; set; } = true;
    /// <summary>Open the shortcut guide (settings app) when Mozaic starts.</summary>
    public bool Welcome { get; set; }
    /// <summary>
    /// Animated "current" lines in the gaps between windows of the same tile: "on" (flows after focus or
    /// layout changes and while $mod is held, otherwise a static glow), "always" (flows all the time) or "off".
    /// </summary>
    public string GroupLines { get; set; } = "on";
    /// <summary>Color of the group lines (0xRRGGBB); null = the client.focused indicator color.</summary>
    public uint? GroupLinesColor { get; set; }
    /// <summary>Opacity in percent of the most recently used inactive window; null turns the effect off.</summary>
    public int? InactiveOpacity { get; set; } = 90;
    /// <summary>Opacity in percent that windows used long ago fade down to.</summary>
    public int InactiveOpacityMin { get; set; } = 70;
    /// <summary>Windows that always stay fully opaque.</summary>
    public List<Criteria> OpacityIgnore { get; } = new();

    /// <summary>Mozaic draws a title bar above every tiled window (tabbed/stacked containers always have tabs).</summary>
    public bool TitleBars { get; set; }
    /// <summary>The application's icon at the start of title bars and tabs.</summary>
    public bool TitleBarIcons { get; set; } = true;
    /// <summary>Corner radius of title bars and tabs, in pixels at 100% scaling.</summary>
    public int TitleBarRadius { get; set; } = 6;
    /// <summary>Buttons on title bars and on the active tab, left to right (see <see cref="AllTitleBarButtons"/>).</summary>
    public List<string> TitleBarButtons { get; set; } = new(AllTitleBarButtons);
    /// <summary>Corner radius of the focus frame (default_border pixel N), in pixels at 100% scaling.</summary>
    public int BorderRadius { get; set; }

    public static readonly string[] AllTitleBarButtons = { "split", "layout", "floating", "fullscreen", "close" };

    public List<ConfigError> Errors { get; } = new();
}
