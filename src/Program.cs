using NetworkTrayAppWPF.Services;
using NetworkTrayAppWPF.WPF;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using NetworkTrayAppWPF.Utils;
using NetworkTrayAppWPF.Visuals;

namespace NetworkTrayAppWPF;

/// <summary>
/// Application entry point that handles crash handler modes.
/// </summary>
internal static class Program
{
    /// <summary>
    /// The PID of the watcher process, if running in monitored mode.
    /// </summary>
    public static int? WatcherPID { get; private set; }

    public const string ApplicationName = "NetworkTrayAppWPF";
    public const string SharedRootFolderName = "TrayAppWPF";

    public static string LocalAppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), SharedRootFolderName);

    public static string AppLocalAppDataDirectory =>
        Path.Combine(LocalAppDataRoot, ApplicationName);

    /// <summary>
    /// True when this process was started with <c>--uninstall</c>.
    /// Set before App.OnStartup runs so the WPF startup branch can skip the tray/monitor/hotkey init
    /// and just show the uninstaller window.
    /// </summary>
    public static bool IsUninstallerMode { get; private set; }

    /// <summary>
    /// The install directory passed via <c>--uninstall &lt;dir&gt;</c>,
    /// valid when <see cref="IsUninstallerMode"/> is true.
    /// </summary>
    public static string? UninstallerInstallDir { get; private set; }

    /// <summary>
    /// The scope passed via <c>--scope user|system</c>, valid when <see cref="IsUninstallerMode"/> is true.
    /// </summary>
    public static WindowsUninstallRegistry.Scope UninstallerScope { get; private set; }
        = WindowsUninstallRegistry.Scope.CurrentUser;

    [STAThread]
    public static int Main(string[] args)
    {
        // Bring the file logger up before any branch
        // so even the short-lived admin / uninstaller / watcher entry points get a logged trail.
        // ProcessExit ensures the buffer flushes on every exit path.
        WPFLog.Initialize();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => WPFLog.Flush();

        // Privileged branches: re-entered with runas. No watcher, no WPF - just do the action and exit.
        if (TryGetArgValue(args, "--admin-action") is { } adminVerb) return RunAdminAction(adminVerb, args);

        // Headless install: --install <system|local>. Same code path as the Settings > General
        // install buttons but without WPF, so a .bat / CI can drive it. The system branch may
        // re-launch itself with --admin-action install-system to elevate.
        if (args.Contains("--install", StringComparer.OrdinalIgnoreCase))
            return RunInstall(TryGetArgValue(args, "--install"));

        // Uninstaller mode: boot WPF minimally and host UninstallerWindow as the only window.
        // On confirm the window writes a self-deleting bat to %TEMP% (via UninstallScript)
        // and shuts the app down so the bat can take over file/registry cleanup.
        if (TryGetArgValue(args, "--uninstall") is { } installDir) return RunUninstall(args, installDir);

        bool isWatcher = args.Contains("--watcher", StringComparer.OrdinalIgnoreCase);
        bool isMonitored = args.Contains("--monitored", StringComparer.OrdinalIgnoreCase);

        if (isWatcher)
        {
            // Run as crash handler/watcher - no WPF needed
            return CrashHandler.RunWatcher();
        }

        if (!isMonitored && !Debugger.IsAttached)
        {
            // First launch without flags - spawn watcher and exit.
            // The watcher will launch the app with --monitored.
            // Skip this when debugger is attached so we can debug directly.
            CrashHandler.LaunchWatcherDetached();
            return 0;
        }

        // Parse watcher PID if provided
        WatcherPID = ParseWatcherPID(args);

#if DEBUG
        // Regenerate app.ico from the current renderer on every Debug run.
        // Writes to the repo root (two levels above bin\<Configuration>)
        // where the csproj's <ApplicationIcon> picks it up on the next build.
        try
        {
            string projectRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."));
            // White glyphs read correctly on Windows' default dark taskbar / Alt-Tab surfaces.
            AppIconGenerator.Generate(
                Path.Combine(projectRoot, "app.ico"), System.Windows.Media.Colors.White);
        }
        catch
        {
            // Dev-only tool; never block app startup on failure.
        }
#endif

        // Normal monitored mode (or debugger attached) - run the WPF app
        App app = new();
        app.InitializeComponent();
        return app.Run();
    }

    private static int? ParseWatcherPID(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--watcher-pid", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(args[i + 1], out int pid))
                return pid;
        }
        return null;
    }

    /// <summary>
    /// Returns the value following <paramref name="flag"/> in <paramref name="args"/>, or null
    /// if the flag is missing or has no value.
    /// </summary>
    private static string? TryGetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return null;
    }

    private static int RunAdminAction(string verb, string[] args)
    {
        switch (verb.ToLowerInvariant())
        {
            case "install-system":
            {
                // --admin-action install-system <sourceExe> <buildNumber>
                int index = Array.FindIndex(args, a => a.Equals("--admin-action", StringComparison.OrdinalIgnoreCase));
                string sourceExe = index + 2 < args.Length ? args[index + 2] : string.Empty;
                int buildNumber = index + 3 < args.Length && int.TryParse(args[index + 3], out int bn) ? bn : 0;
                InstallResult result = InstallationService.RunAdminInstallSystem(sourceExe, buildNumber);
                return result.Success ? 0 : 1;
            }
            case "sync-startmenu":
            {
                // --admin-action sync-startmenu [--remove-scope user|system|store]
                InstallScope? removingScope = ParseRemoveScopeArg(args);
                StartMenuShortcut.Sync(removingScope: removingScope, allUsers: true);
                return 0;
            }
            default:
                WPFLog.Log($"Program.RunAdminAction: unknown verb '{verb}'");
                return 1;
        }
    }

    private static InstallScope? ParseRemoveScopeArg(string[] args)
    {
        if (TryGetArgValue(args, "--remove-scope") is not { } raw) return null;
        return raw.ToLowerInvariant() switch
        {
            "user" or "local" or "localappdata" => InstallScope.LocalAppData,
            "system" or "programfiles" => InstallScope.ProgramFiles,
            "store" or "windowsstore" => InstallScope.WindowsStore,
            _ => null,
        };
    }

    private static int RunUninstall(string[] args, string installDir)
    {
        WindowsUninstallRegistry.Scope scope = ParseScope(args);

        IsUninstallerMode = true;
        UninstallerInstallDir = installDir;
        UninstallerScope = scope;

        App app = new();
        app.InitializeComponent();
        return app.Run();
    }

    private static WindowsUninstallRegistry.Scope ParseScope(string[] args)
    {
        if (TryGetArgValue(args, "--scope") is { } scope) return WindowsUninstallRegistry.ParseScopeArg(scope);
        return WindowsUninstallRegistry.Scope.CurrentUser;
    }

    /// <summary>
    /// Headless install entry point. Drives the same InstallationService methods as the
    /// Settings buttons. Returns 0 on success, 1 on failure, 2 on usage error.
    /// </summary>
    private static int RunInstall(string? scope)
    {
        if (scope is null) return PrintInstallUsage("Missing scope argument after --install");

        switch (scope.ToLowerInvariant())
        {
            case "local":
            {
                InstallResult result = InstallationService.InstallToLocalAppData();
                string msg = result.Success
                    ? $"Installed to {InstallationService.LocalAppDataInstallExecutable}"
                    : $"Local install failed: {result.ErrorMessage}";
                WriteInstallMessage(msg, error: !result.Success);
                return result.Success ? 0 : 1;
            }
            case "system":
            {
                InstallResult result = InstallationService.InstallSystemWide();
                string msg;
                if (result.Success)
                    msg = $"Installed to {InstallationService.ProgramFilesInstallExecutable}";
                else if (result.UserCancelled)
                    msg = "System install cancelled (UAC prompt declined)";
                else
                    msg = $"System install failed: {result.ErrorMessage}";
                WriteInstallMessage(msg, error: !result.Success);
                return result.Success ? 0 : 1;
            }
            default:
                return PrintInstallUsage($"Unknown scope '{scope}'");
        }
    }

    private static int PrintInstallUsage(string? reason)
    {
        string usage =
            "Usage: --install <system|local>" + Environment.NewLine +
            "  system  Install to %ProgramFiles%\\TrayAppWPF (triggers UAC)" + Environment.NewLine +
            "  local   Install to %LOCALAPPDATA%\\TrayAppWPF (no UAC)";
        string body = reason is null ? usage : $"{reason}{Environment.NewLine}{Environment.NewLine}{usage}";
        WriteInstallMessage(body, error: true);
        return 2;
    }

    // WinExe has no console at startup. AttachConsole(ATTACH_PARENT_PROCESS) reattaches stdout /
    // stderr to the cmd / PowerShell that spawned us so .bat scripts see the message. WPFLog
    // mirrors it to disk so Explorer launches (no parent console) still leave a paper trail.
    private static void WriteInstallMessage(string text, bool error)
    {
        WPFLog.Log($"Program.RunInstall: {text}");
        try
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                // Default Console writers were bound to NUL handles at WinExe startup; rebind
                // them against the freshly-attached console.
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                (error ? Console.Error : Console.Out).WriteLine(text);
            }
        }
        catch
        {
            // best-effort; WPFLog above already captured it
        }
    }

    private const int ATTACH_PARENT_PROCESS = -1;
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int dwProcessId);
}
