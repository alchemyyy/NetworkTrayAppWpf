using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetworkTrayAppWPF.Localization;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.WPF.Settings.Utils;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace NetworkTrayAppWPF.WPF.Settings.Pages;

/// <summary>
/// Network settings page. Owns the FlyoutStyle / AdapterSettingsStyle dropdowns and the per-state
/// tray-icon color overrides (Connected / NoInternet / Disconnected). Routes through
/// <see cref="SettingsBindings"/> for the enum combos and reuses the ThemePage swatch / picker
/// pattern for the color rows.
/// </summary>
public partial class NetworkPage : UserControl
{
    // Per-state default color hexes (matching NetworkTrayIconRenderer's built-in defaults).
    // Mirrored here so the swatch fallback color displayed on an "unset" override matches the
    // color the renderer would actually paint.
    private const string DefaultDarkConnected = "#FFFFFF";
    private const string DefaultDarkNoInternet = "#FFB900";
    private const string DefaultDarkDisconnected = "#808080";
    private const string DefaultLightConnected = "#000000";
    private const string DefaultLightNoInternet = "#996600";
    private const string DefaultLightDisconnected = "#666666";

    private AppSettings? _settings;
    private bool _suppressChangeEvents;
    private bool _systemThemeSubscribed;

    // Open color pickers, keyed by (target, isLight) so re-clicking the same swatch focuses the
    // existing picker instead of stacking duplicates that would race the same Temporary slot.
    private readonly Dictionary<(NullableThemeColor Target, bool IsLight), TAWPFColorPicker> _openPickers = [];

    public NetworkPage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    public void LoadFromSettings(AppSettings settings)
    {
        _settings = settings;
        _suppressChangeEvents = true;
        try
        {
            SettingsBindings.SelectComboByTag(FlyoutStyleCombo, settings.FlyoutStyle.ToString());
            SettingsBindings.SelectComboByTag(AdapterSettingsStyleCombo, settings.AdapterSettingsStyle.ToString());

            UpdateColorSwatches();
            UpdateColorSwatchVisibility();
        }
        finally
        {
            _suppressChangeEvents = false;
        }

        // Track system theme flips so swatch visibility follows Windows when ThemeMode is System.
        if (!_systemThemeSubscribed && AppServices.Theme is { } liveTheme)
        {
            liveTheme.ThemeChanged += OnSystemThemeChanged;
            _systemThemeSubscribed = true;
        }
    }

