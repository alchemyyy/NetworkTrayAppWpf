using System.Diagnostics;
using System.IO;
using System.Text;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Services;

namespace NetworkTrayAppWPF.Utils;

/// <summary>
/// Generates a self-deleting .bat in <c>%TEMP%</c> that removes the install exe,
/// the Add/Remove Programs registry entry, optionally the settings folder,
/// and the shell:startup shortcut, then spawns it detached.
/// The WPF process exits cleanly after invoking <see cref="Run"/>;
/// the watcher honours that exit and shuts down too, releasing the file lock so the bat's <c>del</c> succeeds.
/// </summary>
public static class UninstallScript
{
    /// <summary>
    /// Writes and spawns the uninstall .bat.
    /// Returns the spawned <see cref="Process"/> (with <c>EnableRaisingEvents=true</c>)
    /// so the caller can hook <c>Exited</c> for live UI refresh,
    /// or <c>null</c> if the spawn failed (incl. UAC declined).
    /// </summary>
    public static Process? Run(string installDir, WindowsUninstallRegistry.Scope regScope, bool deleteSettings)
    {
        try
        {
            string batPath = Path.Combine(
                Path.GetTempPath(),
                $"{Program.ApplicationName}-uninstall-{Guid.NewGuid():N}.bat");

            string content = BuildScript(installDir, regScope, deleteSettings);
            File.WriteAllText(batPath, content, Encoding.ASCII);

            ProcessStartInfo psi = new()
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{batPath}\"\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            // System-wide uninstall touches Program Files and HKLM. UAC fires here at the click.
            if (regScope == WindowsUninstallRegistry.Scope.LocalMachine) psi.Verb = "runas";

            Process? p = Process.Start(psi);
            if (p != null) p.EnableRaisingEvents = true;
            return p;
        }
        catch (Exception ex)
        {
            WPFLog.Log($"UninstallScript.Run: {ex}");
            return null;
        }
    }

    // Filename patterns we lay down at install time
    // (Release single-file: just the .exe;
    //  Debug-style multi-file: .exe + sibling .dll/.pdb/.runtimeconfig.json/.deps.json).
    // Anything matching these is fair game to delete even when the user opted to keep settings.
    // None of these match user-data files (settings.xml, *.log, etc.) that may share the LocalAppData install dir.
    private static readonly string[] RuntimeFilePatterns =
        ["*.exe", "*.dll", "*.pdb", "*.runtimeconfig.json", "*.deps.json"];

    private static string BuildScript(string installDir, WindowsUninstallRegistry.Scope regScope, bool deleteSettings)
    {
        string installExe = Path.Combine(installDir, InstallationService.InstalledExeFileName);
        string regKeyFullPath = (regScope == WindowsUninstallRegistry.Scope.LocalMachine ? "HKLM\\" : "HKCU\\")
            + WindowsUninstallRegistry.SubKeyPath;
        string startupLnk = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Program.ApplicationName + ".lnk");
        string settingsDir = AppSettings.GetDefaultDirectory();

        // For LocalAppData, install dir == settings dir.
        // For ProgramFiles they're disjoint and we can wipe install dir freely.
        bool installIsSettingsDir = IsSamePath(installDir, settingsDir);

        // Wipe install dir wholesale UNLESS we'd be eating the user's settings against their wishes.
        bool wipeInstallDirWholesale = !installIsSettingsDir || deleteSettings;

        // Independent settings-dir wipe needed only when settings dir is separate AND user asked for it.
        bool wipeSettingsDirSeparately = deleteSettings && !installIsSettingsDir;

