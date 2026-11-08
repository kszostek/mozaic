using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Mozaic.Settings.Pages;

namespace Mozaic.Settings;

public partial class MainWindow : Window
{
    private readonly SettingsModel _model;
    private readonly Dictionary<string, FrameworkElement> _pages = new();
    private readonly DispatcherTimer _statusTimer;
    private bool _closingConfirmed;

    public MainWindow(string configPath)
    {
        InitializeComponent();
        _model = new SettingsModel(configPath);
        DataContext = _model;

        AddNav("guide", "", L.T("Guide"));
        AddNav("general", "", L.T("General"));
        AddNav("appearance", "", L.T("Appearance"));
        AddNav("bar", "", L.T("Bar"));
        AddNav("keys", "", L.T("Keyboard shortcuts"));
        AddNav("rules", "", L.T("Window rules"));
        AddNav("workspaces", "", L.T("Workspaces"));
        AddNav("startup", "", L.T("Startup"));
        AddNav("editor", "", L.T("Config file"));
        AddNav("diagnostics", "", L.T("Diagnostics"));
        Nav.SelectedIndex = 0;
        if (App.StartPage != null) Navigate(App.StartPage);

        _model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SettingsModel.HasErrors) or "") UpdateErrorLink();
            if (e.PropertyName is nameof(SettingsModel.Dirty) or "") UpdateTitle();
        };
        UpdateErrorLink();
        UpdateTitle();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += async (_, _) => await RefreshStatusAsync();
        _statusTimer.Start();
        Loaded += async (_, _) => await RefreshStatusAsync();
    }

    public bool MozaicRunning { get; private set; }
    public event Action? StatusChanged;

    private void AddNav(string key, string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, Style = (Style)FindResource("Icon"), Margin = new Thickness(0, 0, 14, 0) });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, FontSize = 14 });
        Nav.Items.Add(new ListBoxItem { Tag = key, Content = panel, Style = (Style)FindResource("NavItem") });
    }

    public void Navigate(string key)
    {
        foreach (ListBoxItem item in Nav.Items)
            if ((string)item.Tag == key) Nav.SelectedItem = item;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not ListBoxItem { Tag: string key }) return;
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                "guide" => new GuidePage(_model, this),
                "general" => new GeneralPage(),
                "appearance" => new AppearancePage(),
                "bar" => new BarPage(),
                "keys" => new KeysPage(_model),
                "rules" => new RulesPage(_model),
                "workspaces" => new WorkspacesPage(),
                "startup" => new StartupPage(_model),
                "editor" => new EditorPage(_model),
                _ => new DiagnosticsPage(_model, this),
            };
            _pages[key] = page;
        }
        PageHost.Content = page;
        if (page is IPage p) p.OnShown();
    }

    private void UpdateErrorLink() => ErrorLink.Visibility = _model.HasErrors ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateTitle() => Title = L.T("Mozaic Settings") + (_model.Dirty ? " •" : "");

    private void ErrorLink_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) => Navigate("editor");

    private async Task RefreshStatusAsync()
    {
        var v = await MozaicClient.QueryAsync("get_version");
        bool running = v is JsonElement el && el.ValueKind == JsonValueKind.Object;
        MozaicRunning = running;
        if (running)
        {
            var el2 = v!.Value;
            int windows = el2.TryGetProperty("windows", out var w) ? w.GetInt32() : 0;
            bool paused = el2.TryGetProperty("paused", out var p) && p.GetBoolean();
            if (el2.TryGetProperty("executable", out var exe) && exe.GetString() is { Length: > 0 } path) _model.MozaicExePath = path;
            StatusDot.Fill = Res(paused ? "SystemFillColorCautionBrush" : "SystemFillColorSuccessBrush", paused ? Brushes.Orange : Brushes.LimeGreen);
            StatusText.Text = paused ? L.T("Mozaic is paused") : L.F("Mozaic is running ({0} windows)", windows);
            StartButton.Visibility = Visibility.Collapsed;
            if (_model.OutputOptions.Count <= 4) await RefreshOutputsAsync();
        }
        else
        {
            StatusDot.Fill = Res("SystemFillColorCriticalBrush", Brushes.IndianRed);
            StatusText.Text = L.T("Mozaic is not running");
            StartButton.Visibility = _model.MozaicExePath != null ? Visibility.Visible : Visibility.Collapsed;
        }
        StatusChanged?.Invoke();
    }

    private Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    private async Task RefreshOutputsAsync()
    {
        var o = await MozaicClient.QueryAsync("get_outputs");
        if (o is not JsonElement arr || arr.ValueKind != JsonValueKind.Array) return;
        var names = new List<string> { "primary" };
        foreach (var e in arr.EnumerateArray())
            if (e.TryGetProperty("name", out var n) && n.GetString() is { } s) names.Add(s);
        _model.OutputOptions = names;
    }

    public void StartMozaic()
    {
        if (_model.MozaicExePath == null) return;
        try
        {
            Process.Start(new ProcessStartInfo(_model.MozaicExePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Mozaic", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.Invoke(async () => await RefreshStatusAsync()));
    }

    private void StartMozaic_Click(object sender, RoutedEventArgs e) => StartMozaic();

    private async Task<bool> SaveAsync()
    {
        try
        {
            _model.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SaveStatus.Text = L.T("Could not save: ") + ex.Message;
            return false;
        }
        var reply = await MozaicClient.SendAsync("command", "reload");
        SaveStatus.Text = reply != null ? L.T("Saved and applied.") : L.T("Saved. Mozaic is not running.");
        return true;
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveAsync();

    private void Revert_Click(object sender, RoutedEventArgs e)
    {
        _model.Load();
        SaveStatus.Text = L.T("Changes discarded.");
    }

    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_closingConfirmed || !_model.Dirty) return;
        e.Cancel = true;
        var r = MessageBox.Show(this, L.T("Save the changes before closing?"), L.T("Mozaic Settings"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return;
        if (r == MessageBoxResult.Yes && !await SaveAsync()) return;
        _closingConfirmed = true;
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        MozaicClient.Send("command", "hotkeys on", 300);
    }
}

/// <summary>Pages that want to refresh when they become visible.</summary>
public interface IPage
{
    void OnShown();
}
