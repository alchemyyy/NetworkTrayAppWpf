using System.Diagnostics;

namespace NetworkTrayAppWpf;

/// <summary>
/// Application entry point that handles crash handler modes.
/// </summary>
internal static class Program
{
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

        // Normal monitored mode (or debugger attached) - run the WPF app
        App app = new();
        app.InitializeComponent();
        return app.Run();
    }
}
