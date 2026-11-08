using System.IO.Pipes;
using System.Text;
using Mozaic.Ipc;
using Mozaic.Native;
using Mozaic.Platform;

namespace Mozaic;

internal static unsafe class Program
{
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

    [STAThread]
    private static int Main(string[] args)
    {
        AppOptions opts;
        try
        {
            opts = AppOptions.Parse(args);
        }
        catch (Exception e) when (e is ArgumentException or System.Text.RegularExpressions.RegexParseException)
        {
            User32.MessageBoxW(0, e.Message + "\n\n" + AppOptions.Help, "Mozaic", C.MB_ICONERROR);
            return 2;
        }
        if (opts.ShowHelp)
        {
            User32.MessageBoxW(0, AppOptions.Help, "Mozaic", C.MB_ICONINFORMATION);
            return 0;
        }

        User32.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        if (opts.TestWindowTitle != null) return TestWindow.Run(opts.TestWindowTitle);

        Log.Init(Path.Combine(opts.DataDir, "mozaic.log"));
        Log.DebugEnabled = opts.Debug;
        WindowRules.ManageOnly = opts.ManageOnly;

        if (opts.DryRun) return DryRun.Run(opts);

        string mutexName = @"Local\Mozaic" + (opts.Instance.Length > 0 ? "-" + opts.Instance : "");
        using var mutex = new Mutex(false, mutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(opts.Restarted ? 15000 : 0);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // previous instance crashed
        }
        if (!owned && opts.Replace)
        {
            AskRunningInstanceToExit(opts);
            try { owned = mutex.WaitOne(10000); }
            catch (AbandonedMutexException) { owned = true; }
        }
        if (!owned)
        {
            User32.MessageBoxW(0, "Mozaic is already running.\n\nUse the tray icon, \"mozaic-msg exit\", or start with --replace.",
                "Mozaic", C.MB_ICONINFORMATION);
            return 1;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("unhandled exception", e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString()));
            WindowManager.Instance?.EmergencyRestore();
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };

        Log.Info($"Mozaic {typeof(Program).Assembly.GetName().Version?.ToString(3)} starting (pid {Environment.ProcessId}, " +
                 $"{(WindowQuery.SelfElevated ? "elevated" : "not elevated")})");
        var wm = new WindowManager(opts);
        try
        {
            wm.Start();
        }
        catch (Exception e)
        {
            Log.Error("startup failed", e);
            wm.EmergencyRestore();
            User32.MessageBoxW(0, "Mozaic failed to start:\n" + e.Message + "\n\nSee " + Log.FilePath, "Mozaic", C.MB_ICONERROR);
            return 1;
        }

        MSG msg;
        while (User32.GetMessageW(&msg, 0, 0, 0) > 0)
        {
            User32.TranslateMessage(&msg);
            User32.DispatchMessageW(&msg);
        }

        Log.Info("stopped");
        Log.Close();
        try { mutex.ReleaseMutex(); }
        catch (ApplicationException) { }
        return 0;
    }

    private static void AskRunningInstanceToExit(AppOptions opts)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", IpcProtocol.PipeName(opts.Instance), PipeDirection.InOut);
            pipe.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(IpcProtocol.Request("command", "exit") + "\n");
            pipe.Write(bytes);
            pipe.Flush();
        }
        catch (Exception e)
        {
            Log.Warn("could not ask the running instance to exit: " + e.Message);
        }
    }
}
