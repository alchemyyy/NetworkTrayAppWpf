using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NetworkTrayAppWPF.Interop;
using NetworkTrayAppWPF.Localization;
using NetworkTrayAppWPF.Models;

namespace NetworkTrayAppWPF.WPF.Utils;

/// <summary>
/// Picks a hotkey-row template based on whether the row exposes a target selector.
/// Targeted rows get a header-on-top layout (no description brick, target dropdown leftmost),
/// untargeted rows use the standard label-on-left layout.
/// </summary>
public sealed class HotkeyRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? FixedTemplate { get; set; }
    public DataTemplate? TargetedTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object? item, DependencyObject container) =>
        item is HotkeyRowViewModel { ShowsTarget: true } ? TargetedTemplate : FixedTemplate;
}

public enum HotkeyStatus
{
    Unbound,
    Registered,
    Conflict,
}

/// <summary>
/// One row in the Hotkeys settings tab, one per (Action, Parameter) group. Holds the row's
/// "draft" modifier+key inputs (the modifier dropdown and key textbox above the entry list)
/// and the live <see cref="Entries"/> collection of bound chords. The row is stable across edits;
/// the host page drives Entries adds/removes and re-persists into <see cref="AppSettings.Hotkeys"/>.
/// </summary>
public sealed class HotkeyRowViewModel : INotifyPropertyChanged
{
    public HotkeyAction Action { get; }
    public string Label { get; }
    public string Description { get; }
    public bool ShowsTarget { get; }
    public bool ShowsRemove { get; }

    private string _parameter;
    public string Parameter
    {
        get => _parameter;
        set
        {
            if (_parameter == value) return;

            string old = _parameter;
            _parameter = value;
            OnPropertyChanged();
            ParameterChanged?.Invoke(this, old, value);
        }
    }

    public ObservableCollection<HotkeyEntryViewModel> Entries { get; } = [];

    /// <summary>True when at least one chord is bound; drives the entry-list visibility.</summary>
    public bool HasEntries => Entries.Count > 0;

    private uint _draftModifiers;
    public uint DraftModifiers
    {
        get => _draftModifiers;
        set
        {
            if (_draftModifiers == value) return;

            _draftModifiers = value;
            OnPropertyChanged();
            UpdateAddButtonState();
        }
    }

    private uint _draftVirtualKey;
    public uint DraftVirtualKey
    {
        get => _draftVirtualKey;
        set
        {
            if (_draftVirtualKey == value) return;

            _draftVirtualKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DraftKeyDisplay));
            UpdateAddButtonState();
        }
    }

    public string DraftKeyDisplay => HotkeyKeyFormat.Format(_draftVirtualKey);

    private bool _addButtonEnabled;
    public bool AddButtonEnabled
    {
        get => _addButtonEnabled;
        private set
        {
            if (_addButtonEnabled == value) return;

            _addButtonEnabled = value;
            OnPropertyChanged();
        }
    }

    private string _addButtonText = LocalizationManager.Instance["Settings_Hotkeys_Add_Button"];
    public string AddButtonText
    {
        get => _addButtonText;
        private set
        {
            if (_addButtonText == value) return;

            _addButtonText = value;
            OnPropertyChanged();
        }
    }

    public HotkeyRowViewModel(
        HotkeyAction action,
        string parameter,
        string label,
        string description,
        bool showsTarget,
        bool showsRemove)
    {
        Action = action;
        _parameter = parameter;
        Label = label;
        Description = description;
        ShowsTarget = showsTarget;
        ShowsRemove = showsRemove;
        Entries.CollectionChanged += OnEntriesCollectionChanged;
    }

    private void OnEntriesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasEntries));
        UpdateAddButtonState();
    }

    /// <summary>Zeroes the draft modifier+key inputs after the user commits an Add.</summary>
    public void ClearDraft()
    {
        bool changed = false;
        if (_draftModifiers != 0) { _draftModifiers = 0; OnPropertyChanged(nameof(DraftModifiers)); changed = true; }
        if (_draftVirtualKey != 0)
        {
            _draftVirtualKey = 0;
            OnPropertyChanged(nameof(DraftVirtualKey));
            OnPropertyChanged(nameof(DraftKeyDisplay));
            changed = true;
        }
        if (changed) UpdateAddButtonState();
    }

    /// <summary>
    /// Recomputes <see cref="AddButtonEnabled"/> and <see cref="AddButtonText"/>. Called automatically
    /// on draft-input or Entries changes; exposed so initial seeding can also force a recompute.
    /// </summary>
    public void RecomputeAddButtonState() => UpdateAddButtonState();

    private void UpdateAddButtonState()
    {
        if (_draftModifiers == 0 || _draftVirtualKey == 0)
        {
            AddButtonText = LocalizationManager.Instance["Settings_Hotkeys_Add_Button"];
            AddButtonEnabled = false;
            return;
        }

        bool exists = false;
        foreach (HotkeyEntryViewModel entry in Entries)
        {
            if (entry.Modifiers != _draftModifiers) continue;
            if (entry.VirtualKey != _draftVirtualKey) continue;

            exists = true;
            break;
        }
        AddButtonText = exists
            ? LocalizationManager.Instance["Settings_Hotkeys_Exists_Button"]
            : LocalizationManager.Instance["Settings_Hotkeys_Add_Button"];
        AddButtonEnabled = !exists;
    }

    /// <summary>Raised when <see cref="Parameter"/> changes; args are (row, oldParameter, newParameter).</summary>
    public event Action<HotkeyRowViewModel, string, string>? ParameterChanged;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

