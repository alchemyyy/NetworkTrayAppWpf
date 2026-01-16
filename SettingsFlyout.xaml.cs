using NetworkTrayAppWpf.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace NetworkTrayAppWpf;

/// <summary>
/// Modern flyout-style settings window with acrylic background.
/// Lightweight replacement for ModernWpfUI-based SettingsWindow.
/// </summary>
public partial class SettingsFlyout : Window
{
    private readonly AppSettings _settings;
    private readonly ThemeColorManager _themeColor;
    private bool _isInitializing = true;
    private bool _isClosing;

    public SettingsFlyout(AppSettings settings, ThemeColorManager themeColor)
    {
        _settings = settings;
        _themeColor = themeColor;

        InitializeComponent();

        ApplyTheme();
        Loaded += OnLoaded;
    }

    private void Window_SourceInitialized(object sender, EventArgs e)
    {
        // Apply window styles after handle is created
        this.ApplyToolWindowStyle();
        this.EnableRoundedCorners();
        this.SetDarkMode(!_themeColor.IsLightTheme);

        // Enable acrylic blur
        AccentPolicyHelper.EnableAcrylic(this, _themeColor.Acrylic);
    }

    private void ApplyTheme()
    {
        bool isLight = _themeColor.IsLightTheme;

        // Background with acrylic tint
        Color bgColor = isLight ? ThemeColorManager.LightBackground : ThemeColorManager.DarkBackground;
        Color fgColor = isLight ? ThemeColorManager.LightForeground : ThemeColorManager.DarkForeground;
        Color borderColor = isLight ? ThemeColorManager.LightBorder : ThemeColorManager.DarkBorder;
        Color separatorColor = isLight ? ThemeColorManager.LightSeparator : ThemeColorManager.DarkSeparator;

        // Semi-transparent background for acrylic effect
        RootBorder.Background = new SolidColorBrush(Color.FromArgb(0xE8, bgColor.R, bgColor.G, bgColor.B));
        RootBorder.BorderBrush = new SolidColorBrush(borderColor);

        // Text colors
        SolidColorBrush foregroundBrush = new(fgColor);
        HeaderText.Foreground = foregroundBrush;
        FlyoutStyleLabel.Foreground = foregroundBrush;
        AdapterLabel.Foreground = foregroundBrush;
        IconColorsHeader.Foreground = foregroundBrush;
        ConnectedLabel.Foreground = foregroundBrush;
        NoInternetLabel.Foreground = foregroundBrush;
        DisconnectedLabel.Foreground = foregroundBrush;
        ApplyColorsToLightThemeCheckBox.Foreground = foregroundBrush;

        // Separators
        SolidColorBrush separatorBrush = new(separatorColor);
        Separator1.Fill = separatorBrush;
        Separator2.Fill = separatorBrush;

        // Color preview borders
        SolidColorBrush borderBrush = new(borderColor);
        ConnectedColorPreview.BorderBrush = borderBrush;
        NoInternetColorPreview.BorderBrush = borderBrush;
        DisconnectedColorPreview.BorderBrush = borderBrush;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Unsubscribe to prevent memory leak (Loaded only fires once)
        Loaded -= OnLoaded;

        PositionWindow();
        LoadCurrentSettings();
        _isInitializing = false;
    }

    private void PositionWindow()
    {
        // Position at bottom-right of work area (near system tray)
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 12;
        Top = workArea.Bottom - ActualHeight - 12;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Close when focus is lost (flyout behavior)
        if (!_isClosing)
        {
            _isClosing = true;
            // Disable acrylic before closing to avoid visual artifacts
            AccentPolicyHelper.DisableAcrylic(this);
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

        // Set apply to light themeColor checkbox
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

    private static void UpdateColorPreview(System.Windows.Controls.Border preview, string hexColor)
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
