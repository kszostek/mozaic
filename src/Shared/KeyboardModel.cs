using System.IO;
using Mozaic.Core.Commands;
using Mozaic.Core.Config;

namespace Mozaic;

// Shared by the settings app (the Guide page, drawn with WPF) and Mozaic (the hold-to-show cheat sheet, GDI).

public enum KeyCategory { Focus, Move, Workspace, MoveToWorkspace, Layout, Window, Launch, Mode, System, Other }

/// <summary>Short, human labels for commands, as printed on the keyboard picture.</summary>
public static partial class KeyboardLabels
{
    private static string FirstCommand(string command)
    {
        bool quote = false;
        for (int i = 0; i < command.Length; i++)
        {
            if (command[i] == '"') quote = !quote;
            else if (!quote && command[i] is ',' or ';') return command[..i];
        }
        return command;
    }

    private static string Arrow(string dir) => dir switch
    {
        "left" => "←",
        "right" => "→",
        "up" => "↑",
        "down" => "↓",
        _ => dir,
    };

    public static (string Label, KeyCategory Category) Describe(string command)
    {
        var first = FirstCommand(command.Trim());
        if (first.StartsWith('['))
        {
            int close = first.IndexOf(']');
            if (close > 0) first = first[(close + 1)..].Trim();
        }
        List<string> t;
        try { t = CommandParser.Tokenize(first).Select(x => x.ToLowerInvariant()).ToList(); }
        catch (Exception) { t = new List<string>(); }
        if (t.Count == 0) return ("", KeyCategory.Other);
        string a1 = t.Count > 1 ? t[1] : "", a2 = t.Count > 2 ? t[2] : "";
        string Last() => t[^1];

        switch (t[0])
        {
            case "focus":
                return a1 switch
                {
                    "left" or "right" or "up" or "down" => (L.T("focus") + " " + Arrow(a1), KeyCategory.Focus),
                    "parent" => (L.T("parent"), KeyCategory.Focus),
                    "child" => (L.T("child"), KeyCategory.Focus),
                    "mode_toggle" => (L.T("tiled ↔ floating"), KeyCategory.Focus),
                    "last" => (L.T("last window"), KeyCategory.Focus),
                    "output" => (L.T("monitor") + " " + Arrow(a2), KeyCategory.Focus),
                    "" => (L.T("focus"), KeyCategory.Focus),
                    _ => (L.T("focus") + " " + a1, KeyCategory.Focus),
                };
            case "move":
            {
                var rest = t.Skip(1).Where(x => x is not ("container" or "window" or "to" or "--no-auto-back-and-forth")).ToList();
                if (rest.Count == 0) return (L.T("move"), KeyCategory.Move);
                if (rest[0] is "left" or "right" or "up" or "down") return (L.T("move") + " " + Arrow(rest[0]), KeyCategory.Move);
                if (rest[0] == "workspace" && rest.Count > 2 && rest[1] == "to") return (L.T("ws to") + " " + Arrow(Last()), KeyCategory.Workspace);
                if (rest[0] == "workspace" && rest.Count > 1 && rest[1] == "output") return (L.T("ws to") + " " + Arrow(Last()), KeyCategory.Workspace);
                if (rest[0] == "workspace") return ("→ " + L.T("ws") + " " + WorkspaceArg(rest.Skip(1).ToList()), KeyCategory.MoveToWorkspace);
                if (rest[0] == "output") return ("→ " + L.T("monitor") + " " + Arrow(Last()), KeyCategory.MoveToWorkspace);
                if (rest[0] == "scratchpad") return ("→ " + L.T("scratchpad"), KeyCategory.Window);
                if (rest[0] is "position" or "absolute") return (L.T("center"), KeyCategory.Window);
                return (L.T("move"), KeyCategory.Move);
            }
            case "workspace":
                return (WorkspaceArg(t.Skip(1).Where(x => !x.StartsWith("--", StringComparison.Ordinal)).ToList()) switch
                {
                    "next_on_output" or "next" => L.T("next ws"),
                    "prev_on_output" or "prev" => L.T("prev ws"),
                    "back_and_forth" => L.T("ws ⇄"),
                    var w => L.T("ws") + " " + w,
                }, KeyCategory.Workspace);
            case "split":
                return (a1 is "h" or "horizontal" ? L.T("split ⇆") : a1 is "v" or "vertical" ? L.T("split ⇅") : L.T("split"), KeyCategory.Layout);
            case "layout":
                return (a1 switch
                {
                    "stacking" or "stacked" => L.T("stacked"),
                    "tabbed" => L.T("tabbed"),
                    "toggle" => a2 == "all" ? L.T("next layout") : L.T("toggle split"),
                    "splith" => L.T("split ⇆"),
                    "splitv" => L.T("split ⇅"),
                    _ => L.T("layout"),
                }, KeyCategory.Layout);
            case "balance": return (L.T("balance"), KeyCategory.Layout);
            case "fullscreen": return (L.T("full screen"), KeyCategory.Window);
            case "floating": return (L.T("floating"), KeyCategory.Window);
            case "sticky": return (L.T("sticky"), KeyCategory.Window);
            case "kill": return (L.T("close"), KeyCategory.Window);
            case "scratchpad": return (L.T("scratchpad"), KeyCategory.Window);
            case "resize":
                return ((a1, a2) switch
                {
                    ("grow", "width") => L.T("wider"),
                    ("shrink", "width") => L.T("narrower"),
                    ("grow", "height") => L.T("taller"),
                    ("shrink", "height") => L.T("shorter"),
                    _ => L.T("resize"),
                }, KeyCategory.Layout);
            case "gaps":
                return (t.Contains("plus") ? L.T("gaps +") : t.Contains("minus") ? L.T("gaps −") : L.T("gaps"), KeyCategory.Layout);
            case "exec":
            {
                var raw = first.Trim();
                raw = raw.Length > 4 ? raw[4..].Trim() : "";
                while (raw.StartsWith("--", StringComparison.Ordinal))
                {
                    int sp = raw.IndexOf(' ');
                    raw = sp < 0 ? "" : raw[(sp + 1)..].TrimStart();
                }
                var (file, _) = CommandParser.SplitCommandLine(raw.Trim('"'));
                var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                return (name switch
                {
                    "wt" or "cmd" or "powershell" or "pwsh" or "alacritty" or "wezterm" or "conhost" => L.T("terminal"),
                    "explorer" => L.T("files"),
                    "" => L.T("run"),
                    _ => name,
                }, KeyCategory.Launch);
            }
            case "launcher":
                return (a1 switch
                {
                    "windows" or "window" => L.T("windows"),
                    "commands" or "command" or "keys" => L.T("shortcuts"),
                    _ => L.T("launcher"),
                }, KeyCategory.Launch);
            case "mode":
            {
                var m = t.Skip(1).FirstOrDefault(x => !x.StartsWith("--", StringComparison.Ordinal)) ?? "";
                return (m == "default" ? L.T("exit mode") : L.F("{0} mode", m), KeyCategory.Mode);
            }
            case "reload": return (L.T("reload"), KeyCategory.System);
            case "restart": return (L.T("restart"), KeyCategory.System);
            case "exit": return (L.T("exit"), KeyCategory.System);
            case "confirm": return (t.Contains("exit") ? L.T("exit") : L.T("confirm"), KeyCategory.System);
            case "settings": return (L.T("settings"), KeyCategory.System);
            case "pause": return (L.T("pause"), KeyCategory.System);
            case "cheatsheet": return (L.T("cheat sheet"), KeyCategory.System);
            default: return (t[0], KeyCategory.Other);
        }
    }

