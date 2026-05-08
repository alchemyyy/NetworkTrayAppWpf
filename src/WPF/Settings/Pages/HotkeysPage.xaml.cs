using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NetworkTrayAppWPF.Localization;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Services;
using NetworkTrayAppWPF.WPF.Utils;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace NetworkTrayAppWPF.WPF.Settings.Pages;

/// <summary>
/// Hotkeys settings page. One <see cref="HotkeyRowViewModel"/> per (Action, Parameter) group;
/// each row owns an <see cref="HotkeyRowViewModel.Entries"/> list of bound chords surfaced as
/// sub-cards beneath the row's draft modifier+key inputs. Add commits the draft as a new entry,
/// the entry's "x" deletes it. Persistence flows through <see cref="AppSettings.Hotkeys"/>
/// and <see cref="GlobalHotkeyService.Apply"/>.
/// </summary>
public partial class HotkeysPage : UserControl
{
    private AppSettings? _settings;

    private readonly ObservableCollection<HotkeyRowViewModel> _hotkeyRows = [];
    private bool _hotkeyRowsPopulated;

    /// <summary>Static modifier catalog exposed to row templates via <c>{x:Static}</c> binding.</summary>
    public static IReadOnlyList<ModifierCatalog.Option> HotkeyModifierOptions => ModifierCatalog.All;

    public HotkeysPage() => InitializeComponent();

    /// <summary>
    /// Injects AppSettings.
    /// Defers row population until <see cref="RefreshOnShow"/> on first nav so the rows aren't built
    /// for users who never visit this tab.
    /// </summary>
    public void LoadFromSettings(AppSettings settings) => _settings = settings;

    public void RefreshOnShow() => EnsureHotkeyRowsPopulated();

    private static GlobalHotkeyService? GetHotkeyService() => AppServices.HotkeyService;

    private void EnsureHotkeyRowsPopulated()
    {
        if (_hotkeyRowsPopulated) return;

        _hotkeyRowsPopulated = true;

        HotkeyRowsList.ItemsSource = _hotkeyRows;
        RebuildHotkeyRows();
        ReapplyHotkeysAndUpdateStatuses();
    }

    private void RebuildHotkeyRows()
    {
        if (_settings == null) return;

        _hotkeyRows.Clear();

        AddFixedActionRow(HotkeyAction.OpenFlyout, string.Empty,
            LocalizationManager.Instance["Settings_Hotkeys_OpenFlyout_Title"],
            LocalizationManager.Instance["Settings_Hotkeys_OpenFlyout_Description"]);
        AddFixedActionRow(HotkeyAction.OpenSettings, string.Empty,
            LocalizationManager.Instance["Settings_Hotkeys_OpenSettings_Title"],
            LocalizationManager.Instance["Settings_Hotkeys_OpenSettings_Description"]);
    }

    private void AddFixedActionRow(HotkeyAction action, string parameter, string label, string description)
    {
        if (_settings == null) return;

        HotkeyRowViewModel row = new(action, parameter, label, description,
            showsTarget: false, showsRemove: false);
        foreach (HotkeyBinding b in _settings.Hotkeys
            .Where(b => !b.RemovedByUser && b.Matches(action, parameter))
            .OrderBy(b => b.BindingID))
            row.Entries.Add(new HotkeyEntryViewModel(b.BindingID, b.Modifiers, b.VirtualKey));
        AddRow(row);
    }

    private void AddRow(HotkeyRowViewModel row)
    {
        row.RecomputeAddButtonState();
        _hotkeyRows.Add(row);
    }

    /// <summary>
    /// Picks the next free BindingID for a new entry in this (Action, Parameter) group.
    /// Scans the persisted bindings for the group and returns max+1.
    /// </summary>
    private int NextBindingID(HotkeyAction action, string parameter)
    {
        int maxID = 0;

        if (_settings != null)
        {
            foreach (HotkeyBinding b in _settings.Hotkeys)
            {
                if (!b.Matches(action, parameter)) continue;

                if (b.BindingID > maxID) maxID = b.BindingID;
            }
        }

        return maxID + 1;
    }

    private void ReapplyHotkeysAndUpdateStatuses()
    {
        if (_settings == null) return;

        GlobalHotkeyService? hotkeyService = GetHotkeyService();
        if (hotkeyService == null)
        {
            foreach (HotkeyRowViewModel row in _hotkeyRows)
            foreach (HotkeyEntryViewModel entry in row.Entries)
            {
                entry.Status = HotkeyStatus.Conflict;
                entry.StatusTooltip = LocalizationManager.Instance["Settings_Hotkeys_Status_HotkeyServiceUnavailable"];
            }
            return;
        }

        HotkeyApplyResult result;
        try { result = hotkeyService.Apply(_settings.Hotkeys); }
        catch (Exception ex)
        {
            WPFLog.Log($"HotkeysPage.ReapplyHotkeysAndUpdateStatuses: {ex.Message}");
            return;
        }

        foreach (HotkeyRowViewModel row in _hotkeyRows)
        foreach (HotkeyEntryViewModel entry in row.Entries)
        {
            HotkeyBinding? matched = _settings.Hotkeys
                .FirstOrDefault(b => b.Matches(row.Action, row.Parameter, entry.BindingID));
            if (matched is not { IsBound: true })
            {
                entry.Status = HotkeyStatus.Unbound;
                entry.StatusTooltip = null;
                continue;
            }
            if (result.Failed.TryGetValue(matched, out string? errorMessage))
            {
                entry.Status = HotkeyStatus.Conflict;
                entry.StatusTooltip = errorMessage;
            }
            else
            {
                entry.Status = HotkeyStatus.Registered;
                entry.StatusTooltip = LocalizationManager.Instance["Settings_Hotkeys_Status_Registered"];
            }
        }
    }

