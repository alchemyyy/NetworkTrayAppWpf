using System.IO;
using NetworkTrayAppWPF.Services;
using Microsoft.Win32;

namespace NetworkTrayAppWPF.Utils;

/// <summary>
/// Reads/writes the Windows Add-or-Remove-Programs registry entry under HKCU (per-user) or HKLM (machine-wide),
/// so the app shows up in Settings &gt; Apps and can be uninstalled from there.
/// Display strings (Publisher, HelpLink) are skeleton placeholders; downstream apps should override them.
/// </summary>
public static class WindowsUninstallRegistry
{
    public const string SubKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + Program.ApplicationName;

    private const string DisplayName = Program.ApplicationName;
    private const string Publisher = "Unknown Publisher";
    private const string HelpLink = "";

    public enum Scope { CurrentUser, LocalMachine }

    public sealed record Entry(int? DisplayVersion, string? InstallLocation);

    public static Entry? Read(Scope scope)
    {
        try
        {
            using RegistryKey? key = OpenRoot(scope).OpenSubKey(SubKeyPath, writable: false);
            if (key == null) return null;

            int? version = null;
            if (key.GetValue("DisplayVersion") is string v && int.TryParse(v, out int parsed)) version = parsed;

            string? installLocation = key.GetValue("InstallLocation") as string;
            return new Entry(version, installLocation);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"WindowsUninstallRegistry.Read({scope}): {ex.Message}");
            return null;
        }
    }

    public static bool Write(Scope scope, string installDir, int buildNumber)
    {
        try
        {
            string installExe = Path.Combine(installDir, InstallationService.InstalledExeFileName);
            using RegistryKey key = OpenRoot(scope).CreateSubKey(SubKeyPath, writable: true);

            key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
            key.SetValue("DisplayVersion", buildNumber.ToString(), RegistryValueKind.String);
            key.SetValue("Publisher", Publisher, RegistryValueKind.String);
            key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
            key.SetValue("DisplayIcon", installExe, RegistryValueKind.String);
            // Skip HelpLink/URLInfoAbout when the skeleton placeholder is empty.
            if (!string.IsNullOrEmpty(HelpLink))
            {
                key.SetValue("HelpLink", HelpLink, RegistryValueKind.String);
                key.SetValue("URLInfoAbout", HelpLink, RegistryValueKind.String);
            }
            key.SetValue("UninstallString",
                $"\"{installExe}\" --uninstall \"{installDir}\" --scope {ScopeArg(scope)}",
                RegistryValueKind.String);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            // Best-effort EstimatedSize (in KB) so Add/Remove Programs shows a size.
            try
            {
                if (File.Exists(installExe))
                {
                    long bytes = new FileInfo(installExe).Length;
                    key.SetValue("EstimatedSize", (int)(bytes / 1024L), RegistryValueKind.DWord);
                }
            }
            catch { /* size is decorative; never block install on it */ }

            return true;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"WindowsUninstallRegistry.Write({scope}): {ex.Message}");
            return false;
        }
    }

    public static bool Remove(Scope scope)
    {
        try
        {
            using RegistryKey root = OpenRoot(scope);
            using RegistryKey? key = root.OpenSubKey(SubKeyPath);
            if (key == null) return true;
            root.DeleteSubKeyTree(SubKeyPath, throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"WindowsUninstallRegistry.Remove({scope}): {ex.Message}");
            return false;
        }
    }

    private static RegistryKey OpenRoot(Scope scope) => scope switch
    {
        Scope.CurrentUser => Registry.CurrentUser,
        Scope.LocalMachine => Registry.LocalMachine,
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };

    public static string ScopeArg(Scope scope) => scope == Scope.CurrentUser ? "user" : "system";

    public static Scope ParseScopeArg(string s) =>
        s.Equals("system", StringComparison.OrdinalIgnoreCase) ? Scope.LocalMachine : Scope.CurrentUser;
}
