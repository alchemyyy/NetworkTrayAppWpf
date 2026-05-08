using System.Windows;
using System.Windows.Controls;
using NetworkTrayAppWPF.Localization;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.WPF.Settings.Utils;
using ComboBox = System.Windows.Controls.ComboBox;
using UserControl = System.Windows.Controls.UserControl;

namespace NetworkTrayAppWPF.WPF.Settings.Pages;

/// <summary>
/// Tray-icon settings page. Owns its UI, handlers, and the click-action combo seeding.
/// Instantiated by the shell's XAML; the shell calls <see cref="LoadFromSettings"/> after construction
/// to inject AppSettings and seed control values. Routes generic Tag-based mutations through
/// <see cref="SettingsBindings"/> so other pages share the same dispatch tables.
/// </summary>
public partial class TrayIconPage : UserControl
{
    // Tag/key pairs for the click action combos. The Display column is a localization key
    // resolved at populate time; same key drives every shared label. Tag values must match
    // the TrayClickAction enum names so saved settings round-trip through SelectComboByTag.
    private static readonly (string Tag, string DisplayKey)[] TrayClickActionOptions =
    [
        ("Nothing", "Settings_TrayIcon_ClickAction_Nothing"),
        ("OpenSettings", "Settings_TrayIcon_ClickAction_OpenSettings"),
        ("OpenAdapterSettings", "Settings_TrayIcon_ClickAction_OpenAdapterSettings"),
    ];

    private AppSettings? _settings;
    private bool _suppressChangeEvents;

    public TrayIconPage()
    {
        InitializeComponent();
        PopulateClickActionCombos();
    }

    /// <summary>
    /// Injects the AppSettings instance and seeds every control's value from it.
    /// The shell calls this from its own LoadFromSettings;
    /// subsequent calls re-seed the page (used when settings are reloaded externally).
    /// </summary>
    public void LoadFromSettings(AppSettings settings)
    {
        _settings = settings;
        _suppressChangeEvents = true;
        try
        {
            SettingsBindings.SelectComboByTag(ContextMenuPositionCombo, settings.ContextMenuPosition.ToString());
            SettingsBindings.SelectComboByTag(TrayDoubleClickActionCombo, settings.TrayDoubleClickAction.ToString());
            SettingsBindings.SelectComboByTag(
                TrayCtrlLeftClickActionCombo, settings.TrayCtrlLeftClickAction.ToString());
            SettingsBindings.SelectComboByTag(TrayAltLeftClickActionCombo, settings.TrayAltLeftClickAction.ToString());
            SettingsBindings.SelectComboByTag(
                TrayCtrlRightClickActionCombo, settings.TrayCtrlRightClickAction.ToString());
            SettingsBindings.SelectComboByTag(
                TrayAltRightClickActionCombo, settings.TrayAltRightClickAction.ToString());
            SettingsBindings.SelectComboByTag(
                TrayCtrlDoubleLeftClickActionCombo, settings.TrayCtrlDoubleLeftClickAction.ToString());
            SettingsBindings.SelectComboByTag(
                TrayAltDoubleLeftClickActionCombo, settings.TrayAltDoubleLeftClickAction.ToString());
        }
        finally
        {
            _suppressChangeEvents = false;
        }
    }

    private void PopulateClickActionCombos()
    {
        ComboBox[] combos =
        [
            TrayDoubleClickActionCombo,
            TrayCtrlLeftClickActionCombo,
            TrayAltLeftClickActionCombo,
            TrayCtrlRightClickActionCombo,
            TrayAltRightClickActionCombo,
            TrayCtrlDoubleLeftClickActionCombo,
            TrayAltDoubleLeftClickActionCombo
        ];

        foreach (ComboBox combo in combos)
        {
            foreach ((string tag, string displayKey) in TrayClickActionOptions)
                combo.Items.Add(new ComboBoxItem { Tag = tag, Content = LocalizationManager.Instance[displayKey] });
        }
    }

    private void EnumCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null) return;
        SettingsBindings.HandleEnumCombo(
            sender, _settings, SaveAndNotify, () => _suppressChangeEvents, this);
    }

    private void SaveAndNotify()
    {
        if (_settings == null) return;
        _settings.Save();
        _settings.RaiseChanged();
    }
}
