using System.IO;
using System.Windows;

namespace Mozaic.Settings;

public partial class App : Application
{
    public static string ConfigPath { get; private set; } = "";
    public static string Instance { get; private set; } = "";
    public static string? StartPage { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Software rendering by default: on some GPU drivers / Windows builds WPF's hardware path shows an
        // empty white window. A settings window does not need the GPU. MOZAIC_RENDER=hardware opts back in.
        if (Environment.GetEnvironmentVariable("MOZAIC_RENDER") != "hardware")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        DispatcherUnhandledException += (_, ex) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(Path.GetTempPath(), "MozaicSettings-errors.log"), DateTime.Now + " " + ex.Exception + Environment.NewLine);
            }
            catch (IOException)
            {
            }
            MessageBox.Show(ex.Exception.Message, "Mozaic Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        string? config = null, renderGuide = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            switch (e.Args[i])
            {
                case "--config" when i + 1 < e.Args.Length: config = e.Args[++i]; break;
                case "--instance" when i + 1 < e.Args.Length: Instance = e.Args[++i]; break;
                case "--render-guide" when i + 1 < e.Args.Length: renderGuide = e.Args[++i]; break;
                case "--page" when i + 1 < e.Args.Length: StartPage = e.Args[++i]; break;
            }
        }
        MozaicClient.Instance = Instance;
        ConfigPath = config ?? DefaultConfigPath();

        if (renderGuide != null)
        {
            // Documentation helper: writes the keyboard cheat-sheet images and exits.
            GuideRenderer.RenderAll(ConfigPath, renderGuide);
            Shutdown(0);
            return;
        }

        var window = new MainWindow(ConfigPath);
        MainWindow = window;
        window.Show();
    }

    public static string DefaultConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var primary = Path.Combine(home, ".config", "mozaic", "config");
        var alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozaic", "config");
        return File.Exists(primary) || !File.Exists(alt) ? primary : alt;
    }
}
