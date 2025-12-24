using Microsoft.Win32;
using Color = System.Windows.Media.Color;
using Colors = System.Windows.Media.Colors;

namespace NetworkTrayAppWpf;

/// <summary>
/// Lightweight theme manager for detecting system theme and providing theme colors.
/// Replaces the heavy ModernWpfUI theme system.
/// </summary>
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private bool _disposed;
    private bool _lastKnownIsLightTheme;

    public event Action<bool>? ThemeChanged;

    public bool IsLightTheme { get; private set; }

    // Theme colors
    public static Color DarkBackground => Color.FromRgb(0x20, 0x20, 0x20);
    public static Color LightBackground => Color.FromRgb(0xF3, 0xF3, 0xF3);

    public static Color DarkForeground => Colors.White;
    public static Color LightForeground => Colors.Black;

    public static Color DarkBorder => Color.FromRgb(0x45, 0x45, 0x45);
    public static Color LightBorder => Color.FromRgb(0xE0, 0xE0, 0xE0);

    public static Color DarkSeparator => Color.FromRgb(0x3A, 0x3A, 0x3A);
    public static Color LightSeparator => Color.FromRgb(0xE5, 0xE5, 0xE5);

    public static Color DarkHover => Color.FromRgb(0x33, 0x33, 0x33);
    public static Color LightHover => Color.FromRgb(0xE9, 0xE9, 0xE9);

    public static Color DarkPressed => Color.FromRgb(0x2A, 0x2A, 0x2A);
    public static Color LightPressed => Color.FromRgb(0xDF, 0xDF, 0xDF);

    // Control colors (ComboBox, TextBox, etc.)
    public static Color DarkControlBackground => Color.FromRgb(0x33, 0x33, 0x33);
    public static Color LightControlBackground => Colors.White;

    public static Color DarkControlBorder => Color.FromRgb(0x44, 0x44, 0x44);
    public static Color LightControlBorder => Color.FromRgb(0x80, 0x80, 0x80);

    // Acrylic colors (with transparency)
    public static Color DarkAcrylic => Color.FromArgb(0xD0, 0x20, 0x20, 0x20);
    public static Color LightAcrylic => Color.FromArgb(0xD0, 0xF3, 0xF3, 0xF3);

    public Color Background => IsLightTheme ? LightBackground : DarkBackground;
    public Color Foreground => IsLightTheme ? LightForeground : DarkForeground;
    public Color Border => IsLightTheme ? LightBorder : DarkBorder;
    public Color Separator => IsLightTheme ? LightSeparator : DarkSeparator;
    public Color Hover => IsLightTheme ? LightHover : DarkHover;
    public Color Pressed => IsLightTheme ? LightPressed : DarkPressed;
    public Color Acrylic => IsLightTheme ? LightAcrylic : DarkAcrylic;
    public Color ControlBackground => IsLightTheme ? LightControlBackground : DarkControlBackground;
    public Color ControlBorder => IsLightTheme ? LightControlBorder : DarkControlBorder;

    public ThemeManager()
    {
        IsLightTheme = DetectSystemLightTheme();
        _lastKnownIsLightTheme = IsLightTheme;

        // Subscribe to system preference changes
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Theme changes come through as General category
        if (e.Category == UserPreferenceCategory.General)
        {
            bool newIsLightTheme = DetectSystemLightTheme();
            if (newIsLightTheme != _lastKnownIsLightTheme)
            {
                _lastKnownIsLightTheme = newIsLightTheme;
                IsLightTheme = newIsLightTheme;
                ThemeChanged?.Invoke(newIsLightTheme);
            }
        }
    }

    private static bool DetectSystemLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue("SystemUsesLightTheme");
            return value is 1;
        }
        catch
        {
            return false; // Default to dark theme
        }
    }

    public static bool DetectAppsLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is 1;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
