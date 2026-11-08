using System.Text;
using Mozaic.Core.Config;

namespace Mozaic.Core.Commands;

/// <summary>One command of a command string, e.g. <c>move left</c>, with the criteria that applied to it.</summary>
public sealed record ParsedCommand(Criteria? Criteria, IReadOnlyList<string> Tokens, string Raw)
{
    public string Verb => Tokens.Count > 0 ? Tokens[0].ToLowerInvariant() : "";

    /// <summary>The raw text after the verb (used by exec, which keeps quoting/spacing).</summary>
    public string RawArgs
    {
        get
        {
            var s = Raw.TrimStart();
            int i = 0;
            while (i < s.Length && !char.IsWhiteSpace(s[i])) i++;
            return s[i..].Trim();
        }
    }
}

public sealed class CommandParseException(string message) : Exception(message);

/// <summary>
/// Parses i3 command strings: <c>[criteria] cmd args, cmd2 args; [criteria2] cmd3</c>.
/// ',' separates commands that share criteria, ';' resets them.
/// </summary>
public static class CommandParser
{
    public static List<ParsedCommand> Parse(string text)
    {
        var result = new List<ParsedCommand>();
        Criteria? criteria = null;
        var tokens = new List<string>();
        var token = new StringBuilder();
        bool tokenStarted = false;
        int cmdStart = 0;
        int i = 0;
        bool atGroupStart = true;

        void EndToken()
        {
            if (tokenStarted) tokens.Add(token.ToString());
            token.Clear();
            tokenStarted = false;
        }

        void EndCommand(int endIndex)
        {
            EndToken();
            if (tokens.Count > 0)
                result.Add(new ParsedCommand(criteria, tokens.ToList(), text[cmdStart..endIndex].Trim()));
            tokens.Clear();
        }

        while (i < text.Length)
        {
            char c = text[i];
            if (atGroupStart && tokens.Count == 0 && !tokenStarted)
            {
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '[')
                {
                    int depth = 0;
                    char quote = '\0';
                    int start = i + 1;
                    int j = i;
                    for (; j < text.Length; j++)
                    {
                        char d = text[j];
                        bool escaped = j > 0 && text[j - 1] == '\\';
                        if (quote != '\0') { if (d == quote && !escaped) quote = '\0'; }
                        else if (d is '"' or '\'' && text[j - 1] == '=') quote = d;
                        else if (d == '[') depth++;
                        else if (d == ']' && --depth == 0) break;
                    }
                    if (j >= text.Length) throw new CommandParseException("unterminated criteria '['");
                    try { criteria = Criteria.Parse(text[start..j]); }
                    catch (FormatException e) { throw new CommandParseException(e.Message); }
                    i = j + 1;
                    atGroupStart = false;
                    cmdStart = i;
                    continue;
                }
                atGroupStart = false;
                cmdStart = i;
            }

            if (c == '"')
            {
                tokenStarted = true;
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length && (text[i + 1] == '"' || text[i + 1] == '\\'))
                    {
                        token.Append(text[i + 1]);
                        i += 2;
                        continue;
                    }
                    token.Append(text[i++]);
                }
                if (i >= text.Length) throw new CommandParseException("unterminated quote");
                i++;
                continue;
            }
            if (c == ',' || c == ';')
            {
                EndCommand(i);
                i++;
                if (c == ';') { criteria = null; atGroupStart = true; }
                cmdStart = i;
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                EndToken();
                if (tokens.Count == 0) cmdStart = i + 1;
                i++;
                continue;
            }
            tokenStarted = true;
            token.Append(c);
            i++;
        }
        EndCommand(text.Length);
        if (result.Count == 0 && criteria != null)
            result.Add(new ParsedCommand(criteria, new List<string>(), ""));
        return result;
    }

    /// <summary>Splits a line into whitespace-separated tokens, honouring double quotes.</summary>
    public static List<string> Tokenize(string text)
    {
        var list = new List<string>();
        var sb = new StringBuilder();
        bool started = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                started = true;
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                    sb.Append(text[i++]);
                }
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                if (started) { list.Add(sb.ToString()); sb.Clear(); started = false; }
                continue;
            }
            started = true;
            sb.Append(c);
        }
        if (started) list.Add(sb.ToString());
        return list;
    }

    /// <summary>Splits a Windows command line into the program and the remaining argument string.</summary>
    public static (string File, string Arguments) SplitCommandLine(string commandLine)
    {
        var s = commandLine.Trim();
        if (s.Length == 0) return ("", "");
        if (s[0] == '"')
        {
            int end = s.IndexOf('"', 1);
            if (end < 0) return (s[1..], "");
            return (s[1..end], s[(end + 1)..].Trim());
        }
        int sp = 0;
        while (sp < s.Length && !char.IsWhiteSpace(s[sp])) sp++;
        return (s[..sp], s[sp..].Trim());
    }
}
