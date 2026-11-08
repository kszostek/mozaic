using System.Globalization;
using Mozaic.Core.Commands;

namespace Mozaic.Core.Config;

/// <summary>
/// Parses i3 config syntax. Unsupported X11-only directives are accepted and ignored so that
/// existing i3 configs load; genuine mistakes are reported in <see cref="MozaicConfig.Errors"/>.
/// </summary>
public static class ConfigParser
{
    private enum Block { Top, Mode, Bar, BarColors, Skip }

    private static readonly HashSet<string> IgnoredDirectives = new(StringComparer.OrdinalIgnoreCase)
    {
        "smart_borders", "hide_edge_borders", "title_align", "popup_during_fullscreen", "floating_minimum_size",
        "floating_maximum_size", "show_marks", "force_xinerama", "fake-outputs", "ipc-socket", "ipc_socket",
        "ipc_kill_timeout", "restart_state", "new_window", "new_float", "default_floating_border",
        "client.background", "client.placeholder", "force_display_urgency_hint", "focus_wrapping_force",
        "for_window_ignore", "workspace_buttons", "tray_output", "title_format", "client.focused_tab_title",
        "xwayland", "input", "output", "seat", "default_dim_inactive", "primary_selection",
    };

    public static MozaicConfig Parse(string text, string? path = null, Func<char, int?>? charToVk = null)
    {
        var cfg = new MozaicConfig { SourcePath = path };
        var state = new State(cfg, charToVk);
        ParseInto(state, text, path ?? "<config>", 0);
        if (state.Stack.Count > 1)
            cfg.Errors.Add(new ConfigError(path ?? "<config>", 0, "missing closing '}'"));
        AddImplicitBindings(state);
        return cfg;
    }

    private sealed class State(MozaicConfig cfg, Func<char, int?>? charToVk)
    {
        public MozaicConfig Cfg { get; } = cfg;
        public Func<char, int?>? CharToVk { get; } = charToVk;
        public Stack<(Block Block, string Name)> Stack { get; } = new(new[] { (Block.Top, "") });
        public List<string> VarNamesByLength { get; set; } = new();
    }

    private static List<(int Line, string Text)> JoinLines(string text)
    {
        var raw = text.Replace("\r\n", "\n").Split('\n');
        var lines = new List<(int, string)>();
        for (int i = 0; i < raw.Length; i++)
        {
            int start = i;
            string l = raw[i];
            while (l.TrimEnd().EndsWith('\\') && i + 1 < raw.Length)
            {
                var t = l.TrimEnd();
                l = t[..^1] + " " + raw[++i].TrimStart();
            }
            lines.Add((start + 1, l));
        }
        return lines;
    }

    private static string Substitute(State st, string s)
    {
        if (s.IndexOf('$') < 0) return s;
        foreach (var name in st.VarNamesByLength)
            s = s.Replace(name, st.Cfg.Variables[name], StringComparison.Ordinal);
        return s;
    }