    private static string WorkspaceArg(List<string> args)
    {
        if (args.Count == 0) return "";
        if (args[0] == "number" && args.Count > 1) return args[1];
        return string.Join(' ', args);
    }

    public static string NameOf(KeyCategory c) => L.T(c switch
    {
        KeyCategory.Focus => "Focus",
        KeyCategory.Move => "Move window",
        KeyCategory.Workspace => "Workspaces",
        KeyCategory.MoveToWorkspace => "Move to workspace",
        KeyCategory.Layout => "Layout & size",
        KeyCategory.Window => "Window",
        KeyCategory.Launch => "Launch",
        KeyCategory.Mode => "Modes",
        KeyCategory.System => "Mozaic",
        _ => "Other",
    });

    /// <summary>Category color as 0xRRGGBB.</summary>
    public static uint Rgb(KeyCategory c) => c switch
    {
        KeyCategory.Focus => 0x3B82F6,
        KeyCategory.Move => 0xF59E0B,
        KeyCategory.Workspace => 0x22C55E,
        KeyCategory.MoveToWorkspace => 0x14B8A6,
        KeyCategory.Layout => 0xA855F7,
        KeyCategory.Window => 0xEF4444,
        KeyCategory.Launch => 0xEC4899,
        KeyCategory.Mode => 0xEAB308,
        KeyCategory.System => 0x64748B,
        _ => 0x94A3B8,
    };
}

