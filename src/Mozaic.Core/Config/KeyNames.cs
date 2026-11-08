using System.Globalization;

namespace Mozaic.Core.Config;

[Flags]
public enum Modifiers
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
    Win = 8,
}

public readonly record struct KeyCombo(Modifiers Modifiers, int Vk)
{
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(Modifiers.Win)) parts.Add("Win");
        if (Modifiers.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        parts.Add(KeyNames.NameOf(Vk));
        return string.Join('+', parts);
    }
}

/// <summary>Maps i3/X11 key names (keysyms) to Windows virtual-key codes.</summary>
public static class KeyNames
{
    private static readonly Dictionary<string, int> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Return"] = 0x0D, ["Enter"] = 0x0D, ["KP_Enter"] = 0x0D,
        ["space"] = 0x20, ["Tab"] = 0x09, ["Escape"] = 0x1B, ["Esc"] = 0x1B,
        ["BackSpace"] = 0x08, ["Delete"] = 0x2E, ["Del"] = 0x2E, ["Insert"] = 0x2D,
        ["Home"] = 0x24, ["End"] = 0x23, ["Prior"] = 0x21, ["Page_Up"] = 0x21, ["PageUp"] = 0x21,
        ["Next"] = 0x22, ["Page_Down"] = 0x22, ["PageDown"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["Print"] = 0x2C, ["Pause"] = 0x13, ["Scroll_Lock"] = 0x91, ["Caps_Lock"] = 0x14,
        ["Num_Lock"] = 0x90, ["Menu"] = 0x5D, ["Apps"] = 0x5D,
        ["KP_0"] = 0x60, ["KP_1"] = 0x61, ["KP_2"] = 0x62, ["KP_3"] = 0x63, ["KP_4"] = 0x64,
        ["KP_5"] = 0x65, ["KP_6"] = 0x66, ["KP_7"] = 0x67, ["KP_8"] = 0x68, ["KP_9"] = 0x69,
        ["KP_Insert"] = 0x60, ["KP_End"] = 0x61, ["KP_Down"] = 0x62, ["KP_Next"] = 0x63, ["KP_Left"] = 0x64,
        ["KP_Begin"] = 0x65, ["KP_Right"] = 0x66, ["KP_Home"] = 0x67, ["KP_Up"] = 0x68, ["KP_Prior"] = 0x69,
        ["KP_Multiply"] = 0x6A, ["KP_Add"] = 0x6B, ["KP_Subtract"] = 0x6D, ["KP_Decimal"] = 0x6E, ["KP_Divide"] = 0x6F,
        ["XF86AudioMute"] = 0xAD, ["XF86AudioLowerVolume"] = 0xAE, ["XF86AudioRaiseVolume"] = 0xAF,
        ["XF86AudioNext"] = 0xB0, ["XF86AudioPrev"] = 0xB1, ["XF86AudioStop"] = 0xB2, ["XF86AudioPlay"] = 0xB3,
        ["XF86AudioPause"] = 0xB3, ["XF86Mail"] = 0xB4, ["XF86AudioMedia"] = 0xB5, ["XF86Explorer"] = 0xB6,
        ["XF86MyComputer"] = 0xB6, ["XF86Calculator"] = 0xB7, ["XF86Back"] = 0xA6, ["XF86Forward"] = 0xA7,
        ["XF86Reload"] = 0xA8, ["XF86Refresh"] = 0xA8, ["XF86Search"] = 0xAA, ["XF86Favorites"] = 0xAB, ["XF86HomePage"] = 0xAC,
    };

