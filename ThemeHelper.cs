using Microsoft.Win32;

namespace NetworkTrayAppWpf;

/// <summary>
/// Detects Windows taskbar theme (light/dark) and raises events on change.
/// </summary>
internal sealed class ThemeHelper : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";

    private bool _disposed;
    private bool _lastKnownIsLightTheme;

    public event Action<bool>? ThemeChanged;

    /// <summary>
    /// Returns true if the taskbar is using light theme.
    /// </summary>
    public bool IsTaskbarLightTheme { get; private set; }

    public ThemeHelper()
    {
        IsTaskbarLightTheme = DetectTaskbarLightTheme();
        _lastKnownIsLightTheme = IsTaskbarLightTheme;

        // Subscribe to system preference changes
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Theme changes come through as General category
        if (e.Category == UserPreferenceCategory.General)
        {
            bool newIsLightTheme = DetectTaskbarLightTheme();
            if (newIsLightTheme != _lastKnownIsLightTheme)
            {
                _lastKnownIsLightTheme = newIsLightTheme;
                IsTaskbarLightTheme = newIsLightTheme;
                ThemeChanged?.Invoke(newIsLightTheme);
            }
        }
    }

    private static bool DetectTaskbarLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            object? value = key?.GetValue(SystemUsesLightTheme);
            if (value is int intValue)
            {
                return intValue == 1;
            }
        }
        catch
        {
            // Default to dark theme on failure
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
