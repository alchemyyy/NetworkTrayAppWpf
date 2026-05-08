using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace NetworkTrayAppWPF.Services;

/// <summary>
/// Manages the "Run on startup" autostart entry as a <c>shell:startup</c> .lnk shortcut.
/// The shortcut lives at <c>%AppData%\Microsoft\Windows\Start Menu\Programs\Startup\NetworkTrayAppWPF.lnk</c>
/// and is the user-visible mechanism Windows offers in <em>Task Manager &gt; Startup apps</em>,
/// which is why we prefer it over the legacy <c>HKCU\...\Run</c> registry value the app used to write.
/// When the toggle is flipped on, <see cref="ResolveStartupTarget"/> picks the most-permanent install
/// (ProgramFiles, then LocalAppData, then the currently running exe) so a portable build only
/// "wins" the shortcut when no installed copy exists.
/// On every launch the shortcut is validated against the known install paths
/// (<see cref="InstallationService.LocalAppDataInstallExe"/>
/// / <see cref="InstallationService.ProgramFilesInstallExe"/>),
/// and a stale target gets repaired to the running install - see <see cref="RepairShortcutIfStale"/>.
/// </summary>
public static class StartupManager
{
    private const string LegacyRunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string ShortcutPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Program.ApplicationName + ".lnk");

    public static bool GetRunOnStartup()
    {
        try
        {
            return File.Exists(ShortcutPath);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.GetRunOnStartup: {ex.Message}");
            return false;
        }
    }

    public static void SetRunOnStartup(bool enabled)
    {
        try
        {
            string lnk = ShortcutPath;
            if (enabled)
            {
                string exe = ResolveStartupTarget();
                if (string.IsNullOrEmpty(exe)) return;
                CreateShortcut(lnk, exe);
            }
            else if (File.Exists(lnk)) File.Delete(lnk);
        }
        catch (Exception ex)
        {
            // Best-effort: user will see the toggle revert if something breaks.
            WPFLog.Log($"StartupManager.SetRunOnStartup: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-resolves the priority-ordered target (ProgramFiles -&gt; LocalAppData -&gt; running exe)
    /// and rewrites the shortcut if it differs from the current target. No-op when the shortcut
    /// isn't present - we deliberately don't create one here, so a user with run-on-startup off
    /// doesn't suddenly get an autostart entry just because they ran an installer.
    /// <para>
    /// The optional <paramref name="exclude"/> parameter pins one install scope as off-limits.
    /// The uninstall flow uses this to retarget BEFORE the bat runs, so the shortcut already
    /// points away from the install dir about to be wiped (and the bat's surgical-delete pass
    /// then leaves it alone).
    /// </para>
    /// Callers: General page install / uninstall handlers, plus UninstallerWindow's pre-uninstall pass.
    /// </summary>
    public static void RetargetShortcutIfPresent(InstallScope? exclude = null)
    {
        try
        {
            string lnk = ShortcutPath;
            if (!File.Exists(lnk)) return;

            string desired = ResolveStartupTarget(exclude);
            if (string.IsNullOrEmpty(desired)) return;

            string? current = TryReadShortcutTarget(lnk);
            if (!string.IsNullOrEmpty(current)
                && string.Equals(
                    NormalizePath(current),
                    NormalizePath(desired),
                    StringComparison.OrdinalIgnoreCase))
                return;
            CreateShortcut(lnk, desired);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.RetargetShortcutIfPresent: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the path the existing shortcut points at, or <c>null</c> when no shortcut is present
    /// or its target couldn't be read. Used by the General page to show the resolved target in the
    /// "Run on startup" card description.
    /// </summary>
    public static string? GetCurrentShortcutTarget()
    {
        try
        {
            string lnk = ShortcutPath;
            if (!File.Exists(lnk)) return null;
            string? target = TryReadShortcutTarget(lnk);
            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.GetCurrentShortcutTarget: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Drops the legacy <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> entry
    /// the app used to write before switching to a shell:startup shortcut.
    /// Idempotent - safe to call on every launch.
    /// Without this, users who toggled "Run on startup" under the old build would get double autostart
    /// (one Run-key fire + one shortcut fire) after upgrading.
    /// </summary>
    public static void RemoveLegacyRunKey()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(LegacyRunKeyPath, writable: true);
            if (key?.GetValue(Program.ApplicationName) != null)
                key.DeleteValue(Program.ApplicationName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.RemoveLegacyRunKey: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates the shell:startup shortcut on every launch
    /// and repairs it if its target no longer points to a real install.
    /// The repair triggers when:
    ///   1. The shortcut exists (otherwise autostart isn't enabled and there's nothing to fix), AND
    ///   2. Its target is unreadable / missing on disk / not one of the known install paths, AND
    ///   3. The currently running process is itself an installed copy.
    /// In that case we rewrite the shortcut to point at the running install.
    /// Without this, a user who reinstalls the app in a different scope (LocalAppData -> ProgramFiles or vice versa)
    /// gets a stale shortcut that silently does nothing on next sign-in until they re-toggle the switch.
    /// </summary>
    public static void RepairShortcutIfStale()
    {
        try
        {
            string lnk = ShortcutPath;
            if (!File.Exists(lnk)) return;

            string? target = TryReadShortcutTarget(lnk);
            if (IsValidInstallationTarget(target)) return;

            string? runningInstallExe = GetRunningInstallExePathOrNull();
            if (runningInstallExe != null) CreateShortcut(lnk, runningInstallExe);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.RepairShortcutIfStale: {ex.Message}");
        }
    }

    private static bool IsValidInstallationTarget(string? targetPath)
    {
        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath)) return false;

        // Only count one of the known install locations as "valid" -
        // a stale shortcut left behind pointing at someone's old portable build under Downloads\ shouldn't pass.
        string normalized = NormalizePath(targetPath);
        return string.Equals(
                normalized, NormalizePath(InstallationService.LocalAppDataInstallExe),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                normalized, NormalizePath(InstallationService.ProgramFilesInstallExe),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetRunningInstallExePathOrNull()
    {
        try
        {
            InstallationInfo? hit = InstallationService.DetectAll()
                .FirstOrDefault(i => i is { Status: InstallStatus.CurrentlyRunning, Scope: InstallScope.LocalAppData or InstallScope.ProgramFiles });
            return hit?.InstallExePath;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.GetRunningInstallExePathOrNull: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Picks the exe the new shortcut should point at, in priority order:
    /// system-wide Program Files install, then per-user LocalAppData install, then the running exe.
    /// The two install paths are checked by file existence only - we don't require the
    /// uninstall-registry entry, so a copy left behind by a half-finished uninstall still resolves.
    /// Falling back to the running exe lets a portable / dev build self-register without first
    /// going through one of the installers.
    /// <para>
    /// When <paramref name="exclude"/> names an install scope, that scope is skipped entirely AND
    /// the running-exe fallback is suppressed if it sits at the excluded install's path. The
    /// uninstall flow passes the scope being deleted, so we never resolve back to the dir that's
    /// about to be wiped.
    /// </para>
    /// </summary>
    private static string ResolveStartupTarget(InstallScope? exclude = null)
    {
        try
        {
            if (exclude != InstallScope.ProgramFiles)
            {
                string programFiles = InstallationService.ProgramFilesInstallExe;
                if (File.Exists(programFiles)) return programFiles;
            }
            if (exclude != InstallScope.LocalAppData)
            {
                string localAppData = InstallationService.LocalAppDataInstallExe;
                if (File.Exists(localAppData)) return localAppData;
            }

            string? running = Environment.ProcessPath;
            if (string.IsNullOrEmpty(running)) return string.Empty;
            if (!running.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return string.Empty;

            // Running from the install we're being asked to exclude is the "uninstalling the live
            // install copy" case - the running exe path IS the about-to-be-wiped path, so it
            // wouldn't be a useful fallback. Bail and let the bat's surgical-delete handle the .lnk.
            if (exclude.HasValue)
            {
                string excludedExe = exclude.Value == InstallScope.ProgramFiles
                    ? InstallationService.ProgramFilesInstallExe
                    : InstallationService.LocalAppDataInstallExe;
                if (string.Equals(
                        NormalizePath(running),
                        NormalizePath(excludedExe),
                        StringComparison.OrdinalIgnoreCase))
                    return string.Empty;
            }

            return running;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.ResolveStartupTarget: {ex.Message}");
        }
        return string.Empty;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path;
        }
    }

    // -- IShellLink COM glue --------------------------------------------------------

    private static void CreateShortcut(string lnkPath, string targetExe)
    {
        object? linkObj = null;
        try
        {
            linkObj = new CShellLink();
            IShellLinkW link = (IShellLinkW)linkObj;
            link.SetPath(targetExe);
            string? workDir = Path.GetDirectoryName(targetExe);
            if (!string.IsNullOrEmpty(workDir)) link.SetWorkingDirectory(workDir);
            link.SetDescription(Program.ApplicationName);

            string? lnkDir = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(lnkDir)) Directory.CreateDirectory(lnkDir);

            // Cast through the underlying COM object: CShellLink implements both IShellLinkW
            // and IPersistFile, but the managed interfaces don't share an inheritance chain
            // so casting one to the other trips a static analyzer warning.
            IPersistFile persist = (IPersistFile)linkObj;
            persist.Save(lnkPath, true);
        }
        finally
        {
            if (linkObj != null) Marshal.FinalReleaseComObject(linkObj);
        }
    }

    private static string? TryReadShortcutTarget(string lnkPath)
    {
        object? linkObj = null;
        try
        {
            linkObj = new CShellLink();
            IShellLinkW link = (IShellLinkW)linkObj;
            // See CreateShortcut: cast through linkObj rather than 'link' to avoid the
            // "no shared base type" warning on this cross-interface cast.
            IPersistFile persist = (IPersistFile)linkObj;
            persist.Load(lnkPath, 0);

            // SFGAO_NOPATH-like behaviour: prefer raw path.
            // SLGP_RAWPATH = 4 keeps environment-variable expansion off
            // so the comparison against InstallationService paths stays apples-to-apples.
            const uint SLGP_RAWPATH = 0x0004;
            StringBuilder sb = new(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            string raw = sb.ToString();
            return string.IsNullOrEmpty(raw) ? null : raw;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartupManager.TryReadShortcutTarget: {ex.Message}");
            return null;
        }
        finally
        {
            if (linkObj != null) Marshal.FinalReleaseComObject(linkObj);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink;

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig]
        int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }
}
