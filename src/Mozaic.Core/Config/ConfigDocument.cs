using System.Text;
using Mozaic.Core.Commands;

namespace Mozaic.Core.Config;

/// <summary>
/// Line-based, lossless view of a config file for editors: settings, bindings and rules can be
/// read and changed while comments, ordering and everything the editor doesn't understand stay intact.
/// Block names: "" (top level), "bar", "bar.colors", "mode:&lt;name&gt;".
/// </summary>
public sealed class ConfigDocument
{
    private readonly List<string> _lines;
    private readonly string _newline;

    public ConfigDocument(string text)
    {
        _newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        _lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (_lines.Count > 1 && _lines[^1].Length == 0) _lines.RemoveAt(_lines.Count - 1);
    }

    public string Text => string.Join(_newline, _lines) + _newline;
    public IReadOnlyList<string> Lines => _lines;

    public readonly record struct LineInfo(int Index, string Block, string Trimmed, IReadOnlyList<string> Tokens);

    public sealed record BindingLine(int Line, string Mode, string Keys, string Command, bool Release, bool IsCode);

    public sealed record RuleLine(int Line, string Kind, string Criteria, string Action);

    public sealed record ExecLine(int Line, bool Always, string Command);

    // =====================================================================================
    // Scanning
    // =====================================================================================

    public List<LineInfo> Scan()
    {
        var result = new List<LineInfo>();
        var stack = new Stack<string>();
        for (int i = 0; i < _lines.Count; i++)
        {
            var t = _lines[i].Trim();
            string block = stack.Count > 0 ? stack.Peek() : "";
            if (t.Length == 0 || t[0] == '#')
            {
                result.Add(new LineInfo(i, block, t, Array.Empty<string>()));
                continue;
            }
            if (t == "}")
            {
                result.Add(new LineInfo(i, block, t, new[] { "}" }));
                if (stack.Count > 0) stack.Pop();
                continue;
            }
            var tokens = CommandParser.Tokenize(t);
            result.Add(new LineInfo(i, block, t, tokens));
            if (t.EndsWith('{'))
            {
                string first = tokens.Count > 0 ? tokens[0] : "";
                string name = first switch
                {
                    "bar" => "bar",
                    "colors" when block == "bar" => "bar.colors",
                    "mode" => "mode:" + (tokens.FirstOrDefault(x => x != "mode" && !x.StartsWith("--", StringComparison.Ordinal) && x != "{") ?? ""),
                    _ => "?" + first,
                };
                stack.Push(name);
            }
        }
        return result;
    }

    private static bool KeyMatches(IReadOnlyList<string> tokens, string[] key)
    {
        if (tokens.Count < key.Length) return false;
        for (int i = 0; i < key.Length; i++)
            if (!string.Equals(tokens[i], key[i], StringComparison.Ordinal)) return false;
        return true;
    }

    private static string[] SplitKey(string key) => key.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The raw value of the last (= effective) line starting with <paramref name="key"/> in the block.</summary>
    public string? Get(string key, string block = "")
    {
        var k = SplitKey(key);
        string? value = null;
        foreach (var li in Scan())
        {
            if (li.Block != block || li.Tokens.Count == 0 || !KeyMatches(li.Tokens, k)) continue;
            value = RestAfterTokens(li.Trimmed, k.Length);
        }
        return value;
    }

