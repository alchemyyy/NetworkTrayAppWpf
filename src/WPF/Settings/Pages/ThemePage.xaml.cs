using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NetworkTrayAppWPF.Localization;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Visuals;
using NetworkTrayAppWPF.WPF.Settings.Utils;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace NetworkTrayAppWPF.WPF.Settings.Pages;

/// <summary>
/// Theme settings page.
/// Owns its UI plus the color-swatch / reset click handlers.
/// Routes generic Tag-based ToggleSwitch / ComboBox mutations through <see cref="SettingsBindings"/>.
/// Theme-mode changes dispatch through an <see cref="IThemeHost"/> seam
/// so the shell can re-apply the DWM dark-mode title-bar attribute against its own HWND
/// - the page never reaches into the host window's chrome.
/// </summary>
public partial class ThemePage : UserControl
{
    private AppSettings? _settings;
    private IThemeHost? _themeHost;
    private bool _suppressChangeEvents;
    private bool _systemThemeSubscribed;

    // Theme palette source for the swatch "unset" fallback colors. Pulled from the App-owned
    // AppTheme via the same service-locator slot SettingsWindow uses, so a fresh fallback hex
    // always reflects the loaded theme.xml rather than a duplicated set of compile-time defaults.
    private static AppTheme? Theme => AppServices.Theme;

    // Open color pickers, keyed by (theme color object, isLight side).
    // Modeless lifecycle: re-clicking the same swatch must focus the existing picker
    // instead of stacking duplicates that fight over the same Temporary slot.
    // Pickers persist across tab switches; they close when the user X's them or the owning settings window closes.
    private readonly Dictionary<(NullableThemeColor Target, bool IsLight), TAWPFColorPicker> _openPickers = [];

    private static readonly Dictionary<string, Action<ThemePage>> EnumComboPostActions = new()
    {
        ["ThemeMode"] = p =>
        {
            p._themeHost?.ApplyDwmDarkMode();
            p.UpdateColorSwatchVisibility();
        },
    };

