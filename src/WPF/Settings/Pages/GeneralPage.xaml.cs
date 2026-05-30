using System.Windows;
using System.Diagnostics;
using NetworkTrayAppWPF.Localization;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Services;
using NetworkTrayAppWPF.Utils;
using NetworkTrayAppWPF.WPF.Settings.Utils;
using Button = System.Windows.Controls.Button;
using UserControl = System.Windows.Controls.UserControl;

namespace NetworkTrayAppWPF.WPF.Settings.Pages;

/// <summary>
/// General settings page.
/// Hosts the run-on-startup toggle plus the install/uninstall rows
/// for the local-AppData and Program Files install locations.
/// The shell calls <see cref="LoadFromSettings"/> after construction
/// and <see cref="RefreshOnShow"/> on every nav-to-General
/// so the install rows reflect current filesystem state.
/// </summary>
public partial class GeneralPage : UserControl
{
    private static string RunOnStartupOffDescription =>
        LocalizationManager.Instance["Settings_General_RunOnStartup_Description"];
    private static string RunOnStartupOnHeaderLine =>
        LocalizationManager.Instance["Settings_General_RunOnStartup_OnHeaderLine"];

    private AppSettings? _settings;
    private bool _suppressChangeEvents;

    public GeneralPage()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshOnShow();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) RefreshOnShow();
        };
    }

    /// <summary>
    /// Injects AppSettings and seeds every control's value.
    /// The shell calls this once from its own LoadFromSettings;
    /// subsequent calls re-seed if settings are reloaded externally.
    /// </summary>
    public void LoadFromSettings(AppSettings settings)
    {
        _settings = settings;
        _suppressChangeEvents = true;
        try
        {
            RunOnStartupToggle.IsChecked = StartupManager.GetRunOnStartup();
            UpdateRunOnStartupDescription();
        }
        finally
        {
            _suppressChangeEvents = false;
        }

        // Populate install card descriptions on initial load. The default checked nav item is
        // set from XAML before the shell's Checked handler is attached, so first show needs this.
        RefreshInstallationSection();
    }

    /// <summary>
    /// Called by the shell on every nav-to-General.
    /// Refreshes the install/uninstall row state
    /// (so install state reflects the current filesystem state,
    /// e.g. after an elevated install spawned from this page completes).
    /// </summary>
    public void RefreshOnShow()
    {
        RefreshInstallationSection();
        // Re-read the shortcut target so the displayed path catches up if the user installed /
        // uninstalled into a different scope while the Settings window stayed open, or if
        // RepairShortcutIfStale rewrote the target during this app launch.
        UpdateRunOnStartupDescription();
    }

    private void RunOnStartup_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressChangeEvents) return;

        if (_settings == null) return;

        bool enabled = RunOnStartupToggle.IsChecked == true;
        StartupManager.SetRunOnStartup(enabled);
        _settings.RunOnStartup = enabled;
        UpdateRunOnStartupDescription();
        SaveAndNotify();
    }

    /// <summary>
    /// Swaps the "Run on startup" card description between the off-state explanation and an
    /// on-state report that names shell:startup as the location and shows the resolved exe target.
    /// Reads the live shortcut on disk via <see cref="StartupManager.GetCurrentShortcutTarget"/>
    /// rather than recomputing the priority order, so the card reflects what's actually wired up
    /// (including post-repair overrides).
    /// </summary>
    private void UpdateRunOnStartupDescription()
    {
        string? target = StartupManager.GetCurrentShortcutTarget();
        if (string.IsNullOrEmpty(target))
        {
            RunOnStartupCard.Description = RunOnStartupOffDescription;
            return;
        }
        RunOnStartupCard.Description = string.Format(
            LocalizationManager.Instance["Settings_General_RunOnStartup_OnDescriptionFormat"],
            RunOnStartupOnHeaderLine, target);
    }

    /// <summary>
    /// Post-install / post-uninstall fixup: re-target the autostart shortcut at the new
    /// highest-priority install on disk, then refresh the install rows and the run-on-startup
    /// description so the UI reflects what's now wired up. Retarget runs first so the path the
    /// description reads back is the one we just wrote.
    /// </summary>
    private void RefreshAfterInstallChange()
    {
        StartupManager.RetargetShortcutIfPresent();
        RefreshInstallationSection();
        UpdateRunOnStartupDescription();
    }

    private void RefreshInstallationSection()
    {
        List<InstallationInfo> infos = InstallationService.DetectAll();
        foreach (InstallationInfo info in infos)
        {
            switch (info.Scope)
            {
                case InstallScope.LocalAppData:
                    ApplyInstallRow(info,
                        InstallLocalAppDataCard,
                        InstallLocalAppDataButton,
                        UninstallLocalAppDataButton,
                        InstallationService.LocalAppDataInstallExecutable,
                        elevated: false);
                    break;
                case InstallScope.ProgramFiles:
                    ApplyInstallRow(info,
                        InstallProgramFilesCard,
                        InstallProgramFilesButton,
                        UninstallProgramFilesButton,
                        InstallationService.ProgramFilesInstallExecutable,
                        elevated: true);
                    break;
                case InstallScope.WindowsStore:
                    ApplyStoreRow(info);
                    break;
            }
        }
    }

    private static void ApplyInstallRow(
        InstallationInfo info,
        SettingsCard card,
        Button installButton,
        Button uninstallButton,
        string installPath,
        bool elevated)
    {
        string elevationSuffix = elevated
            ? LocalizationManager.Instance["Settings_General_RequiresAdmin_Suffix"]
            : "";

        switch (info.Status)
        {
            case InstallStatus.NotInstalled:
                card.Description = string.Format(
                    LocalizationManager.Instance["Settings_General_NotInstalled_Format"],
                    installPath, elevationSuffix);
                installButton.Content = LocalizationManager.Instance["Settings_General_Install_Button"];
                installButton.Visibility = Visibility.Visible;
                uninstallButton.Visibility = Visibility.Collapsed;
                break;
            case InstallStatus.InstalledUpToDate:
                card.Description = info.InstalledVersion is { } v
                    ? string.Format(
                        LocalizationManager.Instance["Settings_General_InstalledWithBuild_Format"],
                        v, installPath)
                    : string.Format(
                        LocalizationManager.Instance["Settings_General_Installed_Format"],
                        installPath);
                installButton.Visibility = Visibility.Collapsed;
                uninstallButton.Content = LocalizationManager.Instance["Settings_General_Uninstall_Button"];
                uninstallButton.Visibility = Visibility.Visible;
                break;
            case InstallStatus.InstalledOutOfDate:
                card.Description = info.InstalledVersion is { } ov
                    ? string.Format(
                        LocalizationManager.Instance["Settings_General_InstalledOutOfDate_Format"],
                        ov, BuildInfo.BuildNumber, elevationSuffix)
                    : string.Format(
                        LocalizationManager.Instance["Settings_General_InstalledOlderBuild_Format"],
                        installPath, elevationSuffix);
                installButton.Content = LocalizationManager.Instance["Settings_General_Update_Button"];
                installButton.Visibility = Visibility.Visible;
                uninstallButton.Content = LocalizationManager.Instance["Settings_General_Uninstall_Button"];
                uninstallButton.Visibility = Visibility.Visible;
                break;
            case InstallStatus.CurrentlyRunning:
                card.Description = string.Format(
                    LocalizationManager.Instance["Settings_General_CurrentlyRunning_Format"],
                    installPath);
                installButton.Visibility = Visibility.Collapsed;
                uninstallButton.Content = LocalizationManager.Instance["Settings_General_Uninstall_Button"];
                uninstallButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private void ApplyStoreRow(InstallationInfo info)
    {
        InstallStoreCard.Description = info.Status == InstallStatus.CurrentlyRunning
            ? LocalizationManager.Instance["Settings_General_StoreRunning"]
            : LocalizationManager.Instance["Settings_General_StoreNotInstalled"];
    }

    // Window.GetWindow can return null for a UserControl that isn't yet parented,
    // and MessageBox.Show's Window-owner overload doesn't accept null.
    // Fall through to the no-owner overload in that case so the dialog still surfaces.
    private void ShowOwnedWarning(string message, string title)
    {
        Window? owner = Window.GetWindow(this);
        if (owner != null)
            System.Windows.MessageBox.Show(owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            System.Windows.MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private async void InstallLocalAppData_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button) return;

            if (Window.GetWindow(this) is IConfirmDialogService confirm)
            {
                bool ok = await confirm.ConfirmAsync(
                    title: LocalizationManager.Instance["Settings_General_InstallConfirm_Title"],
                    message: string.Format(
                        LocalizationManager.Instance["Settings_General_InstallConfirm_Message_Format"],
                        InstallationService.LocalAppDataInstallExecutable),
                    confirmText: LocalizationManager.Instance["Settings_General_Install_Button"],
                    cancelText: LocalizationManager.Instance["Settings_General_Cancel_Button"]);
                if (!ok) return;
            }

            button.IsEnabled = false;
            try
            {
                InstallResult result = await Task.Run(InstallationService.InstallToLocalAppData);
                if (result is { Success: false, UserCancelled: false } && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    ShowOwnedWarning(
                        result.ErrorMessage,
                        LocalizationManager.Instance["Settings_General_InstallFailed_Title"]);
                }
            }
            finally
            {
                button.IsEnabled = true;
                RefreshAfterInstallChange();
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"GeneralPage.InstallLocalAppData_Click: {ex.Message}");
        }
    }

    private async void InstallProgramFiles_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not Button button) return;

            if (Window.GetWindow(this) is IConfirmDialogService confirm)
            {
                bool ok = await confirm.ConfirmAsync(
                    title: LocalizationManager.Instance["Settings_General_InstallSystemWideConfirm_Title"],
                    message: string.Format(
                        LocalizationManager.Instance["Settings_General_InstallSystemWideConfirm_Message_Format"],
                        InstallationService.ProgramFilesInstallExecutable),
                    confirmText: LocalizationManager.Instance["Settings_General_Install_Button"],
                    cancelText: LocalizationManager.Instance["Settings_General_Cancel_Button"]);
                if (!ok) return;
            }

            button.IsEnabled = false;
            try
            {
                InstallResult result = await Task.Run(InstallationService.InstallSystemWide);
                if (result is { Success: false, UserCancelled: false } && !string.IsNullOrEmpty(result.ErrorMessage))
                {
                    ShowOwnedWarning(
                        result.ErrorMessage,
                        LocalizationManager.Instance["Settings_General_InstallFailed_Title"]);
                }
            }
            finally
            {
                button.IsEnabled = true;
                RefreshAfterInstallChange();
            }
        }
        catch (Exception ex)
        {
            WPFLog.Log($"GeneralPage.InstallProgramFiles_Click: {ex.Message}");
        }
    }

    private void UninstallLocalAppData_Click(object sender, RoutedEventArgs e)
    {
        UninstallerWindow uninstallerDialog = new(
            InstallationService.LocalAppDataInstallDir,
            WindowsUninstallRegistry.Scope.CurrentUser)
        {
            Owner = Window.GetWindow(this),
        };
        uninstallerDialog.ShowDialog();
        HookPostUninstallRefresh(uninstallerDialog);
    }

    private void UninstallProgramFiles_Click(object sender, RoutedEventArgs e)
    {
        UninstallerWindow uninstallerDialog = new(
            InstallationService.ProgramFilesInstallDir,
            WindowsUninstallRegistry.Scope.LocalMachine)
        {
            Owner = Window.GetWindow(this),
        };
        uninstallerDialog.ShowDialog();
        HookPostUninstallRefresh(uninstallerDialog);
    }

    /// <summary>
    /// Wires <see cref="Process.Exited"/> on the bat process
    /// so the install row flips back to "Install" the moment the bat finishes
    /// (file deleted, registry cleared, cmd.exe exits).
    /// Event-driven; nothing polls.
    /// A non-zero ExitCode (install exe still on disk, registry key still present,
    /// or settings folder couldn't be wiped) surfaces a warning MessageBox.
    /// Null UninstallProcess (UAC declined or running install copy shutting down)
    /// leaves the UI alone since either the row's state didn't change or the app is dying.
    /// </summary>
    private void HookPostUninstallRefresh(UninstallerWindow uninstallerDialog)
    {
        if (!uninstallerDialog.ConfirmedUninstall) return;

        Process? uninstallProcess = uninstallerDialog.UninstallProcess;
        if (uninstallProcess == null) return;

        uninstallProcess.Exited += (_, _) => OnUninstallBatExited(uninstallProcess);
        // Race: the bat may have already exited by the time we attach Exited. HasExited is
        // checked AFTER attach so a fast finish doesn't slip through.
        if (uninstallProcess.HasExited) OnUninstallBatExited(uninstallProcess);
    }

    private void OnUninstallBatExited(Process bat)
    {
        int exitCode;
        try { exitCode = bat.ExitCode; }
        catch { exitCode = 0; }
        finally { bat.Dispose(); }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            RefreshAfterInstallChange();
            if (exitCode != 0)
            {
                ShowOwnedWarning(
                    LocalizationManager.Instance["Settings_General_UninstallIncomplete_Message"],
                    LocalizationManager.Instance["Settings_General_UninstallIncomplete_Title"]);
            }
        }));
    }

    private void SaveAndNotify()
    {
        if (_settings == null) return;
        _settings.Save();
        _settings.RaiseChanged();
    }
}