    /// <summary>Text after the first <paramref name="count"/> whitespace-separated tokens (quotes preserved).</summary>
    private static string RestAfterTokens(string line, int count)
    {
        int i = 0;
        for (int n = 0; n < count; n++)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i < line.Length && line[i] == '"')
            {
                i++;
                while (i < line.Length && line[i] != '"') i++;
                if (i < line.Length) i++;
            }
            else
            {
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            }
        }
        return line[i..].Trim();
    }

    /// <summary>Sets (or with null removes) a directive, editing the existing line in place when possible.</summary>
    public void Set(string key, string? value, string block = "")
    {
        var k = SplitKey(key);
        var lines = Scan();
        int last = -1;
        foreach (var li in lines)
            if (li.Block == block && li.Tokens.Count > 0 && KeyMatches(li.Tokens, k)) last = li.Index;

        if (value == null)
        {
            if (last >= 0) _lines.RemoveAt(last);
            return;
        }
        if (last >= 0)
        {
            _lines[last] = Indent(_lines[last]) + key + (value.Length > 0 ? " " + value : "");
            return;
        }
        Insert(block, key + (value.Length > 0 ? " " + value : ""), k[0]);
    }

    private static string Indent(string line) => line[..(line.Length - line.TrimStart().Length)];

    /// <summary>Inserts a new line into a block next to related lines (creating the block if needed).</summary>
    private void Insert(string block, string text, string family)
    {
        var lines = Scan();
        int afterFamily = -1;
        foreach (var li in lines)
            if (li.Block == block && li.Tokens.Count > 0 && li.Tokens[0] == family) afterFamily = li.Index;
        if (afterFamily >= 0)
        {
            _lines.Insert(afterFamily + 1, Indent(_lines[afterFamily]) + text);
            return;
        }

        if (block.Length == 0)
        {
            // After the last plain setting (before bindings, modes and the bar), or after the header comment.
            var nonSettings = new HashSet<string>
            {
                "bindsym", "bindcode", "mode", "bar", "exec", "exec_always", "for_window", "assign", "no_focus",
                "ignore", "workspace", "include", "}",
            };
            int lastSetting = -1;
            foreach (var li in lines)
                if (li.Block.Length == 0 && li.Tokens.Count > 0 && !nonSettings.Contains(li.Tokens[0])) lastSetting = li.Index;
            int at = lastSetting >= 0 ? lastSetting + 1 : FirstContentLine();
            _lines.Insert(at, text);
            return;
        }

        var (start, end) = FindBlock(block);
        if (start < 0)
        {
            if (block == "bar.colors")
            {
                Insert("bar", "colors {", "colors");
                (start, end) = FindBlock(block);
                _lines.Insert(start + 1, "    }");
                (start, end) = FindBlock(block);
            }
            else
            {
                string header = block == "bar" ? "bar {" : block.StartsWith("mode:", StringComparison.Ordinal) ? $"mode \"{block[5..]}\" {{" : block + " {";
                if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
                _lines.Add(header);
                _lines.Add("}");
                (start, end) = FindBlock(block);
            }
        }
        string indent = new(' ', 4 * BlockDepth(block));
        _lines.Insert(end, indent + text);
    }

    private static int BlockDepth(string block) => block == "bar.colors" ? 2 : 1;

    private int FirstContentLine()
    {
        int i = 0;
        while (i < _lines.Count && (_lines[i].Trim().Length == 0 || _lines[i].TrimStart().StartsWith('#'))) i++;
        return i;
    }

    /// <summary>Line indexes of a block's opening line and its closing brace (-1 if absent).</summary>
    public (int Start, int End) FindBlock(string block)
    {
        var lines = Scan();
        for (int i = 0; i < lines.Count; i++)
        {
            var li = lines[i];
            if (!li.Trimmed.EndsWith('{')) continue;
            // The opening line belongs to the parent block; the next line (if any) to the block itself.
            if (i + 1 < lines.Count && lines[i + 1].Block == block && li.Block != block)
            {
                for (int j = i + 1; j < lines.Count; j++)
                    if (lines[j].Trimmed == "}" && lines[j].Block == block) return (i, j);
            }
        }
        return (-1, -1);
    }

    public bool HasBlock(string block) => FindBlock(block).Start >= 0;

    public void RemoveBlock(string block)
    {
        var (s, e) = FindBlock(block);
        if (s < 0) return;
        _lines.RemoveRange(s, e - s + 1);
    }

    // =====================================================================================
    // Variables
    // =====================================================================================

    public string? GetVariable(string name) => Unquote(Get("set " + name));

    public void SetVariable(string name, string value) => Set("set " + name, QuoteIfNeeded(value));

    public Dictionary<string, string> Variables()
    {
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var li in Scan())
        {
            if (li.Block.Length != 0 || li.Tokens.Count < 3 || li.Tokens[0] != "set" || !li.Tokens[1].StartsWith('$')) continue;
            vars[li.Tokens[1]] = Resolve(RestAfterTokens(li.Trimmed, 2), vars);
        }
        return vars;
    }

    public string Resolve(string s) => Resolve(s, Variables());

    private static string Resolve(string s, Dictionary<string, string> vars)
    {
        if (s.IndexOf('$') < 0) return s;
        foreach (var kv in vars.OrderByDescending(kv => kv.Key.Length))
            s = s.Replace(kv.Key, kv.Value, StringComparison.Ordinal);
        return s;
    }

    public static string? Unquote(string? s)
    {
        if (s == null) return null;
        s = s.Trim();
        return s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s[1..^1].Replace("\\\"", "\"") : s;
    }

    public static string QuoteIfNeeded(string s) =>
        s.Length == 0 || s.Any(c => char.IsWhiteSpace(c) || c is ',' or ';' or '"' or '#') ? "\"" + s.Replace("\"", "\\\"") + "\"" : s;

    // =====================================================================================
    // Bindings
    // =====================================================================================

    public List<string> ModeNames()
    {
        var modes = new List<string> { "default" };
        foreach (var li in Scan())
            if (li.Block.StartsWith("mode:", StringComparison.Ordinal) && !modes.Contains(li.Block[5..])) modes.Add(li.Block[5..]);
        return modes;
    }

    public List<BindingLine> Bindings()
    {
        var list = new List<BindingLine>();
        foreach (var li in Scan())
        {
            if (li.Tokens.Count < 3 || li.Tokens[0] is not ("bindsym" or "bindcode")) continue;
            string mode = li.Block.StartsWith("mode:", StringComparison.Ordinal) ? li.Block[5..] : li.Block.Length == 0 ? "default" : "";
            if (mode.Length == 0) continue;
            int idx = 1;
            bool release = false;
            while (idx < li.Tokens.Count && li.Tokens[idx].StartsWith("--", StringComparison.Ordinal))
            {
                if (li.Tokens[idx] == "--release") release = true;
                idx++;
            }
            if (idx >= li.Tokens.Count) continue;
            string keys = li.Tokens[idx];
            string command = RestAfterTokens(li.Trimmed, idx + 1);
            list.Add(new BindingLine(li.Index, mode, keys, command, release, li.Tokens[0] == "bindcode"));
        }
        return list;
    }

    public static string FormatBinding(string keys, string command, bool release, bool isCode = false) =>
        (isCode ? "bindcode " : "bindsym ") + (release ? "--release " : "") + keys + " " + command.Trim();

    public void AddBinding(string mode, string keys, string command, bool release)
    {
        var text = FormatBinding(keys, command, release);
        if (mode == "default")
        {
            var existing = Bindings().Where(b => b.Mode == "default").ToList();
            if (existing.Count > 0)
            {
                int at = existing[^1].Line;
                _lines.Insert(at + 1, Indent(_lines[at]) + text);
            }
            else
            {
                Insert("", text, "bindsym");
            }
            return;
        }
        Insert("mode:" + mode, text, "bindsym");
    }

    public void ReplaceLine(int line, string text)
    {
        if (line < 0 || line >= _lines.Count) return;
        _lines[line] = Indent(_lines[line]) + text;
    }

    public void RemoveLine(int line)
    {
        if (line >= 0 && line < _lines.Count) _lines.RemoveAt(line);
    }

    // =====================================================================================
    // Rules, exec, workspaces
    // =====================================================================================

    private static readonly string[] RuleKinds = { "for_window", "assign", "ignore", "no_focus" };

    public List<RuleLine> Rules()
    {
        var list = new List<RuleLine>();
        foreach (var li in Scan())
        {
            if (li.Block.Length != 0 || li.Tokens.Count < 2 || Array.IndexOf(RuleKinds, li.Tokens[0]) < 0) continue;
            var rest = li.Trimmed[li.Tokens[0].Length..].Trim();
            if (!rest.StartsWith('[')) continue;
            int close = FindCriteriaEnd(rest);
            if (close < 0) continue;
            list.Add(new RuleLine(li.Index, li.Tokens[0], rest[1..close], rest[(close + 1)..].Trim()));
        }
        return list;
    }

    private static int FindCriteriaEnd(string s)
    {
        char quote = '\0';
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (quote != '\0') { if (c == quote && s[i - 1] != '\\') quote = '\0'; }
            else if (c is '"' or '\'' && s[i - 1] == '=') quote = c;
            else if (c == ']') return i;
        }
        return -1;
    }

    public static string FormatRule(string kind, string criteria, string action) =>
        $"{kind} [{criteria.Trim()}]" + (action.Trim().Length > 0 ? " " + action.Trim() : "");

    public void AddRule(string kind, string criteria, string action)
    {
        var text = FormatRule(kind, criteria, action);
        var rules = Rules();
        if (rules.Count > 0)
        {
            _lines.Insert(rules[^1].Line + 1, text);
            return;
        }
        var firstBinding = Bindings().FirstOrDefault(b => b.Mode == "default");
        if (firstBinding != null) _lines.Insert(firstBinding.Line, text);
        else Insert("", text, kind);
    }

    public List<ExecLine> Execs()
    {
        var list = new List<ExecLine>();
        foreach (var li in Scan())
        {
            if (li.Block.Length != 0 || li.Tokens.Count < 2 || li.Tokens[0] is not ("exec" or "exec_always")) continue;
            var rest = li.Trimmed[li.Tokens[0].Length..].Trim();
            while (rest.StartsWith("--", StringComparison.Ordinal))
            {
                int sp = rest.IndexOf(' ');
                rest = sp < 0 ? "" : rest[(sp + 1)..].TrimStart();
            }
            list.Add(new ExecLine(li.Index, li.Tokens[0] == "exec_always", rest));
        }
        return list;
    }

    public void AddExec(bool always, string command)
    {
        var text = (always ? "exec_always " : "exec ") + command.Trim();
        var execs = Execs();
        if (execs.Count > 0) _lines.Insert(execs[^1].Line + 1, text);
        else
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
            _lines.Add(text);
        }
    }

    public List<(int Line, string Workspace, string Outputs)> WorkspaceOutputs()
    {
        var list = new List<(int, string, string)>();
        foreach (var li in Scan())
        {
            if (li.Block.Length != 0 || li.Tokens.Count < 4 || li.Tokens[0] != "workspace") continue;
            int oi = li.Tokens.ToList().IndexOf("output");
            if (oi < 2) continue;
            var ws = string.Join(' ', li.Tokens.Skip(1).Take(oi - 1));
            if (ws.StartsWith("number ", StringComparison.Ordinal)) ws = ws[7..];
            list.Add((li.Index, ws, string.Join(' ', li.Tokens.Skip(oi + 1))));
        }
        return list;
    }

    public void SetWorkspaceOutput(string workspace, string? output)
    {
        var existing = WorkspaceOutputs().FirstOrDefault(w => w.Workspace == workspace);
        string text = $"workspace {QuoteIfNeeded(workspace)} output {output}";
        if (existing.Workspace != null)
        {
            if (output == null) RemoveLine(existing.Line);
            else ReplaceLine(existing.Line, text);
            return;
        }
        if (output == null) return;
        var all = WorkspaceOutputs();
        if (all.Count > 0) _lines.Insert(all[^1].Line + 1, text);
        else Insert("", text, "workspace");
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (var l in _lines) sb.AppendLine(l);
        return sb.ToString();
    }
}
