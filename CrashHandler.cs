using NetworkTrayAppWpf.Interop;
using System.Diagnostics;
using System.IO;

namespace NetworkTrayAppWpf;

/// <summary>
/// Monitors the main application and restarts it if it crashes unexpectedly.
/// Runs in --watcher mode as a separate process.
///
/// Exit code behavior:
/// - Exit code 0: Normal exit (user clicked Exit menu) - don't restart
/// - Exit code 1: Terminated by user (taskkill, task manager) - don't restart
/// - Other exit codes: Crash or unexpected termination - restart
/// </summary>
internal static class CrashHandler
{
    private const int RestartDelayMs = 1000;
    private const int MaxRapidRestarts = 5;
    private const int RapidRestartWindowMs = 30000; // 30 seconds

    // Exit codes that should NOT trigger a restart
    private static readonly int[] UserExitCodes = [0, 1];

    /// <summary>
    /// Runs the crash handler/watcher loop. This blocks until the monitored app exits normally.
    /// </summary>
    public static int RunWatcher()
    {
        string exePath = Environment.ProcessPath ?? "";
        string? exeDir = Path.GetDirectoryName(exePath);

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            ShowError("Cannot determine executable path.");
            return 1;
        }

        // Track rapid restarts to prevent infinite loops
        Queue<long> restartTimes = new(MaxRapidRestarts);

        // Check if app is already running
        Process? childProcess = FindExistingMonitoredProcess();

        if (childProcess == null)
        {
            // Launch the application with --monitored flag
            childProcess = LaunchApplication(exePath, exeDir ?? ".");
            if (childProcess == null)
            {
                ShowError("Failed to start NetworkTrayAppWpf");
                return 1;
            }
        }

        // Watcher should use minimal memory - force collection of startup allocations
        // and trim working set since we're just waiting
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        // Main monitoring loop
        while (true)
        {
            try
            {
                childProcess.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // Process already exited or handle invalid
                break;
            }

            int exitCode = childProcess.ExitCode;
            childProcess.Dispose();
            childProcess = null;

            // Check if this was a user-initiated exit (Exit menu or taskkill)
            // Exit code 0 = graceful exit, Exit code 1 = taskkill/terminated
            if (Array.Exists(UserExitCodes, code => code == exitCode))
            {
                break;
            }

            // Unexpected exit (crash) - check for rapid restart loop
            long now = Environment.TickCount64;
            restartTimes.Enqueue(now);

            // Remove old entries outside the window
            while (restartTimes.Count > 0 && (now - restartTimes.Peek()) > RapidRestartWindowMs)
            {
                restartTimes.Dequeue();
            }

            // Check if we've had too many restarts
            if (restartTimes.Count >= MaxRapidRestarts)
            {
                ShowError(
                    "NetworkTrayAppWpf has crashed repeatedly.\n\n" +
                    "The crash handler will not attempt further restarts.\n" +
                    "Please check for issues and restart manually.");
                break;
            }

            // Wait before restarting
            Thread.Sleep(RestartDelayMs);

            // Restart the application
            childProcess = LaunchApplication(exePath, exeDir ?? ".");
            if (childProcess == null)
            {
                ShowError("Failed to restart NetworkTrayAppWpf");
                break;
            }

            // Clean up memory after restart
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }

        childProcess?.Dispose();
        return 0;
    }

    /// <summary>
    /// Launches the watcher process detached from the current process.
    /// Uses cmd.exe /c start to create a truly independent process.
    /// </summary>
    public static void LaunchWatcherDetached()
    {
        string exePath = Environment.ProcessPath ?? "";

        if (string.IsNullOrEmpty(exePath))
            return;

        // Use cmd.exe /c start to launch a truly independent process
        // The empty quotes after start are for the window title
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"\" \"{exePath}\" --watcher",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            Process.Start(startInfo);
        }
        catch
        {
            // If cmd.exe approach fails, try direct launch (will be a child process but still works)
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = "--watcher",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch
            {
                // Silently fail - app will run without crash handler
            }
        }
    }

    private static Process? LaunchApplication(string exePath, string workDir)
    {
        try
        {
            // Pass watcher PID so monitored app can exit if watcher dies
            int watcherPid = Environment.ProcessId;

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--monitored --watcher-pid {watcherPid}",
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Finds an existing monitored process (not another watcher).
    /// </summary>
    private static Process? FindExistingMonitoredProcess()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return null;

        string currentExeName = Path.GetFileNameWithoutExtension(exePath);
        int currentPid = Environment.ProcessId;

        try
        {
            foreach (Process proc in Process.GetProcessesByName(currentExeName))
            {
                if (proc.Id != currentPid)
                {
                    // Found another instance - assume it's the monitored app
                    return proc;
                }
                proc.Dispose();
            }
        }
        catch
        {
            // Ignore errors enumerating processes
        }

        return null;
    }

    private static void ShowError(string message)
    {
        // Use native MessageBox since we may not have WPF initialized
        User32.MessageBox(IntPtr.Zero, message, "NetworkTrayAppWpf Crash Handler", User32.MB_ICONERROR);
    }
}
