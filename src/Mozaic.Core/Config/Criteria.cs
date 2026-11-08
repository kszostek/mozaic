using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Mozaic.Core.Config;

/// <summary>Facts about a managed window used for criteria matching.</summary>
public readonly record struct WindowFacts(
    int ConId,
    IntPtr Handle,
    string ClassName,
    string Title,
    string ProcessName,
    bool Floating,
    bool Dialog,
    bool Urgent,
    bool Focused,
    string? Workspace,
    IReadOnlyCollection<string> Marks);

/// <summary>
/// i3 criteria such as <c>[class="Firefox" title="^Mail"]</c>.
/// On Windows, <c>class</c> matches the window class OR the process name; <c>process</c>/<c>exe</c>/<c>instance</c>/<c>app_id</c>
/// match the process name (with or without ".exe"). Class/process matching is case-insensitive, title matching is case-sensitive (like i3).
/// </summary>
public sealed class Criteria
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public Regex? Class { get; private set; }
    public Regex? Title { get; private set; }
    public Regex? Process { get; private set; }
    public Regex? ConMark { get; private set; }
    public Regex? Workspace { get; private set; }
    public bool? Floating { get; private set; }
    public bool? Urgent { get; private set; }
    public bool? Dialog { get; private set; }
    public long? Id { get; private set; }
    public int? ConId { get; private set; }
    public bool FocusedOnly { get; private set; }
    public string Source { get; private set; } = "";

    public bool IsEmpty => Class == null && Title == null && Process == null && ConMark == null && Workspace == null &&
                           Floating == null && Urgent == null && Dialog == null && Id == null && ConId == null && !FocusedOnly;

    public static Criteria Parse(string inner)
    {
        var c = new Criteria { Source = inner.Trim() };
        int i = 0;
        while (true)
        {
            while (i < inner.Length && char.IsWhiteSpace(inner[i])) i++;
            if (i >= inner.Length) break;
            int keyStart = i;
            while (i < inner.Length && !char.IsWhiteSpace(inner[i]) && inner[i] != '=') i++;
            string key = inner[keyStart..i].ToLowerInvariant();
            string? value = null;
            if (i < inner.Length && inner[i] == '=')
            {
                i++;
                if (i < inner.Length && inner[i] is '"' or '\'')
                {
                    // Double quotes like i3; single quotes as well (easier to type in PowerShell/cmd).
                    char quote = inner[i];
                    i++;
                    var sb = new StringBuilder();
                    while (i < inner.Length && inner[i] != quote)
                    {
                        if (inner[i] == '\\' && i + 1 < inner.Length && inner[i + 1] == quote) { sb.Append(quote); i += 2; continue; }
                        sb.Append(inner[i++]);
                    }
                    if (i >= inner.Length) throw new FormatException("unterminated quote in criteria");
                    i++;
                    value = sb.ToString();
                }
                else
                {
                    int vs = i;
                    while (i < inner.Length && !char.IsWhiteSpace(inner[i])) i++;
                    value = inner[vs..i];
                }
            }
            c.Apply(key, value);
        }
        return c;
    }

    private void Apply(string key, string? value)
    {
        Regex Rx(bool ignoreCase)
        {
            if (value == null) throw new FormatException($"criterion '{key}' needs a value");
            var opts = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
            try { return new Regex(value, opts, RegexTimeout); }
            catch (ArgumentException e) { throw new FormatException($"invalid regex for {key}: {e.Message}"); }
        }

        switch (key)
        {
            case "class": Class = Rx(true); break;
            case "title": Title = Rx(false); break;
            case "process": case "exe": case "instance": case "app_id": case "app": Process = Rx(true); break;
            case "con_mark": ConMark = Rx(false); break;
            case "workspace":
                if (value == "__focused__") FocusedOnly = true; // approximate: windows on the focused workspace
                else Workspace = Rx(false);
                break;
            case "floating": Floating = true; break;
            case "tiling": Floating = false; break;
            case "floating_from": case "tiling_from": break;
            case "urgent": Urgent = true; break;
            case "window_type":
                Dialog = value?.ToLowerInvariant() switch
                {
                    "dialog" or "utility" or "toolbar" or "splash" or "popup_menu" or "dropdown_menu" or "tooltip" or "notification" => true,
                    "normal" => false,
                    _ => throw new FormatException($"unknown window_type '{value}'"),
                };
                break;
            case "id":
                if (value == null || !long.TryParse(value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value,
                        value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? NumberStyles.HexNumber : NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out long id))
                    throw new FormatException("id needs a numeric window handle");
                Id = id;
                break;
            case "con_id":
                if (value == "__focused__") FocusedOnly = true;
                else if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int cid)) ConId = cid;
                else throw new FormatException("con_id needs a number or __focused__");
                break;
            case "window_role": case "machine": break; // X11 only, ignored
            default: throw new FormatException($"unknown criterion '{key}'");
        }
    }

    private static string StripExe(string p) => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p[..^4] : p;

    private static bool MatchProcess(Regex rx, string process) =>
        process.Length > 0 && (rx.IsMatch(process) || rx.IsMatch(StripExe(process)));

    public bool Matches(in WindowFacts f)
    {
        try
        {
            if (Class != null && !Class.IsMatch(f.ClassName) && !MatchProcess(Class, f.ProcessName)) return false;
            if (Title != null && !Title.IsMatch(f.Title)) return false;
            if (Process != null && !MatchProcess(Process, f.ProcessName)) return false;
            if (ConMark != null)
            {
                bool any = false;
                foreach (var m in f.Marks) if (ConMark.IsMatch(m)) { any = true; break; }
                if (!any) return false;
            }
            if (Workspace != null && (f.Workspace == null || !Workspace.IsMatch(f.Workspace))) return false;
            if (Floating.HasValue && Floating.Value != f.Floating) return false;
            if (Urgent.HasValue && Urgent.Value != f.Urgent) return false;
            if (Dialog.HasValue && Dialog.Value != f.Dialog) return false;
            if (Id.HasValue && Id.Value != (long)f.Handle) return false;
            if (ConId.HasValue && ConId.Value != f.ConId) return false;
            if (FocusedOnly && !f.Focused) return false;
            return true;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public override string ToString() => "[" + Source + "]";
}
