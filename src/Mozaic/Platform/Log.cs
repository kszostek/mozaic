using System.Globalization;
using System.Text;

namespace Mozaic.Platform;

internal static class Log
{
    private static readonly object s_lock = new();
    private static StreamWriter? s_writer;
    private static string? s_path;
    public static bool DebugEnabled { get; set; }

    public static string? FilePath => s_path;

    public static void Init(string path)
    {
        lock (s_lock)
        {
            s_path = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 4 * 1024 * 1024)
                    File.Move(path, path + ".old", overwrite: true);
            }
            catch (IOException) { }
            var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            s_writer = new StreamWriter(fs, new UTF8Encoding(false)) { AutoFlush = true };
        }
    }

    public static void Debug(string msg)
    {
        if (DebugEnabled) Write("DBG", msg);
    }

    public static void Info(string msg) => Write("INF", msg);
    public static void Warn(string msg) => Write("WRN", msg);
    public static void Error(string msg) => Write("ERR", msg);
    public static void Error(string context, Exception e) => Write("ERR", context + ": " + e);

    private static void Write(string level, string msg)
    {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) + " " + level + " " + msg;
        lock (s_lock)
        {
            try { s_writer?.WriteLine(line); }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }
    }

    public static void Close()
    {
        lock (s_lock)
        {
            s_writer?.Dispose();
            s_writer = null;
        }
    }
}