    private void HotkeyKeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.Background = (System.Windows.Media.Brush)FindResource("ThemeTextBoxFocused");
    }

    private void HotkeyKeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.ClearValue(BackgroundProperty);
    }

    /// <summary>
    /// Captures a single key from the focused textbox and writes its VK to the row's
    /// <see cref="HotkeyRowViewModel.DraftVirtualKey"/>.
    /// Bare modifier keys (Ctrl, Alt, Shift, Win) and F12 are ignored - the modifier comes from the
    /// dropdown to its left, and F12 is reserved by the kernel debugger.
    /// </summary>
    private void HotkeyKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { Tag: HotkeyRowViewModel row }) return;

        Key wpfKey = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (wpfKey)
        {
            case Key.LeftCtrl: case Key.RightCtrl:
            case Key.LeftAlt: case Key.RightAlt:
            case Key.LeftShift: case Key.RightShift:
            case Key.LWin: case Key.RWin:
            case Key.None:
                e.Handled = true;
                return;
        }
        if (wpfKey == Key.Escape)
        {
            e.Handled = true;
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(wpfKey);
        if (vk == 0)
        {
            e.Handled = true;
            return;
        }

        if (vk == 0x7B) // VK_F12 - reserved by the kernel debugger
        {
            WPFLog.Log("HotkeysPage: F12 is reserved by the debugger and cannot be bound.");
            e.Handled = true;
            return;
        }

        row.DraftVirtualKey = (uint)vk;
        e.Handled = true;
    }

    /// <summary>
    /// Add click on a row: commit the draft (modifier+key) as a new entry under this row, allocate
    /// a fresh BindingID, persist, then clear the draft so the user can type the next chord.
    /// </summary>
    private void HotkeyAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (sender is not Button { Tag: HotkeyRowViewModel row }) return;
        if (!row.AddButtonEnabled) return;

        uint mods = row.DraftModifiers;
        uint vk = row.DraftVirtualKey;
        if (mods == 0 || vk == 0) return;

        int newID = NextBindingID(row.Action, row.Parameter);
        _settings.Hotkeys.Add(new HotkeyBinding
        {
            Action = row.Action,
            Parameter = row.Parameter,
            Modifiers = mods,
            VirtualKey = vk,
            Enabled = true,
            BindingID = newID,
        });
        row.Entries.Add(new HotkeyEntryViewModel(newID, mods, vk));
        row.ClearDraft();
        SaveAndNotify();
        ReapplyHotkeysAndUpdateStatuses();
    }

    /// <summary>
    /// "x" click on an entry sub-card: remove that one bound chord. The owning row is found by
    /// scanning <see cref="_hotkeyRows"/> since the entry doesn't carry a back-reference.
    /// When the entry occupies a built-in default's identity slot, the persisted binding is
    /// tombstoned (RemovedByUser=true) instead of being deleted, so EnsureDefaultHotkeys doesn't
    /// re-seed the default on the next launch.
    /// </summary>
    private void HotkeyEntryDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        if (sender is not Button { Tag: HotkeyEntryViewModel entry }) return;

        HotkeyRowViewModel? owner = null;
        foreach (HotkeyRowViewModel r in _hotkeyRows)
            if (r.Entries.Contains(entry)) { owner = r; break; }
        if (owner == null) return;

        owner.Entries.Remove(entry);

        if (AppSettings.IsDefaultHotkeyIdentity(owner.Action, owner.Parameter, entry.BindingID))
        {
            foreach (HotkeyBinding b in _settings.Hotkeys)
            {
                if (!b.Matches(owner.Action, owner.Parameter, entry.BindingID)) continue;

                b.RemovedByUser = true;
                b.Enabled = false;
            }
        }
        else
            _settings.Hotkeys.RemoveAll(b => b.Matches(owner.Action, owner.Parameter, entry.BindingID));

        SaveAndNotify();
        ReapplyHotkeysAndUpdateStatuses();
    }

    /// <summary>
    /// Filters the visible rows by case-insensitive substring match against Label + Description.
    /// Empty query clears the filter so the full stack reappears.
    /// </summary>
    private void HotkeySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (HotkeyRowsList?.Items == null) return;

        string query = HotkeySearchBox.Text.Trim();
        if (query.Length == 0)
        {
            HotkeyRowsList.Items.Filter = null;
            return;
        }

        HotkeyRowsList.Items.Filter = item =>
        {
            if (item is not HotkeyRowViewModel row) return false;

            return row.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                   || row.Description.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        };
    }

    private void SaveAndNotify()
    {
        if (_settings == null) return;
        _settings.Save();
        _settings.RaiseChanged();
    }
}