    private void OnSystemThemeChanged(bool isLightTheme) =>
        Dispatcher.BeginInvoke(UpdateColorSwatchVisibility);

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_systemThemeSubscribed && AppServices.Theme is { } liveTheme)
        {
            liveTheme.ThemeChanged -= OnSystemThemeChanged;
            _systemThemeSubscribed = false;
        }
    }

    private bool ResolveEffectiveIsLight()
    {
        if (_settings == null) return AppServices.Theme?.IsLightTheme ?? false;
        return _settings.ThemeMode switch
        {
            Models.ThemeMode.Light => true,
            Models.ThemeMode.Dark => false,
            _ => AppServices.Theme?.IsLightTheme ?? false,
        };
    }

    private void EnumCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null) return;
        SettingsBindings.HandleEnumCombo(
            sender, _settings, SaveAndNotify, () => _suppressChangeEvents, this);
    }

    private NullableThemeColor? ResolveStateColor(string name) => name switch
    {
        "Connected" => _settings?.NetworkConnectedColor,
        "NoInternet" => _settings?.NetworkNoInternetColor,
        "Disconnected" => _settings?.NetworkDisconnectedColor,
        _ => null,
    };

    private static string GetSwatchCardTitle(string name) => name switch
    {
        "Connected" => LocalizationManager.Instance["Settings_Network_ConnectedColor_Title"],
        "NoInternet" => LocalizationManager.Instance["Settings_Network_NoInternetColor_Title"],
        "Disconnected" => LocalizationManager.Instance["Settings_Network_DisconnectedColor_Title"],
        _ => name,
    };

    private static Color GetSwatchFallbackColor(string name, bool isLight)
    {
        string hex = (name, isLight) switch
        {
            ("Connected", true) => DefaultLightConnected,
            ("Connected", false) => DefaultDarkConnected,
            ("NoInternet", true) => DefaultLightNoInternet,
            ("NoInternet", false) => DefaultDarkNoInternet,
            ("Disconnected", true) => DefaultLightDisconnected,
            ("Disconnected", false) => DefaultDarkDisconnected,
            _ => "#000000",
        };
        return (Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)!;
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (sender is not Button { Tag: string spec }) return;

        string[] parts = spec.Split('|');
        if (parts.Length != 2 || ResolveStateColor(parts[0]) is not { } target) return;

        bool isLight = parts[1] == "Light";

        // Re-clicking the same swatch focuses the existing picker.
        if (_openPickers.TryGetValue((target, isLight), out TAWPFColorPicker? existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        Color fallback = GetSwatchFallbackColor(parts[0], isLight);
        Color initial = (isLight ? target.LightColor : target.DarkColor) ?? fallback;
        string variantToken = isLight
            ? LocalizationManager.Instance["Settings_Theme_PickerTitle_LightVariant"]
            : LocalizationManager.Instance["Settings_Theme_PickerTitle_DarkVariant"];
        string title = string.Format(
            LocalizationManager.Instance["Settings_Theme_PickerTitle_Format"],
            GetSwatchCardTitle(parts[0]), variantToken);

        TAWPFColorPicker picker = new(title, hasAlpha: true, initial, defaultColor: fallback)
        {
            Owner = Window.GetWindow(this),
        };

        // Live-preview through Temporary slot so OnSettingsChanged sees the in-flight color
        // without touching LightHex/DarkHex.
        picker.ColorChanged += (_, editedColor) =>
        {
            if (_settings == null) return;

            if (isLight) target.TemporaryLightColor = editedColor;
            else target.TemporaryDarkColor = editedColor;

            UpdateColorSwatches();
        };

        // Auto-save on close: persist whatever color the edit landed on (if dirty), then clear
        // the Temporary slot so display falls through to the saved hex.
        picker.Closed += (s, _) =>
        {
            _openPickers.Remove((target, isLight));
            if (_settings == null) return;

            TAWPFColorPicker closed = (TAWPFColorPicker)s!;
            if (closed.IsDirty)
            {
                Color finalColor = closed.CurrentColor;
                if (isLight) target.LightHex = NullableThemeColor.ToHex(finalColor);
                else target.DarkHex = NullableThemeColor.ToHex(finalColor);
                _settings.Save();
            }

            if (isLight) target.TemporaryLightColor = null;
            else target.TemporaryDarkColor = null;

            UpdateColorSwatches();
        };

        _openPickers[(target, isLight)] = picker;
        picker.Show();
    }

    private void ColorReset_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (sender is not Button { Tag: string name } || ResolveStateColor(name) is not { } target) return;

        target.LightHex = null;
        target.DarkHex = null;
        UpdateColorSwatches();
        _settings.Save();
    }

    private void UpdateColorSwatches()
    {
        if (_settings == null) return;

        UpdateSwatch(ConnectedColorLightSwatch, _settings.NetworkConnectedColor.LightColor, DefaultLightConnected);
        UpdateSwatch(ConnectedColorDarkSwatch, _settings.NetworkConnectedColor.DarkColor, DefaultDarkConnected);
        UpdateSwatch(NoInternetColorLightSwatch, _settings.NetworkNoInternetColor.LightColor, DefaultLightNoInternet);
        UpdateSwatch(NoInternetColorDarkSwatch, _settings.NetworkNoInternetColor.DarkColor, DefaultDarkNoInternet);
        UpdateSwatch(
            DisconnectedColorLightSwatch, _settings.NetworkDisconnectedColor.LightColor, DefaultLightDisconnected);
        UpdateSwatch(
            DisconnectedColorDarkSwatch, _settings.NetworkDisconnectedColor.DarkColor, DefaultDarkDisconnected);
    }

    private void UpdateColorSwatchVisibility()
    {
        bool isLight = ResolveEffectiveIsLight();
        Visibility lightVis = isLight ? Visibility.Visible : Visibility.Collapsed;
        Visibility darkVis = isLight ? Visibility.Collapsed : Visibility.Visible;

        ConnectedColorLightSwatch.Visibility = lightVis;
        ConnectedColorDarkSwatch.Visibility = darkVis;
        NoInternetColorLightSwatch.Visibility = lightVis;
        NoInternetColorDarkSwatch.Visibility = darkVis;
        DisconnectedColorLightSwatch.Visibility = lightVis;
        DisconnectedColorDarkSwatch.Visibility = darkVis;
    }

    private static void UpdateSwatch(Button swatch, Color? color, string fallbackHex)
    {
        if (color.HasValue)
        {
            swatch.Background = new SolidColorBrush(color.Value);
            swatch.Opacity = 1.0;
        }
        else
        {
            Color fallback = (Color)System.Windows.Media.ColorConverter.ConvertFromString(fallbackHex)!;
            swatch.Background = new SolidColorBrush(fallback);
            swatch.Opacity = 0.35;
        }
    }

    private void SaveAndNotify()
    {
        if (_settings == null) return;
        _settings.Save();
        _settings.RaiseChanged();
    }
}
