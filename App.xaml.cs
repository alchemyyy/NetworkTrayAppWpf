using Microsoft.Win32;
using NetworkTrayAppWpf.Interop;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace NetworkTrayAppWpf;

/// <summary>
/// Network Tray Icon Application
/// Uses software rendering and custom Win32 APIs instead of heavy UI libraries.
/// </summary>
public partial class App
{
    private ShellNotifyIcon? _trayIcon;
    private NetworkMonitor? _networkMonitor;
    private TrayIconRenderer? _iconRenderer;
    private AppSettings? _settings;
    private DispatcherTimer? _refreshTimer;
    private SettingsFlyout? _settingsFlyout;
    private ThemeManager? _themeManager;
    private ContextMenu? _contextMenu;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // CRITICAL: Enable software-only rendering to reduce memory footprint
        // This is the single most important optimization from EarTrumpet
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Set up exception handlers for crash handler support
        AppDomain.CurrentDomain.UnhandledException += (_, _) => Environment.Exit(1);
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            Environment.Exit(1);
        };

        _settings = AppSettings.Load();
        _iconRenderer = new TrayIconRenderer(_settings);
        _networkMonitor = new NetworkMonitor();

        // Initialize lightweight theme manager
        _themeManager = new ThemeManager();
        _iconRenderer.IsLightTheme = _themeManager.IsLightTheme;
        _themeManager.ThemeChanged += OnThemeChanged;
        UpdateThemeResources(_themeManager.IsLightTheme);

        // Subscribe to display settings changes
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        CreateTrayIcon();

        _networkMonitor.NetworkStateChanged += OnNetworkStateChanged;
        _networkMonitor.Initialize();

        // Polling fallback every 3 seconds
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _refreshTimer.Tick += (_, _) => _networkMonitor?.RefreshState();
        _refreshTimer.Start();

        UpdateTrayIcon();
    }

    private void CreateTrayIcon()
    {
        _contextMenu = CreateContextMenu();

        _trayIcon = new ShellNotifyIcon();
        _trayIcon.LeftClick += OnTrayLeftClick;
        _trayIcon.RightClick += OnTrayRightClick;

        UpdateTrayIcon();
        _trayIcon.IsVisible = true;
    }

    private ContextMenu CreateContextMenu()
    {
        ContextMenu contextMenu = new();

        MenuItem networkSettingsItem = new() { Header = "Network Settings" };
        networkSettingsItem.Click += (_, _) => OpenNetworkSettings();

        MenuItem adapterSettingsItem = new() { Header = "Adapter Settings" };
        adapterSettingsItem.Click += (_, _) => OpenAdapterSettings();

        MenuItem appSettingsItem = new() { Header = "App Settings" };
        appSettingsItem.Click += (_, _) => OpenAppSettings();

        MenuItem exitItem = new() { Header = "Exit" };
        exitItem.Click += (_, _) => ExitApplication();

        contextMenu.Items.Add(networkSettingsItem);
        contextMenu.Items.Add(adapterSettingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(appSettingsItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exitItem);

        // Apply theme to context menu
        ApplyContextMenuTheme(contextMenu);

        return contextMenu;
    }

    private void ApplyContextMenuTheme(ContextMenu menu)
    {
        bool isLight = _themeManager?.IsLightTheme ?? false;

        menu.Background = new SolidColorBrush(isLight
            ? ThemeManager.LightBackground
            : ThemeManager.DarkBackground);
        menu.Foreground = new SolidColorBrush(isLight
            ? ThemeManager.LightForeground
            : ThemeManager.DarkForeground);
        menu.BorderBrush = new SolidColorBrush(isLight
            ? ThemeManager.LightBorder
            : ThemeManager.DarkBorder);

        foreach (object item in menu.Items)
        {
            if (item is MenuItem menuItem)
            {
                menuItem.Foreground = menu.Foreground;
            }
            else if (item is Separator separator)
            {
                separator.Background = new SolidColorBrush(isLight
                    ? ThemeManager.LightSeparator
                    : ThemeManager.DarkSeparator);
            }
        }
    }

    private void OnTrayLeftClick()
    {
        Dispatcher.BeginInvoke(OpenNetworkFlyout);
    }

    private void OnTrayRightClick(Point point)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_contextMenu != null && _trayIcon != null)
            {
                ApplyContextMenuTheme(_contextMenu);
                _trayIcon.ShowContextMenu(_contextMenu, point);
            }
        });
    }

    private void OnNetworkStateChanged(NetworkIconState state)
    {
        Dispatcher.BeginInvoke(UpdateTrayIcon);
    }

    private void OnThemeChanged(bool isLightTheme)
    {
        Dispatcher.BeginInvoke(() =>
        {
            UpdateThemeResources(isLightTheme);
            if (_iconRenderer != null)
            {
                _iconRenderer.IsLightTheme = isLightTheme;
                UpdateTrayIcon();
            }
        });
    }

    private void UpdateThemeResources(bool isLightTheme)
    {
        Resources["ThemeBackground"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightBackground
            : ThemeManager.DarkBackground);
        Resources["ThemeForeground"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightForeground
            : ThemeManager.DarkForeground);
        Resources["ThemeBorder"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightBorder
            : ThemeManager.DarkBorder);
        Resources["ThemeHover"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightHover
            : ThemeManager.DarkHover);
        Resources["ThemePressed"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightPressed
            : ThemeManager.DarkPressed);
        Resources["ThemeSeparator"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightSeparator
            : ThemeManager.DarkSeparator);
        Resources["ThemeControlBackground"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightControlBackground
            : ThemeManager.DarkControlBackground);
        Resources["ThemeControlBorder"] = new SolidColorBrush(isLightTheme
            ? ThemeManager.LightControlBorder
            : ThemeManager.DarkControlBorder);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Display settings changed - update icon
        Dispatcher.BeginInvoke(UpdateTrayIcon);
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null || _networkMonitor == null || _iconRenderer == null)
            return;

        NetworkIconState state = _networkMonitor.CurrentState;

        // Create icon with layered rendering
        _trayIcon.SetIcon(_iconRenderer.CreateIcon(state));
        _trayIcon.SetTooltip(_networkMonitor.GetTooltipText());
    }

    private void OpenNetworkFlyout()
    {
#if DEBUG
        // Debug mode: simulate a crash to test the crash handler
        Environment.Exit(42);
#else
        FlyoutStyle flyoutStyle = _settings?.Tray.FlyoutStyle ?? FlyoutStyle.AvailableNetworks;

        bool success = flyoutStyle switch
        {
            FlyoutStyle.Windows10 => ShellFlyout.ShowNetworkFlyoutWin10(),
            FlyoutStyle.Windows11 => ShellFlyout.ShowControlCenter(),
            FlyoutStyle.QuickSettings => TryOpenUri("ms-actioncenter:controlcenter/&showFooter=true"),
            FlyoutStyle.AvailableNetworks => TryOpenUri("ms-availablenetworks:"),
            FlyoutStyle.Settings => TryOpenUri("ms-settings:network-wifi"),
            _ => false
        };

        if (!success)
        {
            TryOpenUri("ms-availablenetworks:");
        }
#endif
    }

    private static bool TryOpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
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
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore
        }
    }

    private void OpenAppSettings()
    {
        // Close existing settings flyout if open
        if (_settingsFlyout != null)
        {
            try
            {
                _settingsFlyout.Closed -= OnSettingsFlyoutClosed;
                _settingsFlyout.Close();
            }
            catch
            {
                // Ignored
            }
            _settingsFlyout = null;
        }

        if (_settings == null || _themeManager == null) return;

        _settingsFlyout = new SettingsFlyout(_settings, _themeManager);
        _settingsFlyout.Closed += OnSettingsFlyoutClosed;
        _settingsFlyout.Show();
    }

    private void OnSettingsFlyoutClosed(object? sender, EventArgs args)
    {
        if (_settingsFlyout != null)
        {
            _settingsFlyout.Closed -= OnSettingsFlyoutClosed;
            _settingsFlyout = null;
        }

        // Refresh icon in case colors changed
        UpdateTrayIcon();
    }

    private void OpenAdapterSettings()
    {
        AdapterSettingsStyle style = _settings?.Tray.AdapterSettingsStyle ?? AdapterSettingsStyle.Explorer;

        try
        {
            switch (style)
            {
                case AdapterSettingsStyle.Explorer:
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "shell:::{7007ACC7-3202-11D1-AAD2-00805FC1270E}",
                        UseShellExecute = true
                    });
                    break;

                case AdapterSettingsStyle.ControlPanel:
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "ncpa.cpl",
                        UseShellExecute = true
                    });
                    break;
            }
        }
        catch
        {
            // Ignore
        }
    }

    private void ExitApplication()
    {
        _refreshTimer?.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _themeManager?.Dispose();
        _networkMonitor?.Dispose();
        _iconRenderer?.Dispose();
        _trayIcon?.Dispose();
        Shutdown(0);
    }
}