    public ThemePage()
    {
        InitializeComponent();
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// Injects the AppSettings instance plus the host callback for DWM dark-mode updates and seeds
    /// every control's value. The shell calls this from its own LoadFromSettings; subsequent calls
    /// re-seed the page (used when settings are reloaded externally).
    /// </summary>
    public void LoadFromSettings(AppSettings settings, IThemeHost themeHost)
    {
        _settings = settings;
        _themeHost = themeHost;
        _suppressChangeEvents = true;
        try
        {
            SettingsBindings.BindSpinner(
                ContextMenuFontSizeBox,
                () => settings.ContextMenuFontSize,
                v => settings.ContextMenuFontSize = v,
                () => _suppressChangeEvents,
                SaveAndNotify);

            SettingsBindings.SelectComboByTag(ThemeModeCombo, settings.ThemeMode.ToString());
            RoundedCornersToggle.IsChecked = settings.EnableRoundedCorners;

            UpdateColorSwatches();
            UpdateColorSwatchVisibility();
        }
        finally
        {
            _suppressChangeEvents = false;
        }

        // Track system theme flips so swatch visibility follows Windows when ThemeMode is System.
        // Idempotent: only attaches once per page lifetime; the Unloaded handler tears it down.
        if (!_systemThemeSubscribed && Theme is { } liveTheme)
        {
            liveTheme.ThemeChanged += OnSystemThemeChanged;
            _systemThemeSubscribed = true;
        }
    }

    private void OnSystemThemeChanged(bool isLightTheme) =>
        Dispatcher.BeginInvoke(UpdateColorSwatchVisibility);

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_systemThemeSubscribed && Theme is { } liveTheme)
        {
            liveTheme.ThemeChanged -= OnSystemThemeChanged;
            _systemThemeSubscribed = false;
        }
    }

    // Effective light/dark side under the user's ThemeMode override - mirrors the resolution in
    // SettingsWindow.ResolveEffectiveIsLight / App.ResolveEffectiveIsLightTheme so the visible swatch
    // pair matches the theme that's actually painted everywhere else in the app.
    private bool ResolveEffectiveIsLight()
    {
        if (_settings == null) return Theme?.IsLightTheme ?? false;
        return _settings.ThemeMode switch
        {
            Models.ThemeMode.Light => true,
            Models.ThemeMode.Dark => false,
            _ => Theme?.IsLightTheme ?? false,
        };
    }

    private void BoolToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        SettingsBindings.HandleBoolToggle(sender, _settings, SaveAndNotify, () => _suppressChangeEvents);
    }

    private void EnumCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null) return;
        SettingsBindings.HandleEnumCombo(
            sender, _settings, SaveAndNotify, () => _suppressChangeEvents, this, EnumComboPostActions);
    }

    // --- Color pickers ---
    // Two handlers cover every swatch/reset button. Each XAML button carries its target via Tag:
    // "Text|Light" / "Text|Dark" for swatches, "Text" for reset.

    private NullableThemeColor? ResolveThemeColor(string name) => name switch
    {
        "Text" => _settings?.TextColor,
        "Background" => _settings?.BackgroundColor,
        "TrayIcon" => _settings?.TrayIconColor,
        _ => null,
    };

    /// <summary>
    /// Maps a swatch tag to the displayed Title of the SettingsCard the swatch lives in.
    /// Used as the picker's titlebar prefix so the window header echoes the card the user clicked
    /// from instead of the internal tag name.
    /// </summary>
    private static string GetSwatchCardTitle(string name) => name switch
    {
        "Text" => LocalizationManager.Instance["Settings_Theme_TextColor_Title"],
        "Background" => LocalizationManager.Instance["Settings_Theme_BackgroundColor_Title"],
        "TrayIcon" => LocalizationManager.Instance["Settings_Theme_StaticIconColor_Title"],
        _ => name,
    };

    /// <summary>
    /// Per-swatch fallback color used both as the dimmed "unset" swatch background in
    /// <see cref="UpdateColorSwatches"/> and as the picker's seed color when the user opens
    /// the picker on an unset swatch - so the picker reflects what the user currently sees.
    /// Sourced from the live <see cref="AppTheme"/> instance so user-modified theme.xml values
    /// flow through. Falls back to opaque black when the theme isn't loaded yet (shouldn't happen
    /// in normal app lifetime - the Settings UI opens after AppTheme initialization).
    /// </summary>
    private static Color GetSwatchFallbackColor(string name, bool isLight)
    {
        AppTheme? theme = Theme;
        if (theme == null) return Color.FromRgb(0, 0, 0);

        return name switch
        {
            "Text" => theme.Foreground.For(isLight),
            "Background" => theme.Background.For(isLight),
            "TrayIcon" => theme.Foreground.For(isLight),
            _ => theme.Foreground.For(isLight),
        };
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (sender is not Button { Tag: string spec }) return;

        string[] parts = spec.Split('|');
        if (parts.Length != 2 || ResolveThemeColor(parts[0]) is not { } target) return;

        bool isLight = parts[1] == "Light";

        // Re-clicking the same swatch focuses the existing picker - opening a duplicate would let two
        // pickers race the same Temporary slot, and the user already has one parked nearby.
        if (_openPickers.TryGetValue((target, isLight), out TAWPFColorPicker? existing))
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }

        // Seed the picker with the same color the swatch is currently showing.
        // For an unset (LightHex/DarkHex == null) override the swatch displays a per-swatch fallback;
        // mirror that here so the picker doesn't open at opaque black for an "unset" pick.
        Color fallback = GetSwatchFallbackColor(parts[0], isLight);
        Color initial = (isLight ? target.LightColor : target.DarkColor) ?? fallback;
        string variantToken = isLight
            ? LocalizationManager.Instance["Settings_Theme_PickerTitle_LightVariant"]
            : LocalizationManager.Instance["Settings_Theme_PickerTitle_DarkVariant"];
        string title = string.Format(
            LocalizationManager.Instance["Settings_Theme_PickerTitle_Format"],
            GetSwatchCardTitle(parts[0]), variantToken);

        // Default button loads the per-swatch theme fallback so the user can always get back to
        // "what the swatch looks like when unset" without remembering the hex.
        TAWPFColorPicker picker = new(title, hasAlpha: true, initial, defaultColor: fallback)
        {
            Owner = Window.GetWindow(this),
        };

        // Live-preview: every edit flows through the Temporary slot so Resolve()-based consumers
        // (App.OnSettingsChanged -> brush rebuild, swatch refresh) see the in-flight color through
        // the same code path as a committed value, without touching LightHex/DarkHex.
        // The Temporary* setter auto-fires AppSettings.Changed via the wired callback.
        picker.ColorChanged += (_, editedColor) =>
        {
            if (_settings == null) return;

            if (isLight) target.TemporaryLightColor = editedColor;
            else target.TemporaryDarkColor = editedColor;

            UpdateColorSwatches();
        };

        // Auto-save on close: when the user dismisses the picker, persist whatever color the
        // edit landed on (if it differs from the session baseline) into the hex slot, then clear
        // the Temporary override so display falls through to the saved hex. A clean close (no
        // edits, or edits Reset back to baseline) leaves LightHex/DarkHex untouched - so opening
        // and closing on an "unset" swatch without changing anything keeps it unset.
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
        if (sender is not Button { Tag: string name } || ResolveThemeColor(name) is not { } target) return;

        target.LightHex = null;
        target.DarkHex = null;
        UpdateColorSwatches();
        _settings.Save();
    }

    private void UpdateColorSwatches()
    {
        if (_settings == null) return;
        AppTheme? theme = Theme;
        if (theme == null) return;

        UpdateSwatch(TextColorLightSwatch, _settings.TextColor.LightColor,
            fallbackHex: ToFallbackHex(theme.Foreground.Light));
        UpdateSwatch(TextColorDarkSwatch, _settings.TextColor.DarkColor,
            fallbackHex: ToFallbackHex(theme.Foreground.Dark));
        UpdateSwatch(BackgroundColorLightSwatch, _settings.BackgroundColor.LightColor,
            fallbackHex: ToFallbackHex(theme.Background.Light));
        UpdateSwatch(BackgroundColorDarkSwatch, _settings.BackgroundColor.DarkColor,
            fallbackHex: ToFallbackHex(theme.Background.Dark));
        UpdateSwatch(TrayIconColorLightSwatch, _settings.TrayIconColor.LightColor,
            fallbackHex: ToFallbackHex(theme.Foreground.Light));
        UpdateSwatch(TrayIconColorDarkSwatch, _settings.TrayIconColor.DarkColor,
            fallbackHex: ToFallbackHex(theme.Foreground.Dark));
    }

    private static string ToFallbackHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    // Show only the swatch pair for the currently effective theme side.
    // The user can only paint the surfaces they're seeing, so collapsing the off-side swatches
    // removes a redundant click and keeps the page aligned with the live appearance.
    // Both Light/Dark hex values still persist independently behind the scenes - flipping ThemeMode
    // (or the system theme, in System mode) reveals the other half without losing data.
    private void UpdateColorSwatchVisibility()
    {
        bool isLight = ResolveEffectiveIsLight();
        Visibility lightVis = isLight ? Visibility.Visible : Visibility.Collapsed;
        Visibility darkVis = isLight ? Visibility.Collapsed : Visibility.Visible;

        TextColorLightSwatch.Visibility = lightVis;
        TextColorDarkSwatch.Visibility = darkVis;
        BackgroundColorLightSwatch.Visibility = lightVis;
        BackgroundColorDarkSwatch.Visibility = darkVis;
        TrayIconColorLightSwatch.Visibility = lightVis;
        TrayIconColorDarkSwatch.Visibility = darkVis;
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
