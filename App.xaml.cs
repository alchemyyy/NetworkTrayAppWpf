using H.NotifyIcon;
using ModernWpf;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace NetworkTrayAppWpf;

public partial class App
{
    private TaskbarIcon? _trayIcon;
    private NetworkMonitor? _networkMonitor;
    private IconProvider? _iconProvider;
    private AppSettings? _settings;
    private DispatcherTimer? _refreshTimer;
    private SettingsWindow? _settingsWindow;
    private ThemeHelper? _themeHelper;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = AppSettings.Load();
        _iconProvider = new IconProvider(_settings);
        _networkMonitor = new NetworkMonitor();

        // Initialize theme detection
        _themeHelper = new ThemeHelper();
        _iconProvider.IsLightTheme = _themeHelper.IsTaskbarLightTheme;
        _themeHelper.ThemeChanged += OnThemeChanged;

        // Enable dark mode for any Win32 elements
        //NativeMethods.EnableDarkModeForApp();

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

        // Pre-warm context menu after initial render to avoid first-open delay
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, PreWarmContextMenu);
    }

    private void PreWarmContextMenu()
    {
        if (_trayIcon?.ContextMenu == null)
            return;

        ContextMenu menu = _trayIcon.ContextMenu;

        try
        {
            // Store original values
            double originalOpacity = menu.Opacity;

            // Make menu invisible during pre-warm
            menu.Opacity = 0;

            // Position off-screen to avoid any visual flash
            menu.Placement = PlacementMode.Absolute;
            menu.HorizontalOffset = -10000;
            menu.VerticalOffset = -10000;

            // Open the menu to force visual tree creation and template instantiation
            menu.IsOpen = true;

            // Force layout to complete - this triggers all lazy loading
            menu.UpdateLayout();

            // Close immediately
            menu.IsOpen = false;

            // Restore original opacity
            menu.Opacity = originalOpacity;

            // Reset placement for normal operation
            menu.ClearValue(ContextMenu.PlacementProperty);
            menu.ClearValue(ContextMenu.HorizontalOffsetProperty);
            menu.ClearValue(ContextMenu.VerticalOffsetProperty);
        }
        catch
        {
            // Ignore pre-warm failures - menu will just load on first click
        }
    }

    private void CreateTrayIcon()
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

        _trayIcon = new TaskbarIcon
        {
            ContextMenu = contextMenu,
            ToolTipText = "Network",
            LeftClickCommand = new RelayCommand(OpenNetworkFlyout)
        };

        // Set initial icon
        UpdateTrayIcon();

        // Force the icon to appear in the system tray
        _trayIcon.ForceCreate();
    }

    private void OnNetworkStateChanged(NetworkIconState state)
    {
        Dispatcher.BeginInvoke(UpdateTrayIcon);
    }

    private void OnThemeChanged(bool isLightTheme)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_iconProvider != null)
            {
                _iconProvider.IsLightTheme = isLightTheme;
                UpdateTrayIcon();
            }

            // Update ModernWpf theme for context menu
            if (_trayIcon?.ContextMenu != null)
            {
                ThemeManager.SetRequestedTheme(
                    _trayIcon.ContextMenu,
                    isLightTheme ? ElementTheme.Light : ElementTheme.Dark);
            }
        });
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null || _networkMonitor == null || _iconProvider == null)
            return;

        NetworkIconState state = _networkMonitor.CurrentState;

        // Create icon from glyph
        _trayIcon.IconSource = CreateIconSource(state);
        _trayIcon.ToolTipText = _networkMonitor.GetTooltipText();
    }

    private GeneratedIconSource CreateIconSource(NetworkIconState state)
    {
        return new GeneratedIconSource
        {
            Text = IconProvider.GetGlyph(state),
            Foreground = _iconProvider!.GetBrush(state),
            FontFamily = IconProvider.IconFontFamily,
            FontSize = 64
        };
    }

    private void OpenNetworkFlyout()
    {
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
        if (_settingsWindow != null)
        {
            try
            {
                _settingsWindow.Closed -= OnSettingsWindowClosed;
                _settingsWindow.Close();
            }
            catch
            {
                // ignored
            }
            _settingsWindow = null;
        }

        if (_settings == null) return;

        _settingsWindow = new SettingsWindow(_settings);
        _settingsWindow.Closed += OnSettingsWindowClosed;
        _settingsWindow.Show();
    }

    private void OnSettingsWindowClosed(object? sender, EventArgs args)
    {
        if (_settingsWindow != null)
        {
            _settingsWindow.Closed -= OnSettingsWindowClosed;
            _settingsWindow = null;
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
        _themeHelper?.Dispose();
        _networkMonitor?.Dispose();
        _trayIcon?.Dispose();
        Shutdown();
    }
}