/// <summary>
/// One bound (modifier, virtualKey) entry under a <see cref="HotkeyRowViewModel"/>. The BindingID
/// tracks identity into <see cref="HotkeyBinding.BindingID"/> so per-entry status survives
/// Parameter renames and reapplies.
/// </summary>
public sealed class HotkeyEntryViewModel(int bindingID, uint modifiers, uint virtualKey)
    : INotifyPropertyChanged
{
    public int BindingID { get; } = bindingID;
    public uint Modifiers { get; } = modifiers;
    public uint VirtualKey { get; } = virtualKey;

    /// <summary>Modifier label, "+" separator, key label - the card's left-aligned text.</summary>
    public string Display => $"{ModifierCatalog.LabelFor(Modifiers)} + {HotkeyKeyFormat.Format(VirtualKey)}";

    private HotkeyStatus _status;
    public HotkeyStatus Status
    {
        get => _status;
        set
        {
            if (_status == value) return;

            _status = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StatusGlyph));
        }
    }

    private string? _statusTooltip;
    public string? StatusTooltip
    {
        get => _statusTooltip;
        set
        {
            if (_statusTooltip == value) return;

            _statusTooltip = value;
            OnPropertyChanged();
        }
    }

    // \uE7BA = Segoe Fluent Icons "Warning" glyph. Empty string for non-conflict states keeps the
    // entry card visually quiet when the binding is registered or unbound.
    public string StatusGlyph => _status == HotkeyStatus.Conflict ? "\uE7BA" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? prop = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
}

/// <summary>Static catalog of the 15 non-empty modifier subsets of {Ctrl, Alt, Shift, Win}.</summary>
public static class ModifierCatalog
{
    public sealed class Option
    {
        public string Label { get; init; } = "";
        public uint Modifiers { get; init; }
    }

    public static readonly List<Option> All =
    [
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_Ctrl"],
            Modifiers = User32.MOD_CONTROL },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_Alt"],
            Modifiers = User32.MOD_ALT },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_Shift"],
            Modifiers = User32.MOD_SHIFT },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_Win"],
            Modifiers = User32.MOD_WIN },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_CtrlAlt"],
            Modifiers = User32.MOD_CONTROL | User32.MOD_ALT },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_CtrlShift"],
            Modifiers = User32.MOD_CONTROL | User32.MOD_SHIFT },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_CtrlWin"],
            Modifiers = User32.MOD_CONTROL | User32.MOD_WIN },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_AltShift"],
            Modifiers = User32.MOD_ALT | User32.MOD_SHIFT },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_AltWin"],
            Modifiers = User32.MOD_ALT | User32.MOD_WIN },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_ShiftWin"],
            Modifiers = User32.MOD_SHIFT | User32.MOD_WIN },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_CtrlAltShift"],
            Modifiers = User32.MOD_CONTROL | User32.MOD_ALT | User32.MOD_SHIFT },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_CtrlAltWin"],
            Modifiers = User32.MOD_CONTROL | User32.MOD_ALT | User32.MOD_WIN },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_CtrlShiftWin"],
            Modifiers = User32.MOD_CONTROL | User32.MOD_SHIFT | User32.MOD_WIN },
        new() { Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_AltShiftWin"],
            Modifiers = User32.MOD_ALT | User32.MOD_SHIFT | User32.MOD_WIN },
        new()
        {
            Label = LocalizationManager.Instance["Settings_Hotkeys_Modifier_CtrlAltShiftWin"],
            Modifiers = User32.MOD_CONTROL | User32.MOD_ALT | User32.MOD_SHIFT | User32.MOD_WIN,
        },
    ];

    /// <summary>Reverse lookup for entry-card display: maps a modifier bitfield to its catalog label.</summary>
    public static string LabelFor(uint modifiers)
    {
        foreach (Option o in All)
            if (o.Modifiers == modifiers) return o.Label;
        return "";
    }
}

/// <summary>One entry in a target dropdown for hotkey rows that bind to a parameterized target.</summary>
public sealed class HotkeyTargetOption
{
    public string Label { get; init; } = "";
    /// <summary>Raw target string written to <see cref="HotkeyBinding.Parameter"/>.</summary>
    public string Value { get; init; } = "";
}

/// <summary>Renders a virtual-key code as a human label for the row's read-only display.</summary>
public static class HotkeyKeyFormat
{
    public static string Format(uint vk)
    {
        if (vk == 0) return string.Empty;

        try
        {
            Key wpfKey = KeyInterop.KeyFromVirtualKey((int)vk);
            if (wpfKey == Key.None) return $"VK({vk:X2})";

            return wpfKey switch
            {
                Key.Oem1 => ";",
                Key.Oem2 => "/",
                Key.Oem3 => "`",
                Key.Oem4 => "[",
                Key.Oem5 => "\\",
                Key.Oem6 => "]",
                Key.Oem7 => "'",
                Key.OemMinus => "-",
                Key.OemPlus => "=",
                Key.OemComma => ",",
                Key.OemPeriod => ".",
                Key.Space => LocalizationManager.Instance["Settings_Hotkeys_Key_Space"],
                Key.Return => LocalizationManager.Instance["Settings_Hotkeys_Key_Enter"],
                Key.Escape => LocalizationManager.Instance["Settings_Hotkeys_Key_Escape"],
                Key.PageUp => LocalizationManager.Instance["Settings_Hotkeys_Key_PageUp"],
                Key.PageDown => LocalizationManager.Instance["Settings_Hotkeys_Key_PageDown"],
                _ => wpfKey.ToString(),
            };
        }
        catch
        {
            return $"VK({vk:X2})";
        }
    }
}
