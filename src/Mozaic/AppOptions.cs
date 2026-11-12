using System.Text.RegularExpressions;

namespace Mozaic;

/// <summary>Command line options of Mozaic.exe.</summary>
internal sealed class AppOptions
{
    public string? ConfigPath { get; private set; }
    public string Instance { get; private set; } = "";
    public bool NoHotkeys { get; private set; }
    public bool NoTray { get; private set; }
    public bool Restarted { get; private set; }
    public bool Debug { get; private set; }
    public bool DryRun { get; private set; }
    public string? DumpPath { get; private set; }
    public Regex? ManageOnly { get; private set; }
    public string? TestWindowTitle { get; private set; }
    /// <summary>Test only: exiting gets stuck halfway, to check that the exit watchdog still ends the process.</summary>
    public bool TestHangOnExit { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool Replace { get; private set; }

    /// <summary>The original arguments, so that "restart" starts an identical instance.</summary>
    public IReadOnlyList<string> RawArgs { get; private set; } = Array.Empty<string>();

    public string DataDir
    {
        get
        {
            var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mozaic");
            return Instance.Length == 0 ? root : Path.Combine(root, "instances", Instance);
        }
    }

    public static AppOptions Parse(string[] args)
    {
        var o = new AppOptions { RawArgs = args.ToArray() };
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            switch (a)
            {
                case "-c": case "--config": o.ConfigPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(Next())); break;
                case "--instance": o.Instance = Next(); break;
                case "--no-hotkeys": o.NoHotkeys = true; break;
                case "--no-tray": o.NoTray = true; break;
                case "--restarted": o.Restarted = true; break;
                case "--replace": o.Replace = true; break;
                case "-d": case "--debug": o.Debug = true; break;
                case "--dry-run": o.DryRun = true; break;
                case "--dump": o.DumpPath = Next(); break;
                case "--manage-only": o.ManageOnly = new Regex(Next(), RegexOptions.CultureInvariant); break;
                case "--test-window": o.TestWindowTitle = Next(); break;
                case "--test-hang-on-exit": o.TestHangOnExit = true; break;
                case "-h": case "--help": case "/?": o.ShowHelp = true; break;
                default: throw new ArgumentException($"unknown option '{a}'");
            }
        }
        return o;
    }

    public const string Help = """
Mozaic — i3-style tiling window manager for Windows 11

Usage: Mozaic.exe [options]
  -c, --config <file>   Use this config file (default: %USERPROFILE%\.config\mozaic\config)
  -d, --debug           Verbose logging (%LOCALAPPDATA%\Mozaic\mozaic.log)
      --replace         Ask a running Mozaic to exit and take over
      --dry-run         Classify windows and compute the layout without touching anything
      --dump <file>     With --dry-run: write the computed tree as JSON
      --no-hotkeys      Do not install the keyboard hook
      --no-tray         No notification-area icon
      --instance <name> Run an isolated instance (own pipe, state and log)
      --manage-only <regex>  Only manage windows whose title matches (testing)

Control a running instance with mozaic-msg.exe, e.g.:  mozaic-msg workspace 3
""";
}
