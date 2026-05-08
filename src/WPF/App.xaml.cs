using Microsoft.Win32;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using NetworkTrayAppWPF.Interop;
using NetworkTrayAppWPF.Localization;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Services;
using NetworkTrayAppWPF.Visuals;
using Point = System.Windows.Point;
using SettingsThemeMode = NetworkTrayAppWPF.Models.ThemeMode;

namespace NetworkTrayAppWPF.WPF;

/// <summary>
/// Network tray app shell. Owns settings, theme, tray icon, hotkeys, the network monitor,
/// and the settings window.
/// Software rendering plus custom Win32 interop for the tray icon.
/// </summary>
public partial class App
{
    private TrayIconManager? _trayIconManager;
    private AppTheme? _theme;
    private AppSettings? _appSettings;
    private ContextMenu? _contextMenu;
    private CancellationTokenSource? _watcherMonitorCts;
    private SettingsWindow? _settingsWindow;
    private GlobalHotkeyService? _hotkeyService;

    private NetworkMonitor? _networkMonitor;
    private NetworkTrayIconRenderer? _networkRenderer;
    private DispatcherTimer? _refreshTimer;

    // Polling fallback in case WinRT NetworkStatusChanged misses an event (rare but observed on suspend/resume).
    private static readonly TimeSpan NetworkPollInterval = TimeSpan.FromSeconds(3);


    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Idempotent - Program.Main already called this.
        // Safe to repeat so any direct App entry (e.g. attached debugger) still gets a logger.
        WPFLog.Initialize();
        WPFLog.Log($"App.OnStartup: begin, args=[{string.Join(' ', e.Args)}]");

        // Seed the localization manager before any UI is built so the first XAML load
        // sees the right culture on every {loc:Loc ...} lookup.
        LocalizationManager.Instance.Initialize();

        if (Program.IsUninstallerMode)
        {
            RunUninstallerMode();
            return;
        }

