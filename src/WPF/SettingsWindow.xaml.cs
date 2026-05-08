using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using NetworkTrayAppWPF.Interop;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Services;
using NetworkTrayAppWPF.WPF.Settings.Utils;
using RadioButton = System.Windows.Controls.RadioButton;

namespace NetworkTrayAppWPF.WPF;

public enum SettingsTab
{
    General,
    TrayIcon,
    Network,
    Hotkeys,
    Theme,
    About,
}


public partial class SettingsWindow : Window, IConfirmDialogService, IThemeHost
{
    private readonly AppSettings _settings;

    private HwndSource? _hwndSource;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();
        LoadFromSettings();

        ApplyOuterCornerRadius();

        _settings.Changed += OnAppSettingsChanged;
        SourceInitialized += OnSourceInitialized;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Closed += OnWindowClosed;

        // Land keyboard focus on the currently-checked nav item once the visual tree is realized,
        // so arrow nav works immediately when the window opens.
        ContentRendered += (_, _) =>
        {
            RadioButton? checkedNav = new[] {
                NavGeneral, NavTrayIcon, NavNetwork, NavHotkeys, NavTheme, NavAbout
            }.FirstOrDefault(rb => rb.IsChecked == true);
            checkedNav?.Focus();
        };
    }

    private void OnAppSettingsChanged() => Dispatcher.BeginInvoke(ApplyOuterCornerRadius);

    /// <summary>
    /// Returns true when this window's HWND is the OS-level foreground window.
    /// </summary>
    public bool HasFocus()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        return hwnd != IntPtr.Zero && hwnd == User32.GetForegroundWindow();
    }

    /// <summary>
    /// Drives the outer window chrome + root border corner radius from AppSettings.
    /// Kept imperative (rather than DynamicResource) because WindowChrome is a bare DependencyObject
    /// and resource resolution against it is unreliable.
    /// Also re-applies the DWM corner preference, which overrides WindowChrome on Win11
    /// and is the only knob that actually rounds the outermost window edge.
    /// </summary>
    private void ApplyOuterCornerRadius()
    {
        double r = _settings.EnableRoundedCorners ? 8 : 0;
        CornerRadius radius = new(r);

        System.Windows.Shell.WindowChrome? chrome =
            System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CornerRadius = radius;

        RootBorder.CornerRadius = radius;
        ApplyDwmRoundedCorners();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (_hwndSource != null)
        {
            _hwndSource.RemoveHook(WindowProcHook);
            _hwndSource = null;
        }

        _settings.Changed -= OnAppSettingsChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        ApplyDwmDarkMode();
        ApplyDwmRoundedCorners();
        UpdateMaximizeGlyph();

        // Force MA_ACTIVATE on inactive-window clicks so the first click that activates the SettingsWindow
        // ALSO reaches WPF input. Without this, custom-chrome modeless windows can occasionally see the
        // OS return MA_ACTIVATEANDEAT, which swallows the click.
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
        {
            _hwndSource = HwndSource.FromHwnd(hwnd);
            _hwndSource?.AddHook(WindowProcHook);
        }
    }

    private IntPtr WindowProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(User32.MA_ACTIVATE);
        }
        return IntPtr.Zero;
    }

    public void ApplyDwmDarkMode()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Dark title bar on Win11; matching the app's effective theme
            bool isLight = ResolveEffectiveIsLight();
            int value = isLight ? 0 : 1;
            DWMAPI.DwmSetWindowAttribute(hwnd, DWMAPI.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch
        {
            // DWM call may fail on older Windows; non-fatal.
        }
    }

    private void ApplyDwmRoundedCorners()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int value = _settings.EnableRoundedCorners ? DWMAPI.DWMWCP_ROUND : DWMAPI.DWMWCP_DONOTROUND;
            DWMAPI.DwmSetWindowAttribute(hwnd, DWMAPI.DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        }
        catch
        {
            // DWM call may fail on older Windows; non-fatal.
        }
    }

    private bool ResolveEffectiveIsLight()
    {
        return _settings.ThemeMode switch
        {
            Models.ThemeMode.Light => true,
            Models.ThemeMode.Dark => false,
            _ => AppServices.Theme?.IsLightTheme ?? false,
        };
    }

    private void UpdateMaximizeGlyph()
    {
        // ChromeMaximize \uE922 vs ChromeRestore \uE923 (Segoe Fluent Icons).
        MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    public void SelectTab(SettingsTab tab)
    {
        RadioButton? target = tab switch
        {
            SettingsTab.General => NavGeneral,
            SettingsTab.TrayIcon => NavTrayIcon,
            SettingsTab.Network => NavNetwork,
            SettingsTab.Hotkeys => NavHotkeys,
            SettingsTab.Theme => NavTheme,
            SettingsTab.About => NavAbout,
            _ => null,
        };
        if (target != null) target.IsChecked = true;
    }

    private void LoadFromSettings()
    {
        // Per-section pages own their own load - see <Page>.LoadFromSettings.
        GeneralSection.LoadFromSettings(_settings);
        TrayIconSection.LoadFromSettings(_settings);
        NetworkSection.LoadFromSettings(_settings);
        HotkeysSection.LoadFromSettings(_settings);
        ThemeSection.LoadFromSettings(_settings, this);
    }

    private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
    {
        string folder = Path.GetDirectoryName(AppSettings.GetDefaultPath()) ?? string.Empty;
        if (string.IsNullOrEmpty(folder)) return;

        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true,
        });
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        GeneralSection.Visibility = Visibility.Collapsed;
        TrayIconSection.Visibility = Visibility.Collapsed;
        NetworkSection.Visibility = Visibility.Collapsed;
        HotkeysSection.Visibility = Visibility.Collapsed;
        ThemeSection.Visibility = Visibility.Collapsed;
        AboutSection.Visibility = Visibility.Collapsed;

        switch (tag)
        {
            case "General": GeneralSection.Visibility = Visibility.Visible; break;
            case "TrayIcon": TrayIconSection.Visibility = Visibility.Visible; break;
            case "Network": NetworkSection.Visibility = Visibility.Visible; break;
            case "Hotkeys": HotkeysSection.Visibility = Visibility.Visible; break;
            case "Theme": ThemeSection.Visibility = Visibility.Visible; break;
            case "About": AboutSection.Visibility = Visibility.Visible; break;
        }
    }

    /// <summary>
    /// In-flight confirm prompt. The overlay only supports one prompt at a time; a second call
    /// while a prompt is open auto-cancels the previous one so the new prompt's task is the only
    /// one a caller is awaiting.
    /// </summary>
    private TaskCompletionSource<bool>? _confirmDialogTcs;

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText)
    {
        // Auto-cancel any earlier prompt that's still awaiting a click. The overlay UI is single-instance,
        // so a second open call would otherwise leave the first awaiter stuck forever.
        _confirmDialogTcs?.TrySetResult(false);

        ConfirmOverlayTitle.Text = title;
        ConfirmOverlayMessage.Text = message;
        ConfirmOverlayConfirmButton.Content = confirmText;
        ConfirmOverlayCancelButton.Content = cancelText;
        ConfirmOverlay.Visibility = Visibility.Visible;

        TaskCompletionSource<bool> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _confirmDialogTcs = tcs;
        return tcs.Task;
    }

    private void ConfirmOverlayConfirm_Click(object sender, RoutedEventArgs e)
    {
        TaskCompletionSource<bool>? tcs = _confirmDialogTcs;
        _confirmDialogTcs = null;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        tcs?.TrySetResult(true);
    }

    private void ConfirmOverlayCancel_Click(object sender, RoutedEventArgs e)
    {
        TaskCompletionSource<bool>? tcs = _confirmDialogTcs;
        _confirmDialogTcs = null;
        ConfirmOverlay.Visibility = Visibility.Collapsed;
        tcs?.TrySetResult(false);
    }


    private void SaveAndNotify()
    {
        _settings.Save();
        _settings.RaiseChanged();
    }

}
