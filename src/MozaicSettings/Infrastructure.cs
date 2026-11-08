using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Markup;
using Microsoft.Win32;
using Mozaic.Ipc;

namespace Mozaic.Settings;

/// <summary>XAML: {l:T Some English text} → translated text.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string text) => Text = text;
    public string Text { get; set; } = "";
    public override object ProvideValue(IServiceProvider serviceProvider) => L.T(Text);
}

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected void OnAllPropertiesChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class CategoryBrushConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is KeyCategory c ? KeyboardLabels.BrushOf(c) : System.Windows.Media.Brushes.Transparent;

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>"" (no monitor) → "Any monitor".</summary>
public sealed class AnyOutputConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        value is string s && s.Length > 0 ? s : L.T("Any monitor");

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Talks to a running Mozaic over its named pipe (same protocol as mozaic-msg).</summary>
public static class MozaicClient
{
    public static string Instance { get; set; } = "";

    public static string? Send(string type, string payload = "", int timeoutMs = 800)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName(Instance), PipeDirection.InOut);
            pipe.Connect(timeoutMs);
            var utf8 = new UTF8Encoding(false);
            using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
            using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
            writer.WriteLine(IpcProtocol.Request(type, payload));
            return reader.ReadLine();
        }
        catch (Exception e) when (e is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static Task<string?> SendAsync(string type, string payload = "") => Task.Run(() => Send(type, payload));

    public static async Task<JsonElement?> QueryAsync(string type)
    {
        var reply = await SendAsync(type).ConfigureAwait(true);
        if (reply == null) return null;
        try { return JsonDocument.Parse(reply).RootElement; }
        catch (JsonException) { return null; }
    }

    /// <summary>Fire-and-forget command (e.g. "hotkeys off" while recording a shortcut).</summary>
    public static void Post(string command) => _ = SendAsync("command", command);
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct CHOOSECOLOR
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public int rgbResult;
        public IntPtr lpCustColors;
        public int Flags;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
    }

    [DllImport("comdlg32.dll", EntryPoint = "ChooseColorW")]
    private static extern bool ChooseColor(ref CHOOSECOLOR cc);

    private static readonly IntPtr s_custom = Marshal.AllocHGlobal(16 * 4);

    /// <summary>The Windows color dialog. Colors are 0xRRGGBB.</summary>
    public static uint? PickColor(IntPtr owner, uint rgb)
    {
        var cc = new CHOOSECOLOR
        {
            lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
            hwndOwner = owner,
            rgbResult = (int)(((rgb & 0xFF) << 16) | (rgb & 0xFF00) | ((rgb >> 16) & 0xFF)),
            lpCustColors = s_custom,
            Flags = 0x1 | 0x2 | 0x100, // RGBINIT | FULLOPEN | ANYCOLOR
        };
        if (!ChooseColor(ref cc)) return null;
        uint r = (uint)cc.rgbResult;
        return ((r & 0xFF) << 16) | (r & 0xFF00) | ((r >> 16) & 0xFF);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int L, T, R, B; }

    [DllImport("shell32.dll")]
    private static extern UIntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

    private const uint ABM_GETSTATE = 4, ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1;

    /// <summary>Windows' own "automatically hide the taskbar" setting.</summary>
    public static bool TaskbarAutoHide
    {
        get
        {
            var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() };
            return ((int)SHAppBarMessage(ABM_GETSTATE, ref d) & ABS_AUTOHIDE) != 0;
        }
        set
        {
            var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), lParam = value ? ABS_AUTOHIDE : 0 };
            SHAppBarMessage(ABM_SETSTATE, ref d);
        }
    }

    public static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string? AutostartCommand
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue("Mozaic") as string;
            }
            catch (Exception)
            {
                return null;
            }
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value == null) key.DeleteValue("Mozaic", throwOnMissingValue: false);
            else key.SetValue("Mozaic", value);
        }
    }
}