    private static readonly Dictionary<string, char> CharNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["minus"] = '-', ["equal"] = '=', ["plus"] = '+', ["comma"] = ',', ["period"] = '.',
        ["slash"] = '/', ["backslash"] = '\\', ["semicolon"] = ';', ["apostrophe"] = '\'',
        ["grave"] = '`', ["bracketleft"] = '[', ["bracketright"] = ']', ["numbersign"] = '#',
        ["less"] = '<', ["greater"] = '>', ["colon"] = ':', ["quotedbl"] = '"', ["asterisk"] = '*',
        ["section"] = '§', ["asciicircum"] = '^', ["asciitilde"] = '~', ["dead_circumflex"] = '^',
        ["dead_acute"] = '´', ["dead_grave"] = '`', ["question"] = '?', ["exclam"] = '!',
    };

    // US layout OEM keys, used when the active layout does not produce the character.
    private static readonly Dictionary<char, int> UsOem = new()
    {
        ['-'] = 0xBD, ['='] = 0xBB, ['+'] = 0xBB, [','] = 0xBC, ['.'] = 0xBE, ['/'] = 0xBF,
        ['\\'] = 0xDC, [';'] = 0xBA, ['\''] = 0xDE, ['`'] = 0xC0, ['['] = 0xDB, [']'] = 0xDD,
        ['<'] = 0xE2, ['>'] = 0xE2,
    };

    private static readonly HashSet<int> ModifierVks = new() { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };

    public static bool IsModifierVk(int vk) => ModifierVks.Contains(vk);

    public static bool TryParseModifier(string s, out Modifiers mod)
    {
        switch (s.ToLowerInvariant())
        {
            case "mod1": case "alt": case "meta": case "lalt": case "ralt": mod = Modifiers.Alt; return true;
            case "mod4": case "super": case "win": case "windows": case "hyper": case "lwin": case "rwin": mod = Modifiers.Win; return true;
            case "shift": mod = Modifiers.Shift; return true;
            case "control": case "ctrl": mod = Modifiers.Ctrl; return true;
            case "mod5": case "altgr": case "iso_level3_shift": mod = Modifiers.Ctrl | Modifiers.Alt; return true;
            case "mod2": mod = Modifiers.None; return true; // NumLock: ignored like i3 ignores lock modifiers
            default: mod = Modifiers.None; return false;
        }
    }

    public static bool TryParseKey(string s, Func<char, int?>? charToVk, out int vk)
    {
        vk = 0;
        if (s.Length == 0) return false;
        if (Named.TryGetValue(s, out vk)) return true;
        if (s.Length == 1)
        {
            char c = s[0];
            if (c is >= 'a' and <= 'z') { vk = char.ToUpperInvariant(c); return true; }
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9') { vk = c; return true; }
            return TryChar(c, charToVk, out vk);
        }
        if ((s[0] == 'F' || s[0] == 'f') && int.TryParse(s.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out int f) && f is >= 1 and <= 24)
        {
            vk = 0x70 + f - 1;
            return true;
        }
        if (CharNames.TryGetValue(s, out char ch)) return TryChar(ch, charToVk, out vk);
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk) && vk is > 0 and < 256)
            return true;
        return false;
    }

    private static bool TryChar(char c, Func<char, int?>? charToVk, out int vk)
    {
        var resolved = charToVk?.Invoke(c);
        if (resolved is > 0) { vk = resolved.Value; return true; }
        return UsOem.TryGetValue(c, out vk);
    }

    /// <summary>Parses "Mod1+Shift+q". With <paramref name="isCode"/> the key is a numeric virtual-key code (bindcode).</summary>
    public static bool TryParseCombo(string text, Func<char, int?>? charToVk, bool isCode, out KeyCombo combo, out string? error)
    {
        combo = default;
        error = null;
        string keyPart;
        string[] modParts;
        if (text.EndsWith("++", StringComparison.Ordinal))
        {
            keyPart = "+";
            modParts = text[..^2].Split('+', StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            var parts = text.Split('+');
            keyPart = parts[^1];
            modParts = parts[..^1];
        }
        var mods = Modifiers.None;
        foreach (var m in modParts)
        {
            if (!TryParseModifier(m, out var mod)) { error = $"unknown modifier '{m}'"; return false; }
            mods |= mod;
        }
        int vk;
        if (isCode)
        {
            bool ok = keyPart.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(keyPart.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out vk)
                : int.TryParse(keyPart, NumberStyles.None, CultureInfo.InvariantCulture, out vk);
            if (!ok || vk is <= 0 or > 255) { error = $"invalid key code '{keyPart}'"; return false; }
        }
        else if (!TryParseKey(keyPart, charToVk, out vk))
        {
            error = $"unknown key '{keyPart}'";
            return false;
        }
        combo = new KeyCombo(mods, vk);
        return true;
    }

    public static string NameOf(int vk)
    {
        if (vk is >= 'A' and <= 'Z' or >= '0' and <= '9') return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F).ToString(CultureInfo.InvariantCulture);
        foreach (var kv in Named) if (kv.Value == vk) return kv.Key;
        foreach (var kv in UsOem) if (kv.Value == vk)
            foreach (var cn in CharNames) if (cn.Value == kv.Key) return cn.Key;
        return "0x" + vk.ToString("X2", CultureInfo.InvariantCulture);
    }
}
