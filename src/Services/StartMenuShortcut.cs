using System.IO;
using Microsoft.Win32;
using NetworkTrayAppWPF.Interop;
using NetworkTrayAppWPF.Utils;

namespace NetworkTrayAppWPF.Services;

/// <summary>
/// Reconciles this app's per-profile Start Menu Programs shortcuts with installed scopes.
/// </summary>
public static class StartMenuShortcut
{
    public const string LocalSuffix = "Local";
    public const string SystemSuffix = "System";

    private const string ProgramsRelativePath =
        @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs";

    private static string LocalAppDataExeRelativePath =>
        @"AppData\Local\" + Program.SharedRootFolderName + @"\" + InstallationService.InstalledExeFileName;

    private static string PlainFileName => $"{Program.ApplicationName}.lnk";
    private static string LocalSuffixedFileName => $"{Program.ApplicationName} ({LocalSuffix}).lnk";
    private static string SystemSuffixedFileName => $"{Program.ApplicationName} ({SystemSuffix}).lnk";

    public static void Sync(InstallScope? removingScope = null, bool allUsers = false)
    {
        try
        {
            List<InstallationInfo> infos = InstallationService.DetectAll();
            bool systemInstalled = removingScope != InstallScope.ProgramFiles
                && IsConsideredInstalled(infos, InstallScope.ProgramFiles);
            bool storeInstalled = removingScope != InstallScope.WindowsStore
                && IsConsideredInstalled(infos, InstallScope.WindowsStore);
            string systemExe = InstallationService.ProgramFilesInstallExe;

            if (!allUsers)
            {
                bool localInstalled = removingScope != InstallScope.LocalAppData
                    && IsConsideredInstalled(infos, InstallScope.LocalAppData);
                string localExe = InstallationService.LocalAppDataInstallExe;
                string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                ApplyProfile(
                    programsDir,
                    localInstalled, localExe,
                    systemInstalled, systemExe,
                    storeInstalled);
                return;
            }

            foreach (string profile in EnumerateAllProfilePaths())
            {
                try
                {
                    string profilePrograms = Path.Combine(profile, ProgramsRelativePath);
                    string profileLocalExe = Path.Combine(profile, LocalAppDataExeRelativePath);
                    bool profileLocalInstalled = removingScope != InstallScope.LocalAppData
                        && File.Exists(profileLocalExe);

                    ApplyProfile(
                        profilePrograms,
                        profileLocalInstalled, profileLocalExe,
                        systemInstalled, systemExe,
                        storeInstalled);
                }
                catch (Exception exProfile)
                {
                    WPFLog.Log($"StartMenuShortcut.Sync (profile {profile}): {exProfile.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartMenuShortcut.Sync: {ex.Message}");
        }
    }

    private static bool IsConsideredInstalled(List<InstallationInfo> infos, InstallScope scope) =>
        infos.Any(i => i.Scope == scope && i.Status is
            InstallStatus.InstalledUpToDate or
            InstallStatus.InstalledOutOfDate or
            InstallStatus.CurrentlyRunning);

    private static void ApplyProfile(
        string programsDir,
        bool localInstalled, string localExe,
        bool systemInstalled, string systemExe,
        bool storeInstalled)
    {
        int count = (localInstalled ? 1 : 0) + (systemInstalled ? 1 : 0) + (storeInstalled ? 1 : 0);
        bool useSuffixes = count > 1;

        string plainPath = Path.Combine(programsDir, PlainFileName);
        string localSuffixedPath = Path.Combine(programsDir, LocalSuffixedFileName);
        string systemSuffixedPath = Path.Combine(programsDir, SystemSuffixedFileName);

        string? plainTarget = null;
        string? localSuffixedTarget = null;
        string? systemSuffixedTarget = null;

        if (useSuffixes)
        {
            if (localInstalled) localSuffixedTarget = localExe;
            if (systemInstalled) systemSuffixedTarget = systemExe;
        }
        else if (localInstalled)
            plainTarget = localExe;
        else if (systemInstalled) plainTarget = systemExe;

        ApplyDesired(plainPath, plainTarget);
        ApplyDesired(localSuffixedPath, localSuffixedTarget);
        ApplyDesired(systemSuffixedPath, systemSuffixedTarget);
    }

    private static void ApplyDesired(string lnkPath, string? targetExe)
    {
        if (targetExe == null) TryDelete(lnkPath);
        else TryCreateShortcut(lnkPath, targetExe);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartMenuShortcut.TryDelete({path}): {ex.Message}");
        }
    }

    private static void TryCreateShortcut(string lnkPath, string targetExe)
    {
        try
        {
            string? dir = Path.GetDirectoryName(lnkPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            ShellLink.Create(lnkPath, targetExe, Program.ApplicationName);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"StartMenuShortcut.TryCreateShortcut({lnkPath}): {ex.Message}");
        }
    }

    private static IEnumerable<string> EnumerateAllProfilePaths()
    {
        string currentProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return currentProfile;

        using (RegistryKey? root = Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList"))
        {
            if (root != null)
            {
                foreach (string sid in root.GetSubKeyNames())
                {
                    if (!sid.StartsWith("S-1-5-21-", StringComparison.Ordinal)) continue;

                    using RegistryKey? sub = root.OpenSubKey(sid);
                    if (sub?.GetValue("ProfileImagePath") is not string path) continue;
                    if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) continue;

                    if (string.Equals(
                            PathNormalization.Normalize(path),
                            PathNormalization.Normalize(currentProfile),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    yield return path;
                }
            }
        }

        string? defaultProfile = GetDefaultProfilePath();
        if (defaultProfile != null) yield return defaultProfile;
    }

    private static string? GetDefaultProfilePath()
    {
        string current = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string? parent = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(parent)) return null;
        string defaultProfile = Path.Combine(parent, "Default");
        return Directory.Exists(defaultProfile) ? defaultProfile : null;
    }
}