/// <summary>A US ANSI keyboard in key units (1 unit = one letter key), for the shortcut pictures.</summary>
public static class KeyboardLayout
{
    public sealed record KeyCap(string Label, int Vk, double X, double Y, double W);

    public const double UnitsW = 18.75, UnitsH = 6.5;

    public static IReadOnlyList<KeyCap> Caps { get; } = Build();

    private static List<KeyCap> Build()
    {
        var caps = new List<KeyCap>();
        void Row(double y, double x, IEnumerable<(string L, int Vk, double W)> keys)
        {
            foreach (var (label, vk, w) in keys)
            {
                caps.Add(new KeyCap(label, vk, x, y, w));
                x += w;
            }
        }
        IEnumerable<(string, int, double)> Chars(string s, int[] vks) => s.Select((c, i) => (c.ToString(), vks[i], 1.0));

        caps.Add(new KeyCap("Esc", 0x1B, 0, 0, 1));
        for (int i = 0; i < 12; i++) caps.Add(new KeyCap("F" + (i + 1), 0x70 + i, 2 + i + (i / 4) * 0.5, 0, 1));
        Row(1.25, 0, Chars("`1234567890-=", new[] { 0xC0, '1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 0xBD, 0xBB })
            .Append(("Backspace", 0x08, 2.0)));
        Row(2.25, 0, new[] { ("Tab", 0x09, 1.5) }.Concat(Chars("QWERTYUIOP[]", new[] { 'Q', 'W', 'E', 'R', 'T', 'Y', 'U', 'I', 'O', 'P', 0xDB, 0xDD }))
            .Append(("\\", 0xDC, 1.5)));
        Row(3.25, 0, new[] { ("Caps", 0x14, 1.75) }.Concat(Chars("ASDFGHJKL;'", new[] { 'A', 'S', 'D', 'F', 'G', 'H', 'J', 'K', 'L', 0xBA, 0xDE }))
            .Append(("Enter", 0x0D, 2.25)));
        Row(4.25, 0, new[] { ("Shift", 0xA0, 2.25) }.Concat(Chars("ZXCVBNM,./", new[] { 'Z', 'X', 'C', 'V', 'B', 'N', 'M', 0xBC, 0xBE, 0xBF }))
            .Append(("Shift", 0xA1, 2.75)));
        Row(5.25, 0, new[]
        {
            ("Ctrl", 0xA2, 1.25), ("Win", 0x5B, 1.25), ("Alt", 0xA4, 1.25), ("", 0x20, 6.25),
            ("AltGr", 0xA5, 1.25), ("Win", 0x5C, 1.25), ("Menu", 0x5D, 1.25), ("Ctrl", 0xA3, 1.25),
        });
        Row(1.25, 15.75, new[] { ("Ins", 0x2D, 1.0), ("Home", 0x24, 1.0), ("PgUp", 0x21, 1.0) });
        Row(2.25, 15.75, new[] { ("Del", 0x2E, 1.0), ("End", 0x23, 1.0), ("PgDn", 0x22, 1.0) });
        caps.Add(new KeyCap("↑", 0x26, 16.75, 4.25, 1));
        Row(5.25, 15.75, new[] { ("←", 0x25, 1.0), ("↓", 0x28, 1.0), ("→", 0x27, 1.0) });
        return caps;
    }

    /// <summary>The bindings of one modifier layer of a binding mode, by virtual key.</summary>
    public static Dictionary<int, Binding> BindingsFor(MozaicConfig? cfg, string mode, Modifiers mods)
    {
        var map = new Dictionary<int, Binding>();
        if (cfg == null) return map;
        foreach (var b in cfg.Bindings)
            if (b.Mode == mode && b.Combo.Modifiers == mods) map.TryAdd(b.Combo.Vk, b);
        return map;
    }

    /// <summary>True for the modifier keys that are part of the layer (drawn highlighted).</summary>
    public static bool IsLayerModifier(int vk, Modifiers mods) => vk switch
    {
        0xA0 or 0xA1 => mods.HasFlag(Modifiers.Shift),
        0xA2 or 0xA3 => mods.HasFlag(Modifiers.Ctrl),
        0xA4 => mods.HasFlag(Modifiers.Alt),
        0x5B or 0x5C => mods.HasFlag(Modifiers.Win),
        _ => false,
    };
}
