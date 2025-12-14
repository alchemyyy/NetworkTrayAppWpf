using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NetworkTrayAppWpf;

public partial class SettingsWindow : Window
{
    // Windows 11 style colors
    private static readonly Color DarkBackground = Color.FromRgb(0x20, 0x20, 0x20);
    private static readonly Color LightBackground = Color.FromRgb(0xF3, 0xF3, 0xF3);

    private readonly AppSettings _settings;
    private bool _isInitializing = true;
    private bool _isClosing;

    public SettingsWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        ApplyThemeBackground();
        Loaded += OnLoaded;
    }

    private void ApplyThemeBackground()
    {
        bool isLight = IsSystemLightTheme();
        SolidColorBrush brush = new(isLight ? LightBackground : DarkBackground);
        Background = brush;
        RootBorder.Background = brush;
    }

    private static bool IsSystemLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("AppsUseLightTheme");
            return value is 1;
        }
        catch
        {
            return false;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionWindow();
        LoadCurrentSettings();
        _isInitializing = false;
    }

    private void PositionWindow()
    {
        // Position at bottom-right of work area (near system tray)
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight + 18;
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Set flag before window closes to prevent Deactivated from calling Close() again
        _isClosing = true;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Close when focus is lost (flyout behavior)
        if (!_isClosing)
        {
            _isClosing = true;
            Close();
        }
    }

    private void LoadCurrentSettings()
    {
        // Set FlyoutStyle combo box
        string flyoutStyleTag = _settings.Tray.FlyoutStyle.ToString();
        foreach (ComboBoxItem item in FlyoutStyleComboBox.Items)
        {
            if (item.Tag?.ToString() == flyoutStyleTag)
            {
                FlyoutStyleComboBox.SelectedItem = item;
                break;
            }
        }

        // Set AdapterSettingsStyle combo box
        string adapterStyleTag = _settings.Tray.AdapterSettingsStyle.ToString();
        foreach (ComboBoxItem item in AdapterSettingsStyleComboBox.Items)
        {
            if (item.Tag?.ToString() == adapterStyleTag)
            {
                AdapterSettingsStyleComboBox.SelectedItem = item;
                break;
            }
        }

        // Set color text boxes and previews
        ConnectedColorTextBox.Text = _settings.Icon.ConnectedColor;
        NoInternetColorTextBox.Text = _settings.Icon.NoInternetColor;
        DisconnectedColorTextBox.Text = _settings.Icon.DisconnectedColor;

        UpdateColorPreview(ConnectedColorPreview, _settings.Icon.ConnectedColor);
        UpdateColorPreview(NoInternetColorPreview, _settings.Icon.NoInternetColor);
        UpdateColorPreview(DisconnectedColorPreview, _settings.Icon.DisconnectedColor);

        // Set apply to light theme checkbox
        ApplyColorsToLightThemeCheckBox.IsChecked = _settings.Icon.ApplyColorsToLightTheme;
    }

    private void FlyoutStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (FlyoutStyleComboBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (Enum.TryParse(tag, out FlyoutStyle style))
            {
                _settings.Tray.FlyoutStyle = style;
                _settings.Save();
            }
        }
    }

    private void AdapterSettingsStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;

        if (AdapterSettingsStyleComboBox.SelectedItem is ComboBoxItem { Tag: string tag })
        {
            if (Enum.TryParse(tag, out AdapterSettingsStyle style))
            {
                _settings.Tray.AdapterSettingsStyle = style;
                _settings.Save();
            }
        }
    }

    private void OpenJsonButton_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.OpenSettingsFile();
    }

    private void ConnectedColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;

        string color = ConnectedColorTextBox.Text;
        if (TryParseColor(color, out _))
        {
            _settings.Icon.ConnectedColor = color;
            _settings.Save();
            UpdateColorPreview(ConnectedColorPreview, color);
        }
    }

    private void NoInternetColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;

        string color = NoInternetColorTextBox.Text;
        if (TryParseColor(color, out _))
        {
            _settings.Icon.NoInternetColor = color;
            _settings.Save();
            UpdateColorPreview(NoInternetColorPreview, color);
        }
    }

    private void DisconnectedColorTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isInitializing) return;

        string color = DisconnectedColorTextBox.Text;
        if (TryParseColor(color, out _))
        {
            _settings.Icon.DisconnectedColor = color;
            _settings.Save();
            UpdateColorPreview(DisconnectedColorPreview, color);
        }
    }

    private void ApplyColorsToLightThemeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;

        _settings.Icon.ApplyColorsToLightTheme = ApplyColorsToLightThemeCheckBox.IsChecked == true;
        _settings.Save();
    }

    private static void UpdateColorPreview(Border preview, string hexColor)
    {
        if (TryParseColor(hexColor, out Color color))
        {
            preview.Background = new SolidColorBrush(color);
        }
    }

    private static bool TryParseColor(string hexColor, out Color color)
    {
        color = default;
        try
        {
            if (string.IsNullOrWhiteSpace(hexColor)) return false;

            if (hexColor.StartsWith('#'))
                hexColor = hexColor[1..];

            switch (hexColor.Length)
            {
                case 6:
                {
                    byte r = Convert.ToByte(hexColor[0..2], 16);
                    byte g = Convert.ToByte(hexColor[2..4], 16);
                    byte b = Convert.ToByte(hexColor[4..6], 16);
                    color = Color.FromArgb(255, r, g, b);
                    return true;
                }
                case 8:
                {
                    byte a = Convert.ToByte(hexColor[0..2], 16);
                    byte r = Convert.ToByte(hexColor[2..4], 16);
                    byte g = Convert.ToByte(hexColor[4..6], 16);
                    byte b = Convert.ToByte(hexColor[6..8], 16);
                    color = Color.FromArgb(a, r, g, b);
                    return true;
                }
            }
        }
        catch
        {
            // Invalid hex color
        }

        return false;
    }
}
