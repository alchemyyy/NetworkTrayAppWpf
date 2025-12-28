using System.Diagnostics;

namespace NetworkTrayAppWpf;

/// <summary>
/// Application entry point that handles crash handler modes.
/// </summary>
internal static class Program
{
    /// <summary>
    /// The PID of the watcher process, if running in monitored mode.
    /// </summary>
    public static int? WatcherPid { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        bool isWatcher = args.Contains("--watcher", StringComparer.OrdinalIgnoreCase);
        bool isMonitored = args.Contains("--monitored", StringComparer.OrdinalIgnoreCase);

        if (isWatcher)
        {
            // Run as crash handler/watcher - no WPF needed
            return CrashHandler.RunWatcher();
        }

        if (!isMonitored && !Debugger.IsAttached)
        {
            // First launch without flags - spawn watcher and exit
            // The watcher will launch the app with --monitored
            // Skip this when debugger is attached so we can debug directly
            CrashHandler.LaunchWatcherDetached();
            return 0;
        }

        // Parse watcher PID if provided
        WatcherPid = ParseWatcherPid(args);

        // Normal monitored mode (or debugger attached) - run the WPF app
        App app = new();
        app.InitializeComponent();
        return app.Run();
    }

    private static int? ParseWatcherPid(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--watcher-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int pid))
            {
                return pid;
            }
        }
        return null;
    }
}