        // Crash-path shutdown handlers. Cap each best-effort cleanup so a hung op can't block the exit.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            WPFLog.Log($"FATAL UnhandledException: {args.ExceptionObject}");
            WPFLog.Flush();
            Environment.Exit(1);
        };
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            WPFLog.Log($"FATAL DispatcherUnhandledException: {args.Exception}");
            WPFLog.Flush();
            Environment.Exit(1);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // Last handler to fire on every exit path - tear the logger down here, not earlier.
            WPFLog.Shutdown();
        };
        SessionEnding += (_, args) =>
        {
            WPFLog.Log($"SessionEnding: reason={args.ReasonSessionEnding}");
            WPFLog.Flush();
        };

        // Software rendering keeps the long-lived tray app's working set small;
        // hardware accel would pin a swapchain for a hidden window we never paint into.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Detect first-run before LoadOrDefault writes the default file
        // so we can reconcile OS state (e.g. startup registration) with the defaults that just got persisted.
        try
        {
            string settingsPath = AppSettings.GetDefaultPath();
            bool firstRun = !File.Exists(settingsPath);
            _appSettings = AppSettings.LoadOrDefault(settingsPath);
            if (firstRun) StartupManager.SetRunOnStartup(_appSettings.RunOnStartup);
        }
        catch (Exception ex)
        {
            WPFLog.Log($"App.OnStartup: settings load failed: {ex.Message}");
            _appSettings = new AppSettings();
        }

        // Drop the legacy HKCU\...\Run autostart entry (older builds wrote one)
        // and revalidate the shell:startup shortcut.
        StartupManager.RemoveLegacyRunKey();
        StartupManager.RepairShortcutIfStale();
        _appSettings.Changed += OnSettingsChanged;
        AppServices.Settings = _appSettings;

        try
        {
            _theme = AppTheme.LoadOrDefault(AppTheme.GetDefaultPath());
            _theme.ThemeChanged += OnThemeChanged;
            AppServices.Theme = _theme;
            UpdateThemeResources(ResolveEffectiveIsLightTheme());
        }
        catch (Exception ex)
        {
            WPFLog.Log($"App.OnStartup: theme init failed: {ex.Message}");
        }

        // Spin up the network monitor before the tray icon so the first refresh has a real state to show.
        try
        {
            _networkRenderer = new NetworkTrayIconRenderer
            {
                IsLightTheme = ResolveEffectiveIsLightTheme(),
            };
            ApplyNetworkColorOverrides();

            _networkMonitor = new NetworkMonitor();
            _networkMonitor.NetworkStateChanged += OnNetworkStateChanged;
            _networkMonitor.Initialize();
        }
        catch (Exception ex) { WPFLog.Log($"App.OnStartup: NetworkMonitor init failed: {ex.Message}"); }

        try { CreateTrayIcon(); }
        catch (Exception ex) { WPFLog.Log($"App.OnStartup: CreateTrayIcon failed: {ex.Message}"); }

        try { RequestTrayRefresh(); }
        catch (Exception ex) { WPFLog.Log($"App.OnStartup: RequestTrayRefresh failed: {ex.Message}"); }

        // Polling fallback: WinRT's NetworkStatusChanged can drop edges around suspend/resume,
        // so a low-frequency refresh keeps the tray honest without burning CPU.
        try
        {
            _refreshTimer = new DispatcherTimer { Interval = NetworkPollInterval };
            _refreshTimer.Tick += OnRefreshTimerTick;
            _refreshTimer.Start();
        }
        catch (Exception ex) { WPFLog.Log($"App.OnStartup: refresh timer init failed: {ex.Message}"); }

        // DPI changes (taskbar move between monitors at different scale) need an icon redraw
        // because the renderer queries DPI at draw time and the cached icon is the wrong size.
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        // Global hotkeys. Owns its own message-only window for WM_HOTKEY;
        // created on the UI thread so RegisterHotKey's thread-affinity contract is satisfied
        // and hotkey events fire back here without Dispatcher marshaling.
        try
        {
            _hotkeyService = new GlobalHotkeyService();
            _hotkeyService.Initialize();
            _hotkeyService.Fired += OnHotkeyFired;
            if (_appSettings != null) _hotkeyService.Apply(_appSettings.Hotkeys);

            AppServices.HotkeyService = _hotkeyService;
        }
        catch (Exception ex) { WPFLog.Log($"App.OnStartup: GlobalHotkeyService init failed: {ex.Message}"); }

        try { StartWatcherMonitor(); }
        catch (Exception ex) { WPFLog.Log($"App.OnStartup: StartWatcherMonitor failed: {ex.Message}"); }
    }

    /// <summary>
    /// Stripped-down startup for <c>--uninstall</c> mode:
    /// load settings purely so the theme follows the user's preference, init theme resources,
    /// then show <see cref="UninstallerWindow"/> as the only window.
    /// No tray icon, no hotkeys, no watcher.
    /// </summary>
    private void RunUninstallerMode()
    {
        try { _appSettings = AppSettings.LoadOrDefault(); }
        catch { _appSettings = new AppSettings(); }

        try
        {
            _theme = AppTheme.LoadOrDefault(AppTheme.GetDefaultPath());
            UpdateThemeResources(ResolveEffectiveIsLightTheme());
        }
        catch (Exception ex)
        {
            WPFLog.Log($"App.RunUninstallerMode: theme init failed: {ex.Message}");
        }

        ShutdownMode = ShutdownMode.OnLastWindowClose;

        UninstallerWindow window = new(
            Program.UninstallerInstallDir ?? string.Empty,
            Program.UninstallerScope);
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Returns the theme to apply (light=true) after considering the user's ThemeMode override.
    /// </summary>
    private bool ResolveEffectiveIsLightTheme()
    {
        if (_appSettings == null || _theme == null) return _theme?.IsLightTheme ?? false;

        return _appSettings.ThemeMode switch
        {
            SettingsThemeMode.Light => true,
            SettingsThemeMode.Dark => false,
            _ => _theme.IsLightTheme,
        };
    }

    /// <summary>
    /// Polls the watcher process and exits the app when it dies, so we don't run orphaned.
    /// </summary>
    private void StartWatcherMonitor()
    {
        if (Program.WatcherPID is not { } watcherPID) return;

        _watcherMonitorCts = new CancellationTokenSource();
        CancellationToken token = _watcherMonitorCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                using Process watcherProcess = Process.GetProcessById(watcherPID);

                while (!token.IsCancellationRequested)
                {
                    if (watcherProcess.HasExited)
                    {
                        await Dispatcher.InvokeAsync(ExitApplication);
                        return;
                    }

                    await Task.Delay(TimeConstants.WatcherLivenessPollIntervalMs, token);
                }
            }
            catch (ArgumentException)
            {
                // Watcher PID already gone - exit immediately.
                await Dispatcher.InvokeAsync(ExitApplication);
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation during shutdown.
            }
            catch
            {
                // ignore
            }
        }, token);
    }

    private void CreateTrayIcon()
    {
        if (_theme == null) return;

        _contextMenu = CreateContextMenu();

        _trayIconManager = new TrayIconManager();
        _trayIconManager.LeftClick += OnTrayLeftClick;
        _trayIconManager.LeftDoubleClick += OnTrayLeftDoubleClick;
        _trayIconManager.RightClick += OnTrayRightClick;
        _trayIconManager.RefreshNeeded += RequestTrayRefresh;

        RequestTrayRefresh();
        _trayIconManager.IsVisible = true;
    }

    private ContextMenu CreateContextMenu()
    {
        ContextMenu contextMenu = new();

        MenuItem networkSettingsItem = new() { Header = LocalizationManager.Instance["Tray_NetworkSettings"] };
        networkSettingsItem.Click += (_, _) => OpenNetworkSettings();

        MenuItem adapterSettingsItem = new() { Header = LocalizationManager.Instance["Tray_AdapterSettings"] };
        adapterSettingsItem.Click += (_, _) => OpenAdapterSettings();

        MenuItem appSettingsItem = new() { Header = LocalizationManager.Instance["Tray_Settings"] };
        appSettingsItem.Click += (_, _) => OpenSettings();

        MenuItem exitItem = new() { Header = LocalizationManager.Instance["Tray_Exit"] };
        exitItem.Click += (_, _) => ExitApplication();

        contextMenu.Items.Add(networkSettingsItem);
        contextMenu.Items.Add(adapterSettingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(appSettingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        ApplyContextMenuTheme(contextMenu);

        // Dissolve every Separator: tag the preceding item HasBottomRule, the following HasTopRule,
        // and remove the Separator itself - the rules now live inside neighbor MenuItems' templates.
        DissolveSeparatorsIntoNeighbors(contextMenu);

        return contextMenu;
    }

    private const string MenuItemTagHasTopRule = "HasTopRule";
    private const string MenuItemTagHasBottomRule = "HasBottomRule";

    private static void DissolveSeparatorsIntoNeighbors(ContextMenu menu)
    {
        // Walk back-to-front so RemoveAt doesn't shift indices we still need to read.
        for (int i = menu.Items.Count - 1; i >= 0; i--)
        {
            if (menu.Items[i] is not Separator) continue;

            if (i > 0 && menu.Items[i - 1] is MenuItem prev)
                prev.Tag = MenuItemTagHasBottomRule;

            if (i + 1 < menu.Items.Count && menu.Items[i + 1] is MenuItem next)
                next.Tag = MenuItemTagHasTopRule;

            menu.Items.RemoveAt(i);
        }
    }

    private void OnHotkeyFired(object? sender, HotkeyFiredEventArgs e)
    {
        try { HandleHotkey(e.Action); }
        catch (Exception ex) { WPFLog.Log($"App.OnHotkeyFired: {ex.Message}"); }
    }

    /// <summary>
    /// Translates a fired hotkey into the matching app action. Runs on the UI thread because the
    /// hotkey service's message-only window was created here, so direct UI calls are safe.
    /// </summary>
    private void HandleHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.OpenSettings:
                OpenSettings();
                break;
            case HotkeyAction.OpenFlyout:
                OpenNetworkFlyout();
                break;
            case HotkeyAction.OpenNetworkSettings:
                OpenNetworkSettings();
                break;
            case HotkeyAction.OpenAdapterSettings:
                OpenAdapterSettings();
                break;
        }
    }

    private void OnTrayLeftClick() => OpenNetworkFlyout();

    private void OnTrayLeftDoubleClick()
    {
        TrayClickAction action = _appSettings?.TrayDoubleClickAction ?? TrayClickAction.OpenAdapterSettings;
        switch (action)
        {
            case TrayClickAction.OpenSettings:
                OpenSettings();
                break;
            case TrayClickAction.OpenAdapterSettings:
                OpenAdapterSettings();
                break;
        }
    }

    private void OnTrayRightClick(Point point)
    {
        Dispatcher.BeginInvoke(() =>
        {
            // Rebuild every time so settings-driven changes take effect.
            _contextMenu = CreateContextMenu();
            ContextMenuPosition placement = _appSettings?.ContextMenuPosition ?? ContextMenuPosition.Classic;
            _trayIconManager?.ShowContextMenu(_contextMenu, point, placement);
        });
    }

    private void OnNetworkStateChanged(NetworkIconState state) =>
        Dispatcher.BeginInvoke(RequestTrayRefresh);

    private void OnRefreshTimerTick(object? sender, EventArgs e) => _networkMonitor?.RefreshState();

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Taskbar moved across DPI surfaces - invalidate the icon cache so the next refresh
        // re-queries DPI and renders at the new size.
        _networkRenderer?.InvalidateCache();
        Dispatcher.BeginInvoke(RequestTrayRefresh);
    }

    private void OnThemeChanged(bool isLightTheme)
    {
        Dispatcher.BeginInvoke(() =>
        {
            bool effective = ResolveEffectiveIsLightTheme();
            UpdateThemeResources(effective);
            if (_networkRenderer != null) _networkRenderer.IsLightTheme = effective;
            if (_trayIconManager != null) RequestTrayRefresh();
        });
    }

    private void OnSettingsChanged()
    {
        Dispatcher.BeginInvoke(() =>
        {
            bool effective = ResolveEffectiveIsLightTheme();
            UpdateThemeResources(effective);

            if (_networkRenderer != null)
            {
                _networkRenderer.IsLightTheme = effective;
                ApplyNetworkColorOverrides();
            }

            if (_trayIconManager != null && _appSettings != null) RequestTrayRefresh();

            // Re-apply hotkeys so edits in Settings take effect immediately.
            if (_hotkeyService != null && _appSettings != null) _hotkeyService.Apply(_appSettings.Hotkeys);

            _contextMenu = CreateContextMenu();
        });
    }

    /// <summary>
    /// Pushes the current AppSettings color overrides into the renderer.
    /// Resolves each override against the effective theme (Temporary* live-preview wins via NullableThemeColor.Resolve).
    /// </summary>
    private void ApplyNetworkColorOverrides()
    {
        if (_networkRenderer == null || _appSettings == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();
        _networkRenderer.ConnectedColorOverride = _appSettings.NetworkConnectedColor.Resolve(isLight);
        _networkRenderer.NoInternetColorOverride = _appSettings.NetworkNoInternetColor.Resolve(isLight);
        _networkRenderer.DisconnectedColorOverride = _appSettings.NetworkDisconnectedColor.Resolve(isLight);
    }

    private void ApplyContextMenuTheme(ContextMenu menu)
    {
        if (_theme == null) return;

        bool isLight = ResolveEffectiveIsLightTheme();

        menu.Background = new SolidColorBrush(_theme.ResolveBackground(_appSettings, isLight));
        menu.Foreground = new SolidColorBrush(_theme.ResolveForeground(_appSettings, isLight));
        menu.BorderBrush = new SolidColorBrush(_theme.Border.For(isLight));

        int fontSize = _appSettings?.ContextMenuFontSize ?? 15;

        foreach (object item in menu.Items)
        {
            switch (item)
            {
                case MenuItem menuItem:
                    menuItem.Foreground = menu.Foreground;
                    menuItem.FontSize = fontSize;
                    break;
                case Separator separator:
                    separator.Background = new SolidColorBrush(_theme.Separator.For(isLight));
                    break;
            }
        }
    }

    private void UpdateThemeResources(bool isLightTheme)
    {
        if (_theme == null) return;

        // Core colors (user overrides win).
        Resources["ThemeBackground"] = new SolidColorBrush(_theme.ResolveBackground(_appSettings, isLightTheme));
        Resources["ThemeForeground"] = new SolidColorBrush(_theme.ResolveForeground(_appSettings, isLightTheme));
        Resources["ThemeBorder"] = new SolidColorBrush(_theme.Border.For(isLightTheme));
        Resources["ThemeHover"] = new SolidColorBrush(_theme.Hover.For(isLightTheme));
        Resources["ThemePressed"] = new SolidColorBrush(_theme.Pressed.For(isLightTheme));
        Resources["ThemeSeparator"] = new SolidColorBrush(_theme.Separator.For(isLightTheme));
        Resources["ThemeDisabledForeground"] = new SolidColorBrush(_theme.DisabledForeground.For(isLightTheme));
        Resources["ThemeAccent"] = new SolidColorBrush(_theme.Accent.For(isLightTheme));

        Resources["ThemeSecondaryForeground"] = new SolidColorBrush(_theme.SecondaryForeground.For(isLightTheme));
        Resources["ThemeFooterBackground"] = new SolidColorBrush(_theme.FooterBackground.For(isLightTheme));

        // Win11 Settings card background (slightly lighter than body).
        Resources["ThemeCardBackground"] = new SolidColorBrush(_theme.CardBackground.For(isLightTheme));

        // Win11 input control background (text boxes, combo boxes, buttons).
        Resources["ThemeControlBackground"] = new SolidColorBrush(_theme.ControlBackground.For(isLightTheme));

        // Focused TextBox: a shade darker than ThemeControlBackground so the focused state stays visible
        // without collapsing toward the window bg.
        Resources["ThemeTextBoxFocused"] = new SolidColorBrush(_theme.TextBoxFocused.For(isLightTheme));
        Resources["ThemeSliderTrack"] = new SolidColorBrush(_theme.SliderTrack.For(isLightTheme));
        Resources["ThemeSliderProgress"] = new SolidColorBrush(_theme.SliderProgress.For(isLightTheme));
        Resources["ThemeSliderThumb"] = new SolidColorBrush(_theme.SliderThumb.For(isLightTheme));
        Resources["ThemeButtonHover"] = new SolidColorBrush(_theme.ButtonHover.For(isLightTheme));
        Resources["ThemeButtonPressed"] = new SolidColorBrush(_theme.ButtonPressed.For(isLightTheme));
        Resources["ThemeIconForeground"] = new SolidColorBrush(_theme.IconForeground.For(isLightTheme));

        // Chrome brushes for control templates whose triggers used to hardcode hex literals in App.xaml.
        // These are theme-agnostic single-color values
        // promoting to per-theme is a one-line lift (.For(isLightTheme)) if visual design ever requires it.
        Resources["ToggleSwitchOnTrackBrush"] = new SolidColorBrush(_theme.ToggleSwitchOnTrack.Light);
        Resources["ToggleSwitchOnThumbBrush"] = new SolidColorBrush(_theme.ToggleSwitchOnThumb.Light);
        Resources["CloseButtonHoverBrush"] = new SolidColorBrush(_theme.CloseButtonHover.Light);
        Resources["CloseButtonPressedBrush"] = new SolidColorBrush(_theme.CloseButtonPressed.Light);
        Resources["CloseButtonGlyphActiveBrush"] = new SolidColorBrush(_theme.CloseButtonGlyphActive.Light);

        Resources["GlyphSettings"] = _theme.GlyphSettings;

        // Rounded-corners toggle:
        // map every literal radius in XAML to a resource that evaluates to 0 when disabled,
        // and the original visual value when on.
        bool rounded = _appSettings?.EnableRoundedCorners ?? true;
        Resources["CornerRadiusTiny"] = new CornerRadius(rounded ? 1.5 : 0);
        Resources["CornerRadiusSmall"] = new CornerRadius(rounded ? 2 : 0);
        Resources["CornerRadiusScrollThumb"] = new CornerRadius(rounded ? 3 : 0);
        Resources["CornerRadiusScrollThumbExpanded"] = new CornerRadius(rounded ? 7 : 0);
        Resources["CornerRadiusMedium"] = new CornerRadius(rounded ? 4 : 0);
        Resources["CornerRadiusLarge"] = new CornerRadius(rounded ? 6 : 0);
        Resources["CornerRadiusFlyout"] = new CornerRadius(rounded ? 8 : 0);
        Resources["CornerRadiusHuge"] = new CornerRadius(rounded ? 16 : 0);
        Resources["CornerRadiusFooterBottom"] = new CornerRadius(0, 0, rounded ? 8 : 0, rounded ? 8 : 0);
        Resources["CornerRadiusCapsule"] = new CornerRadius(rounded ? 7 : 0);
    }

    private void RequestTrayRefresh() => _trayIconManager?.Update(GetTrayIconAndTooltip);

    private (Icon? icon, string tooltip) GetTrayIconAndTooltip()
    {
        if (_networkRenderer == null || _networkMonitor == null)
            return (null, LocalizationManager.Instance["Tray_Tooltip_Default"]);

        _networkRenderer.State = _networkMonitor.CurrentState;
        Icon icon = _networkRenderer.CreateIcon();
        return (icon, _networkMonitor.GetTooltipText());
    }

    /// <summary>
    /// Surfaces the configured network UI on left-click. Falls back to ms-availablenetworks: if the chosen
    /// path fails (the COM-backed Win10/Win11 paths are undocumented and can vary by build).
    /// </summary>
    private void OpenNetworkFlyout()
    {
        FlyoutStyle flyoutStyle = _appSettings?.FlyoutStyle ?? FlyoutStyle.AvailableNetworks;

        bool success = flyoutStyle switch
        {
            FlyoutStyle.Windows10 => ShellFlyout.ShowNetworkFlyoutWin10(),
            FlyoutStyle.Windows11 => ShellFlyout.ShowControlCenter(),
            FlyoutStyle.QuickSettings => TryOpenUri("ms-actioncenter:controlcenter/&showFooter=true"),
            FlyoutStyle.AvailableNetworks => TryOpenUri("ms-availablenetworks:"),
            FlyoutStyle.Settings => TryOpenUri("ms-settings:network-wifi"),
            _ => false,
        };

        if (!success) TryOpenUri("ms-availablenetworks:");
    }

    private static bool TryOpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void OpenNetworkSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:network",
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }

    private void OpenAdapterSettings()
    {
        AdapterSettingsStyle style = _appSettings?.AdapterSettingsStyle ?? AdapterSettingsStyle.Explorer;

        try
        {
            switch (style)
            {
                case AdapterSettingsStyle.Explorer:
                    AdapterSettingsShellMonitor.OpenAndMonitorExplorerShell();
                    break;
                case AdapterSettingsStyle.ControlPanel:
                    AdapterSettingsShellMonitor.OpenAndMonitorControlPanel();
                    break;
            }
        }
        catch (Exception ex) { WPFLog.Log($"App.OpenAdapterSettings: {ex.Message}"); }
    }

    private void OpenSettings()
    {
        if (_appSettings == null) return;

        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_appSettings);
            _settingsWindow.Closed += OnSettingsWindowClosed;
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs e)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow = null;
        }

        // Aggressive GC after the heavy settings UI is torn down
        // to reclaim memory that would otherwise linger in gen2 for a long-running tray app.
        _ = Task.Delay(TimeConstants.PostSettingsCloseGCDelayMs).ContinueWith(_ =>
        {
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }, TaskScheduler.Default);
    }

    private void ExitApplication()
    {
        // Tear down the global hotkey service first to unregister all WM_HOTKEY bindings
        // so they can't fire into an app that's mid-shutdown.
        if (_hotkeyService != null)
        {
            _hotkeyService.Fired -= OnHotkeyFired;
            try { _hotkeyService.Dispose(); } catch { /* ignore */ }
            _hotkeyService = null;
        }

        _watcherMonitorCts?.Cancel();
        _watcherMonitorCts?.Dispose();
        _watcherMonitorCts = null;

        if (_refreshTimer != null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= OnRefreshTimerTick;
            _refreshTimer = null;
        }

        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        if (_networkMonitor != null)
        {
            _networkMonitor.NetworkStateChanged -= OnNetworkStateChanged;
            _networkMonitor.Dispose();
            _networkMonitor = null;
        }

        _networkRenderer?.Dispose();
        _networkRenderer = null;

        if (_appSettings != null) _appSettings.Changed -= OnSettingsChanged;

        // Close child windows; unsubscribe handlers first so they don't fire mid-shutdown.
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            try { _settingsWindow.Close(); } catch { /* ignore */ }
            _settingsWindow = null;
        }

        if (_theme != null)
        {
            _theme.ThemeChanged -= OnThemeChanged;
            _theme.Dispose();
            _theme = null;
        }

        if (_trayIconManager != null)
        {
            _trayIconManager.LeftClick -= OnTrayLeftClick;
            _trayIconManager.LeftDoubleClick -= OnTrayLeftDoubleClick;
            _trayIconManager.RightClick -= OnTrayRightClick;
            _trayIconManager.RefreshNeeded -= RequestTrayRefresh;
            _trayIconManager.Dispose();
            _trayIconManager = null;
        }

        _contextMenu = null;

        WPFLog.Log("App.ExitApplication: clean exit");
        WPFLog.Flush();
        Shutdown(0);
    }
}
