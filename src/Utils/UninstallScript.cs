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

    // App-specific files laid down by the current install flow. The install root is shared
    // across TrayAppWPF-derived apps, so uninstall must not wildcard-delete sibling apps.
    private static string[] InstalledAppFileNames =>
    [
        InstallationService.InstalledExecutableFileName,
        Program.ApplicationName + ".dll",
        Program.ApplicationName + ".pdb",
        Program.ApplicationName + ".runtimeconfig.json",
        Program.ApplicationName + ".deps.json",
    ];

    private static string BuildScript(string installDir, WindowsUninstallRegistry.Scope regScope, bool deleteSettings)
    {
        string installExecutable = Path.Combine(installDir, InstallationService.InstalledExecutableFileName);
        string regKeyFullPath = (regScope == WindowsUninstallRegistry.Scope.LocalMachine ? "HKLM\\" : "HKCU\\")
            + WindowsUninstallRegistry.SubKeyPath;
        string startupLnk = StartupManager.ShortcutPath;
        string settingsDir = AppSettings.GetDefaultDirectory();

        // PowerShell single-quoted literal: any embedded ' must be doubled.
        string installExecutableForPs = installExecutable.Replace("'", "''");
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
        if (regScope == WindowsUninstallRegistry.Scope.LocalMachine)
        {
            sb.AppendLine("rem Reconcile Start Menu shortcuts across every user profile from an already-elevated context.");
            sb.AppendLine($"start \"\" /wait \"{EscBat(installExecutable)}\" --admin-action sync-startmenu --remove-scope system");
            sb.AppendLine();
        }
        sb.AppendLine("rem Kill processes whose executable path equals the install exe (and only those -");
        sb.AppendLine("rem a portable copy of the app running from elsewhere is untouched).");
        sb.AppendLine("rem Loops with a brief sleep so the watcher/monitored restart race resolves.");
        sb.AppendLine("powershell -NoProfile -ExecutionPolicy Bypass -Command "
            + "\"$p = '" + installExecutableForPs + "'; "
            + "for ($i=0; $i -lt 20; $i++) { "
            + "$procs = Get-Process -Name " + Program.ApplicationName + " -ErrorAction SilentlyContinue "
            + "| Where-Object { try { $_.Path -ieq $p } catch { $false } }; "
            + "if (-not $procs) { break }; "
            + "$procs | Stop-Process -Force -ErrorAction SilentlyContinue; "
            + "Start-Sleep -Milliseconds 500 }\" >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem Shared install root: remove only this app's files and leave sibling apps alone.");
        foreach (string fileName in InstalledAppFileNames)
            sb.AppendLine($"del /f /q \"{EscBat(Path.Combine(installDir, fileName))}\" >nul 2>&1");
        sb.AppendLine($"if exist \"{EscBat(installExecutable)}\" set ERR=1");
        sb.AppendLine("rem Remove the shared install dir only if it is empty after this app's files are gone.");
        sb.AppendLine($"rmdir \"{EscBat(installDir)}\" >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem Registry: missing key returns errorlevel 1 (orphan-cleaned state) - not a real failure.");
        sb.AppendLine("rem Only flag if the key still exists after the delete (i.e. permission denied).");
        sb.AppendLine($"reg delete \"{regKeyFullPath}\" /f >nul 2>&1");
        sb.AppendLine($"reg query \"{regKeyFullPath}\" >nul 2>&1");
        sb.AppendLine("if not errorlevel 1 set ERR=1");
        sb.AppendLine();
        if (deleteSettings)
        {
            sb.AppendLine("rem User wants settings gone. Settings remain app-specific under the shared AppData root.");
            sb.AppendLine($"rmdir /s /q \"{EscBat(settingsDir)}\" >nul 2>&1");
            sb.AppendLine($"if exist \"{EscBat(settingsDir)}\" set ERR=1");
            sb.AppendLine("rem Settings may have been the last child keeping a shared root from being empty.");
            sb.AppendLine($"rmdir \"{EscBat(installDir)}\" >nul 2>&1");
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
        sb.AppendLine($"reg delete \"HKCU\\{StartupManager.LegacyRunKeyRegistryPath}\""
            + $" /v \"{Program.ApplicationName}\" /f >nul 2>&1");
        sb.AppendLine();
        sb.AppendLine("rem Self-delete and propagate ERR. (goto) discards the rest of the script,");
        sb.AppendLine("rem but the parsed compound chain on this line still runs to completion.");
        sb.AppendLine("(goto) 2>nul & del /f /q \"%~f0\" & exit /b %ERR%");
        return sb.ToString();
    }

    // cmd.exe always expands % at parse time. A literal % in an embedded path must be doubled
    // so a folder name like "50%off" doesn't trigger a (missing) variable expansion.
    private static string EscBat(string path) => path.Replace("%", "%%");
}