        // PowerShell single-quoted literal: any embedded ' must be doubled.
        string installExeForPs = installExe.Replace("'", "''");
        string startupLnkForPs = startupLnk.Replace("'", "''");
        // Trailing separator pins the StartsWith comparison to whole-segment matches:
        // "C:\Foo\Bar" must not match "C:\Foo\BarBaz\app.exe".
        string installDirPrefixForPs = (installDir.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .Replace("'", "''");

        StringBuilder sb = new();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal");
        sb.AppendLine("set ERR=0");
        sb.AppendLine();
        sb.AppendLine("rem Kill processes whose executable path equals the install exe (and only those -");
        sb.AppendLine("rem a portable copy of the app running from elsewhere is untouched).");
        sb.AppendLine("rem Loops with a brief sleep so the watcher/monitored restart race resolves.");
        sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
            + "\"$p = '" + installExeForPs + "'; "
            + "for ($i=0; $i -lt 20; $i++) { "
            + "$procs = Get-Process -Name " + Program.ApplicationName + " -ErrorAction SilentlyContinue "
            + "| Where-Object { try { $_.Path -ieq $p } catch { $false } }; "
            + "if (-not $procs) { break }; "
            + "$procs | Stop-Process -Force -ErrorAction SilentlyContinue; "
            + "Start-Sleep -Milliseconds 500 }\" >nul 2>&1");
        sb.AppendLine();
        if (wipeInstallDirWholesale)
        {
            sb.AppendLine("rem Wipe the install dir (and everything in it). Either it's ProgramFiles");
            sb.AppendLine("rem (no user data) or the user opted to delete settings too.");
            sb.AppendLine($"rmdir /s /q \"{EscBat(installDir)}\" >nul 2>&1");
            sb.AppendLine($"if exist \"{EscBat(installDir)}\" set ERR=1");
        }
        else
        {
            sb.AppendLine("rem LocalAppData install dir is shared with settings. User asked to keep settings,");
            sb.AppendLine("rem so surgically remove only the runtime files we deployed and leave user data alone.");
            foreach (string pattern in RuntimeFilePatterns)
                sb.AppendLine($"del /f /q \"{EscBat(installDir)}\\{pattern}\" >nul 2>&1");
            sb.AppendLine($"if exist \"{EscBat(installExe)}\" set ERR=1");
            sb.AppendLine("rem If the dir is empty after the surgical pass (e.g. fresh install never ran),");
            sb.AppendLine("rem clean it up; if user data remains, rmdir no-ops.");
            sb.AppendLine($"rmdir \"{EscBat(installDir)}\" >nul 2>&1");
        }
        sb.AppendLine();
        sb.AppendLine("rem Registry: missing key returns errorlevel 1 (orphan-cleaned state) - not a real failure.");
        sb.AppendLine("rem Only flag if the key still exists after the delete (i.e. permission denied).");
        sb.AppendLine($"reg delete \"{regKeyFullPath}\" /f >nul 2>&1");
        sb.AppendLine($"reg query \"{regKeyFullPath}\" >nul 2>&1");
        sb.AppendLine("if not errorlevel 1 set ERR=1");
        sb.AppendLine();
        if (wipeSettingsDirSeparately)
        {
            sb.AppendLine("rem ProgramFiles install + user wants settings gone. Wipe the AppData settings dir too.");
            sb.AppendLine($"rmdir /s /q \"{EscBat(settingsDir)}\" >nul 2>&1");
            sb.AppendLine($"if exist \"{EscBat(settingsDir)}\" set ERR=1");
            sb.AppendLine();
        }
        sb.AppendLine("rem Surgical shortcut delete: only remove the shell:startup .lnk if its target");
        sb.AppendLine("rem still points inside the install dir we're wiping. The C# pre-uninstall pass");
        sb.AppendLine("rem already retargeted it to a peer install / running exe when one was available;");
        sb.AppendLine("rem this catches the residual case (uninstalling the live install copy with no peer).");
        sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
            + "\"$lnk = '" + startupLnkForPs + "'; "
            + "$dir = '" + installDirPrefixForPs + "'; "
            + "if (Test-Path -LiteralPath $lnk) { try { "
            + "$ws = New-Object -ComObject WScript.Shell; "
            + "$sc = $ws.CreateShortcut($lnk); "
            + "$t = $sc.TargetPath; "
            + "if ($t -and $t.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)) "
            + "{ Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue } "
            + "} catch { } }\" >nul 2>&1");
        sb.AppendLine("rem Legacy HKCU\\...\\Run entry from the pre-shortcut autostart era; idempotent removal.");
        sb.AppendLine("reg delete \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\""
            + $" /v \"{Program.ApplicationName}\" /f >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem Self-delete and propagate ERR. (goto) discards the rest of the script,");
        sb.AppendLine("rem but the parsed compound chain on this line still runs to completion.");
        sb.AppendLine("(goto) 2>nul & del /f /q \"%~f0\" & exit /b %ERR%");
        return sb.ToString();
    }

    private static bool IsSamePath(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // cmd.exe always expands % at parse time. A literal % in an embedded path must be doubled
    // so a folder name like "50%off" doesn't trigger a (missing) variable expansion.
    private static string EscBat(string path) => path.Replace("%", "%%");
}
