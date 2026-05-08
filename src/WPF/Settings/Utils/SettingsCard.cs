using System.Windows;
using Control = System.Windows.Controls.Control;

namespace NetworkTrayAppWPF.WPF.Settings.Utils;

/// <summary>
/// Standard settings-row container: title + optional description on the left, single content slot
/// on the right (vertically centered). Templated rather than a UserControl so consumers can place
/// named children inside the Control DP without hitting MC3093 namescope conflicts.
/// The default style + template lives in App.xaml and uses the existing
/// SettingsCardStyle / SettingsCardTitlePanel / SettingTitleStyle / SettingDescriptionStyle resources.
/// </summary>
public class SettingsCard : Control
{
    /// <summary>Bold setting label rendered at the top-left.</summary>
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(SettingsCard),
        new PropertyMetadata(string.Empty));

    /// <summary>Secondary explanatory line under the title; collapses when empty.</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SettingsCard),
        new PropertyMetadata(string.Empty));

    /// <summary>Right-column content (toggle, combo, spinner, button row, etc.).</summary>
    public static readonly DependencyProperty ControlProperty = DependencyProperty.Register(
        nameof(Control), typeof(object), typeof(SettingsCard),
        new PropertyMetadata(null));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public object? Control
    {
        get => GetValue(ControlProperty);
        set => SetValue(ControlProperty, value);
    }

    static SettingsCard()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SettingsCard),
            new FrameworkPropertyMetadata(typeof(SettingsCard)));
    }
}
