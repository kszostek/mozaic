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
        string? config = null, renderGuide = null, renderFile = null;
        int renderHeight = 1000;
        for (int i = 0; i < e.Args.Length; i++)
        {
            switch (e.Args[i])
            {
                case "--config" when i + 1 < e.Args.Length: config = e.Args[++i]; break;
                case "--instance" when i + 1 < e.Args.Length: Instance = e.Args[++i]; break;
                case "--render-guide" when i + 1 < e.Args.Length: renderGuide = e.Args[++i]; break;
                case "--page" when i + 1 < e.Args.Length: StartPage = e.Args[++i]; break;
                case "--render-to" when i + 1 < e.Args.Length: renderFile = e.Args[++i]; break;
                case "--render-height" when i + 1 < e.Args.Length: int.TryParse(e.Args[++i], out renderHeight); break;
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
        if (renderFile != null)
        {
            // Documentation / test helper: renders the window (on --page) to a PNG without showing it on screen.
            RenderWindow(new MainWindow(ConfigPath), renderFile, 1280, Math.Clamp(renderHeight, 400, 4000));
            Shutdown(0);
            return;
        }

        var window = new MainWindow(ConfigPath);
        MainWindow = window;
        window.Show();
    }

    private static void RenderWindow(Window window, string file, double width, double height)
    {
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        var background = Current.TryFindResource("ApplicationBackgroundBrush") as System.Windows.Media.Brush
                         ?? (Native.SystemUsesDarkTheme() ? System.Windows.Media.Brushes.Black : System.Windows.Media.Brushes.White);
        var host = new System.Windows.Controls.Border
        {
            Background = background, Child = root, Width = width, Height = height, DataContext = window.DataContext,
        };
        host.Measure(new Size(width, height));
        host.Arrange(new Rect(0, 0, width, height));
        host.UpdateLayout();
        // Let data binding and deferred layout finish before taking the picture.
        Current.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        host.UpdateLayout();
        var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, (int)height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bmp.Render(host);
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }

    public static string DefaultConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var primary = Path.Combine(home, ".config", "mozaic", "config");
        var alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mozaic", "config");
        return File.Exists(primary) || !File.Exists(alt) ? primary : alt;
    }
}