    private static void ParseInto(State st, string text, string file, int depth)
    {
        var lines = JoinLines(text);

        // Pass 1: variables (i3 resolves them before parsing the rest).
        foreach (var (_, raw) in lines)
        {
            var l = raw.Trim();
            bool isSet = l.StartsWith("set ", StringComparison.Ordinal) || l.StartsWith("set\t", StringComparison.Ordinal);
            bool isRes = l.StartsWith("set_from_resource ", StringComparison.Ordinal);
            if (!isSet && !isRes) continue;
            var rest = l[(isSet ? 4 : 18)..].Trim();
            int sp = IndexOfWhitespace(rest);
            if (sp < 0 || rest[0] != '$') continue;
            string name = rest[..sp];
            string value = rest[sp..].Trim();
            if (isRes)
            {
                // set_from_resource $var resource.name fallback
                int sp2 = IndexOfWhitespace(value);
                value = sp2 < 0 ? "" : value[sp2..].Trim();
            }
            st.Cfg.Variables[name] = Substitute(st, value);
            st.VarNamesByLength = st.Cfg.Variables.Keys.OrderByDescending(k => k.Length).ToList();
        }

        // Pass 2: everything else.
        foreach (var (lineNo, raw) in lines)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            if (trimmed.StartsWith("set ", StringComparison.Ordinal) || trimmed.StartsWith("set\t", StringComparison.Ordinal) ||
                trimmed.StartsWith("set_from_resource ", StringComparison.Ordinal))
                continue;
            var line = Substitute(st, trimmed);
            try
            {
                ParseLine(st, line, file, lineNo, depth);
            }
            catch (FormatException e)
            {
                st.Cfg.Errors.Add(new ConfigError(file, lineNo, e.Message));
            }
            catch (CommandParseException e)
            {
                st.Cfg.Errors.Add(new ConfigError(file, lineNo, e.Message));
            }
        }
    }

    private static int IndexOfWhitespace(string s)
    {
        for (int i = 0; i < s.Length; i++) if (char.IsWhiteSpace(s[i])) return i;
        return -1;
    }

    /// <summary>Splits off the first word; returns (word, rest).</summary>
    private static (string Word, string Remainder) Head(string s)
    {
        s = s.Trim();
        int sp = IndexOfWhitespace(s);
        return sp < 0 ? (s, "") : (s[..sp], s[sp..].Trim());
    }

    private static void ParseLine(State st, string line, string file, int lineNo, int depth)
    {
        var cfg = st.Cfg;
        var (block, blockName) = st.Stack.Peek();

        if (line == "}")
        {
            if (st.Stack.Count <= 1) throw new FormatException("unexpected '}'");
            st.Stack.Pop();
            return;
        }

        if (block == Block.Skip)
        {
            if (line.EndsWith('{')) st.Stack.Push((Block.Skip, ""));
            return;
        }

        var (word, rest) = Head(line);
        string directive = word.ToLowerInvariant();

        if (block == Block.Mode)
        {
            if (directive is "bindsym" or "bindcode") ParseBinding(st, blockName, rest, directive == "bindcode");
            else throw new FormatException($"only bindsym/bindcode are allowed inside a mode, got '{word}'");
            return;
        }
        if (block == Block.Bar) { ParseBarLine(st, directive, rest, line); return; }
        if (block == Block.BarColors) { ParseBarColor(cfg.Bar!.Colors, directive, rest); return; }

        switch (directive)
        {
            case "bindsym":
            case "bindcode":
                ParseBinding(st, "default", rest, directive == "bindcode");
                break;

            case "mode":
            {
                var toks = CommandParser.Tokenize(rest);
                toks.RemoveAll(t => t.StartsWith("--", StringComparison.Ordinal));
                if (toks.Count < 2 || toks[^1] != "{") throw new FormatException("expected: mode \"name\" {");
                string name = toks[0];
                cfg.Modes.Add(name);
                st.Stack.Push((Block.Mode, name));
                break;
            }

            case "bar":
                if (rest != "{") throw new FormatException("expected: bar {");
                cfg.Bar ??= new BarConfig();
                st.Stack.Push((Block.Bar, ""));
                break;

            case "font":
                cfg.Font = ParseFont(rest);
                break;

            case "floating_modifier":
            {
                var mods = Modifiers.None;
                foreach (var part in rest.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!KeyNames.TryParseModifier(part, out var m)) throw new FormatException($"unknown modifier '{part}'");
                    mods |= m;
                }
                cfg.FloatingModifier = mods == Modifiers.None ? null : mods;
                break;
            }

            case "exec":
            case "exec_always":
            {
                var cmd = StripExecFlags(rest);
                if (cmd.Length == 0) throw new FormatException("exec needs a command");
                (directive == "exec" ? cfg.Exec : cfg.ExecAlways).Add(cmd);
                break;
            }

            case "gaps":
                ParseGaps(cfg, rest);
                break;

            case "smart_gaps":
                cfg.SmartGaps = rest.Trim().ToLowerInvariant() switch
                {
                    "inverse_outer" => true,
                    var v => ParseBool(v),
                };
                break;

            case "default_orientation":
                cfg.DefaultOrientation = rest.ToLowerInvariant() switch
                {
                    "horizontal" => "horizontal",
                    "vertical" => "vertical",
                    "auto" => "auto",
                    _ => throw new FormatException("default_orientation must be horizontal, vertical or auto"),
                };
                break;

            case "workspace_layout":
                cfg.WorkspaceLayout = rest.ToLowerInvariant() switch
                {
                    "default" => null,
                    "stacking" or "stacked" => LayoutKind.Stacked,
                    "tabbed" => LayoutKind.Tabbed,
                    _ => throw new FormatException("workspace_layout must be default, stacking or tabbed"),
                };
                break;

            case "default_border":
                cfg.BorderWidth = ParseBorder(rest);
                break;

            case "workspace":
                ParseWorkspaceDirective(cfg, rest);
                break;

            case "assign":
                ParseAssign(cfg, rest);
                break;

            case "for_window":
            {
                var (crit, cmd) = SplitCriteria(rest);
                if (crit == null || cmd.Length == 0) throw new FormatException("expected: for_window [criteria] command");
                CommandParser.Parse(cmd); // validate syntax early
                cfg.ForWindow.Add(new WindowRule(crit, cmd));
                break;
            }

            case "no_focus":
            {
                var (crit, _) = SplitCriteria(rest);
                if (crit == null) throw new FormatException("expected: no_focus [criteria]");
                cfg.NoFocus.Add(crit);
                break;
            }

            case "ignore":
            {
                var (crit, _) = SplitCriteria(rest);
                if (crit == null) throw new FormatException("expected: ignore [criteria]");
                cfg.Ignore.Add(crit);
                break;
            }

            case "focus_follows_mouse": cfg.FocusFollowsMouse = ParseBool(rest); break;
            case "mouse_warping":
                cfg.MouseWarping = rest.ToLowerInvariant() is "none" ? "none" : "output";
                break;
            case "focus_wrapping":
                cfg.FocusWrapping = rest.ToLowerInvariant() switch
                {
                    "force" or "workspace" => true,
                    var v => ParseBool(v),
                };
                break;
            case "force_focus_wrapping": if (ParseBool(rest)) cfg.FocusWrapping = true; break;
            case "workspace_auto_back_and_forth": cfg.WorkspaceAutoBackAndForth = ParseBool(rest); break;
            case "focus_on_window_activation":
                cfg.FocusOnWindowActivation = rest.ToLowerInvariant() switch
                {
                    "smart" or "urgent" or "focus" or "none" => rest.ToLowerInvariant(),
                    _ => throw new FormatException("focus_on_window_activation must be smart, urgent, focus or none"),
                };
                break;

            case "client.focused": cfg.Focused = ParseClientColor(rest, cfg.Focused); break;
            case "client.focused_inactive": cfg.FocusedInactive = ParseClientColor(rest, cfg.FocusedInactive); break;
            case "client.unfocused": cfg.Unfocused = ParseClientColor(rest, cfg.Unfocused); break;
            case "client.urgent": cfg.Urgent = ParseClientColor(rest, cfg.Urgent); break;

            case "include":
                ParseInclude(st, rest, file, depth);
                break;

            // ---- Windows-specific extensions
            case "window_border_colors":
            case "dwm_borders": cfg.WindowBorderColors = ParseBool(rest); break;
            case "corner_style":
                cfg.CornerStyle = rest.ToLowerInvariant() switch
                {
                    "default" or "square" or "round" or "small" => rest.ToLowerInvariant(),
                    "rounded" => "round",
                    _ => throw new FormatException("corner_style must be default, square, round or small"),
                };
                break;
            case "floating_always_on_top": cfg.FloatingAlwaysOnTop = ParseBool(rest); break;
            case "flatten_containers": cfg.FlattenContainers = ParseBool(rest); break;
            case "tiling_drag":
                cfg.TilingDrag = rest.Trim().ToLowerInvariant() is not ("off" or "no" or "false" or "disable");
                break;
            case "remember_layout": cfg.RememberLayout = ParseBool(rest); break;
            case "autotiling": cfg.Autotiling = ParseBool(rest); break;
            case "osd": cfg.Osd = ParseBool(rest); break;
            case "welcome": cfg.Welcome = ParseBool(rest); break;
            case "group_lines":
                cfg.GroupLines = rest.Trim().ToLowerInvariant() switch
                {
                    "always" => "always",
                    "on" or "yes" or "true" or "1" or "enable" => "on",
                    "off" or "no" or "false" or "0" or "disable" => "off",
                    _ => throw new FormatException("group_lines must be on, always or off"),
                };
                break;
            case "group_lines_color":
                cfg.GroupLinesColor = ParseColor(rest.Trim());
                break;
            case "inactive_opacity":
                ParseInactiveOpacity(cfg, rest);
                break;
            case "opacity_ignore":
            {
                var (crit, _) = SplitCriteria(rest);
                if (crit == null) throw new FormatException("expected: opacity_ignore [criteria]");
                cfg.OpacityIgnore.Add(crit);
                break;
            }

            default:
                if (IgnoredDirectives.Contains(directive) || directive.StartsWith("client.", StringComparison.Ordinal))
                {
                    if (line.EndsWith('{')) st.Stack.Push((Block.Skip, ""));
                    break;
                }
                if (line.EndsWith('{')) st.Stack.Push((Block.Skip, ""));
                throw new FormatException($"unknown directive '{word}'");
        }
    }

    /// <summary>inactive_opacity off | &lt;last used %&gt; [&lt;minimum %&gt;]</summary>
    private static void ParseInactiveOpacity(MozaicConfig cfg, string rest)
    {
        var parts = rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.TrimEnd('%')).ToArray();
        if (parts.Length == 1 && parts[0].ToLowerInvariant() is "off" or "no" or "false" or "disable" or "100")
        {
            cfg.InactiveOpacity = null;
            return;
        }
        if (parts.Length is < 1 or > 2 ||
            !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int first) ||
            (parts.Length == 2 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
            throw new FormatException("expected: inactive_opacity off | <percent> [<minimum percent>]");
        int min = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : first;
        if (first is < 10 or > 100 || min is < 10 or > 100) throw new FormatException("inactive_opacity: percentages must be between 10 and 100");
        cfg.InactiveOpacity = first;
        cfg.InactiveOpacityMin = Math.Min(min, first);
    }

    /// <summary>
    /// The shortcut cheat sheet is bound to $mod+` and $mod+Shift+` unless the config binds those keys or uses
    /// the cheatsheet command itself, so that configs written before the feature existed get it too.
    /// </summary>
    private static void AddImplicitBindings(State st)
    {
        var cfg = st.Cfg;
        if (cfg.Bindings.Any(b => b.Command.TrimStart().StartsWith("cheatsheet", StringComparison.OrdinalIgnoreCase))) return;
        var mod = cfg.Variables.TryGetValue("$mod", out var m) ? m : "Alt";
        foreach (var text in new[] { mod + "+grave", mod + "+Shift+grave" })
        {
            if (!KeyNames.TryParseCombo(text, st.CharToVk, false, out var combo, out _)) continue;
            if (cfg.Bindings.Any(b => b.Mode == "default" && b.Combo == combo)) continue;
            cfg.Bindings.Add(new Binding("default", combo, "cheatsheet", false, Implicit: true));
        }
    }

    private static void ParseBinding(State st, string mode, string rest, bool isCode)
    {
        var s = rest.Trim();
        bool release = false;
        while (s.StartsWith("--", StringComparison.Ordinal))
        {
            var (flag, r) = Head(s);
            if (flag == "--release") release = true;
            s = r;
        }
        var (comboText, command) = Head(s);
        if (comboText.Length == 0 || command.Length == 0) throw new FormatException("expected: bindsym <keys> <command>");
        if (!KeyNames.TryParseCombo(comboText, st.CharToVk, isCode, out var combo, out var err))
            throw new FormatException(err!);
        CommandParser.Parse(command); // validate syntax early
        st.Cfg.Bindings.RemoveAll(b => b.Mode == mode && b.Combo == combo);
        st.Cfg.Bindings.Add(new Binding(mode, combo, command, release));

        // Remember workspace names in binding order so new outputs get sensible defaults.
        var toks = CommandParser.Tokenize(command);
        int wi = toks.FindIndex(t => t == "workspace");
        if (wi == 0 && toks.Count >= 2)
        {
            string name = toks[1] == "number" && toks.Count >= 3 ? toks[2] : toks[1];
            if (name is not ("next" or "prev" or "next_on_output" or "prev_on_output" or "back_and_forth") &&
                !name.StartsWith("--", StringComparison.Ordinal) && !st.Cfg.WorkspaceOrder.Contains(name))
                st.Cfg.WorkspaceOrder.Add(name);
        }
    }

    private static string StripExecFlags(string rest)
    {
        var s = rest.Trim();
        while (s.StartsWith("--", StringComparison.Ordinal))
        {
            var (_, r) = Head(s);
            s = r;
        }
        return s;
    }

    public static FontSpec ParseFont(string spec)
    {
        var s = spec.Trim();
        foreach (var prefix in new[] { "pango:", "xft:" })
            if (s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) s = s[prefix.Length..].Trim();
        if (s.StartsWith('-')) return FontSpec.Default; // X core font names are meaningless on Windows
        double size = FontSpec.Default.Size;
        int sp = s.LastIndexOf(' ');
        if (sp > 0 && double.TryParse(s[(sp + 1)..].TrimEnd('p', 'x'), NumberStyles.Float, CultureInfo.InvariantCulture, out var sz))
        {
            size = sz;
            s = s[..sp].Trim();
        }
        var family = s.Split(',')[0].Trim();
        // Drop pango style words ("Bold", "Italic") that GDI does not accept as part of the face name.
        foreach (var w in new[] { " Bold", " Italic", " Regular", " Medium", " Light" })
            if (family.EndsWith(w, StringComparison.OrdinalIgnoreCase)) family = family[..^w.Length];
        family = family.ToLowerInvariant() switch
        {
            "" or "sans" or "sans-serif" or "sans serif" => "Segoe UI",
            "monospace" or "mono" => "Consolas",
            "serif" => "Georgia",
            _ => family,
        };
        return new FontSpec(family, Math.Clamp(size, 5, 48));
    }

    private static int ParsePx(string s)
    {
        s = s.Trim();
        if (s.EndsWith("px", StringComparison.OrdinalIgnoreCase)) s = s[..^2];
        if (!int.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int v))
            throw new FormatException($"expected a number of pixels, got '{s}'");
        return v;
    }

    private static void ParseGaps(MozaicConfig cfg, string rest)
    {
        var toks = CommandParser.Tokenize(rest);
        if (toks.Count < 2) throw new FormatException("expected: gaps inner|outer|horizontal|vertical|top|right|bottom|left <px>");
        int v = ParsePx(toks[^1]);
        switch (toks[0].ToLowerInvariant())
        {
            case "inner": cfg.GapsInner = Math.Max(0, v); break;
            case "outer": cfg.GapsTop = cfg.GapsRight = cfg.GapsBottom = cfg.GapsLeft = v; break;
            case "horizontal": cfg.GapsLeft = cfg.GapsRight = v; break;
            case "vertical": cfg.GapsTop = cfg.GapsBottom = v; break;
            case "top": cfg.GapsTop = v; break;
            case "right": cfg.GapsRight = v; break;
            case "bottom": cfg.GapsBottom = v; break;
            case "left": cfg.GapsLeft = v; break;
            default: throw new FormatException($"unknown gaps type '{toks[0]}'");
        }
    }

    public static int ParseBorder(string rest)
    {
        var toks = CommandParser.Tokenize(rest);
        if (toks.Count == 0) throw new FormatException("expected: default_border normal|pixel [n]|none");
        return toks[0].ToLowerInvariant() switch
        {
            "pixel" => toks.Count > 1 ? Math.Max(0, ParsePx(toks[1])) : 1,
            // Windows apps draw their own title bars, so "normal" only keeps the (DWM) border.
            "normal" => 0,
            "none" => 0,
            _ => throw new FormatException($"unknown border style '{toks[0]}'"),
        };
    }

    private static void ParseWorkspaceDirective(MozaicConfig cfg, string rest)
    {
        var toks = CommandParser.Tokenize(rest);
        int oi = toks.FindIndex(t => t.Equals("output", StringComparison.OrdinalIgnoreCase));
        if (oi < 0)
        {
            if (toks.Any(t => t.Equals("gaps", StringComparison.OrdinalIgnoreCase))) return; // per-workspace gaps: ignored
            throw new FormatException("expected: workspace <name> output <output>...");
        }
        if (oi == 0 || oi == toks.Count - 1) throw new FormatException("expected: workspace <name> output <output>...");
        string name = string.Join(' ', toks.Take(oi));
        if (toks[0] == "number" && oi == 2) name = toks[1];
        cfg.WorkspaceOutputs[name] = toks.Skip(oi + 1).ToList();
        if (!cfg.WorkspaceOrder.Contains(name)) cfg.WorkspaceOrder.Add(name);
    }

    private static (Criteria? Criteria, string Remainder) SplitCriteria(string s)
    {
        s = s.Trim();
        if (!s.StartsWith('[')) return (null, s);
        bool inQuote = false;
        for (int i = 1; i < s.Length; i++)
        {
            if (s[i] == '"' && s[i - 1] != '\\') inQuote = !inQuote;
            else if (!inQuote && s[i] == ']')
                return (Criteria.Parse(s[1..i]), s[(i + 1)..].Trim());
        }
        throw new FormatException("unterminated criteria '['");
    }

    private static void ParseAssign(MozaicConfig cfg, string rest)
    {
        var (crit, target) = SplitCriteria(rest);
        if (crit == null) throw new FormatException("expected: assign [criteria] workspace");
        var toks = CommandParser.Tokenize(target);
        if (toks.Count > 0 && toks[0] is "→" or "->") toks.RemoveAt(0);
        if (toks.Count == 0) throw new FormatException("assign needs a target workspace");
        bool toOutput = false, byNumber = false;
        if (toks[0] == "output") { toOutput = true; toks.RemoveAt(0); }
        else
        {
            if (toks[0] == "workspace") toks.RemoveAt(0);
            if (toks.Count > 0 && toks[0] == "number") { byNumber = true; toks.RemoveAt(0); }
        }
        if (toks.Count == 0) throw new FormatException("assign needs a target");
        cfg.Assigns.Add(new AssignRule(crit, string.Join(' ', toks), toOutput, byNumber));
    }

    public static uint ParseColor(string s)
    {
        s = s.Trim();
        if (!s.StartsWith('#')) throw new FormatException($"invalid color '{s}'");
        var hex = s[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        if (hex.Length == 8) hex = hex[..6];
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v))
            throw new FormatException($"invalid color '{s}'");
        return v;
    }

    private static ClientColor ParseClientColor(string rest, ClientColor current)
    {
        var toks = CommandParser.Tokenize(rest);
        if (toks.Count < 3) throw new FormatException("expected: client.<class> <border> <background> <text> [<indicator> [<child_border>]]");
        uint border = ParseColor(toks[0]), bg = ParseColor(toks[1]), text = ParseColor(toks[2]);
        uint indicator = toks.Count > 3 ? ParseColor(toks[3]) : current.Indicator;
        uint child = toks.Count > 4 ? ParseColor(toks[4]) : bg;
        return new ClientColor(border, bg, text, indicator, child);
    }

    private static WorkspaceColor ParseWsColor(string rest)
    {
        var toks = CommandParser.Tokenize(rest);
        if (toks.Count < 3) throw new FormatException("expected: <border> <background> <text>");
        return new WorkspaceColor(ParseColor(toks[0]), ParseColor(toks[1]), ParseColor(toks[2]));
    }

    private static void ParseBarLine(State st, string directive, string rest, string line)
    {
        var bar = st.Cfg.Bar!;
        switch (directive)
        {
            case "position":
                bar.Position = rest.ToLowerInvariant() == "bottom" ? "bottom" : "top";
                break;
            case "status_command":
                bar.StatusCommand = StripExecFlags(rest);
                break;
            case "font":
                bar.Font = ParseFont(rest);
                break;
            case "height":
                bar.Height = Math.Clamp(ParsePx(rest), 0, 200);
                break;
            case "workspace_buttons": bar.WorkspaceButtons = ParseBool(rest); break;
            case "workspace_icons": bar.WorkspaceIcons = ParseBool(rest); break;
            case "binding_mode_indicator": bar.BindingModeIndicator = ParseBool(rest); break;
            case "strip_workspace_numbers": bar.StripWorkspaceNumbers = ParseBool(rest); break;
            case "strip_workspace_name": bar.StripWorkspaceName = ParseBool(rest); break;
            case "show_title": bar.ShowTitle = ParseBool(rest); break;
            case "output": bar.Outputs.Add(rest.Trim()); break;
            case "mode":
                bar.Mode = rest.ToLowerInvariant() is "invisible" ? "invisible" : "dock";
                break;
            case "separator_symbol":
                bar.SeparatorSymbol = CommandParser.Tokenize(rest).FirstOrDefault() ?? rest;
                break;
            case "status_blocks":
                bar.StatusBlocks = CommandParser.Tokenize(rest).SelectMany(t => t.Split(' ', ',')).Where(t => t.Length > 0)
                    .Select(t => t.ToLowerInvariant()).ToList();
                break;
            case "datetime_format":
                bar.DateTimeFormat = CommandParser.Tokenize(rest).FirstOrDefault() ?? rest;
                break;
            case "status_interval":
                bar.StatusInterval = Math.Clamp(ParsePx(rest), 1, 3600);
                break;
            case "workspace_min_width":
                bar.WorkspaceMinWidth = Math.Max(0, ParsePx(rest));
                break;
            case "colors":
                if (rest != "{") throw new FormatException("expected: colors {");
                st.Stack.Push((Block.BarColors, ""));
                break;
            case "hidden_state": case "modifier": case "tray_output": case "tray_padding": case "id":
            case "i3bar_command": case "padding": case "bindsym": case "wheel_up_cmd": case "wheel_down_cmd":
            case "workspace_command": case "verbose": case "socket_path":
                break;
            default:
                if (line.EndsWith('{')) st.Stack.Push((Block.Skip, ""));
                throw new FormatException($"unknown bar directive '{directive}'");
        }
    }

    private static void ParseBarColor(BarColors c, string directive, string rest)
    {
        switch (directive)
        {
            case "background": c.Background = ParseColor(rest); break;
            case "statusline": c.Statusline = ParseColor(rest); break;
            case "separator": c.Separator = ParseColor(rest); break;
            case "focused_workspace": c.FocusedWorkspace = ParseWsColor(rest); break;
            case "active_workspace": c.ActiveWorkspace = ParseWsColor(rest); break;
            case "inactive_workspace": c.InactiveWorkspace = ParseWsColor(rest); break;
            case "urgent_workspace": c.UrgentWorkspace = ParseWsColor(rest); break;
            case "binding_mode": c.BindingMode = ParseWsColor(rest); break;
            case "focused_background": case "focused_statusline": case "focused_separator": break;
            default: throw new FormatException($"unknown bar color '{directive}'");
        }
    }

    private static void ParseInclude(State st, string rest, string file, int depth)
    {
        if (depth > 8) throw new FormatException("include nesting too deep");
        var pattern = Environment.ExpandEnvironmentVariables(CommandParser.Tokenize(rest).FirstOrDefault() ?? "");
        if (pattern.StartsWith('~'))
            pattern = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + pattern[1..];
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(file == "<config>" ? "." : file)) ?? ".";
        if (!Path.IsPathRooted(pattern)) pattern = Path.Combine(baseDir, pattern);
        var dir = Path.GetDirectoryName(pattern) ?? baseDir;
        var name = Path.GetFileName(pattern);
        if (!Directory.Exists(dir)) throw new FormatException($"include: directory not found: {dir}");
        var files = name.Contains('*') || name.Contains('?')
            ? Directory.GetFiles(dir, name).OrderBy(f => f, StringComparer.Ordinal).ToArray()
            : File.Exists(pattern) ? new[] { pattern } : Array.Empty<string>();
        if (files.Length == 0) throw new FormatException($"include: no file matches {pattern}");
        foreach (var f in files) ParseInto(st, File.ReadAllText(f), f, depth + 1);
    }

    public static bool ParseBool(string s) => s.Trim().ToLowerInvariant() switch
    {
        "yes" or "on" or "true" or "enable" or "enabled" or "1" => true,
        "no" or "off" or "false" or "disable" or "disabled" or "0" => false,
        _ => throw new FormatException($"expected yes/no, got '{s.Trim()}'"),
    };
}
