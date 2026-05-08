using System.Xml.Serialization;

namespace NetworkTrayAppWPF.Models;

/// <summary>
/// Network tray app action set. Hotkey bindings and tray click handlers route through these.
/// </summary>
public enum HotkeyAction
{
    OpenSettings,
    OpenFlyout,
    OpenNetworkSettings,
    OpenAdapterSettings,
}

/// <summary>
/// One persisted hotkey binding.
/// Identity = (Action, Parameter, BindingID): per (action, parameter) pair there can be N bindings,
/// distinguished by BindingID. BindingID == 0 is the legacy/primary row; legacy XML files without
/// the attribute deserialise to 0 and so become the primary row by default.
/// Modifiers and VirtualKey are raw Win32 values (MOD_* and VK_*) so the storage shape matches RegisterHotKey
/// directly and the settings model doesn't depend on WPF input enums.
/// MOD_NOREPEAT is added at registration time, never persisted.
/// </summary>
public sealed class HotkeyBinding
{
    [XmlAttribute]
    public HotkeyAction Action { get; set; }

    /// <summary>
    /// Free-form action-specific parameter, encoded as a string so XmlSerializer roundtrips it trivially.
    /// Empty for fixed-target actions; project-specific actions are free to define their own encoding.
    /// </summary>
    [XmlAttribute]
    public string Parameter { get; set; } = string.Empty;

    // MOD_* flags from RegisterHotKey (no MOD_NOREPEAT - added at registration time).
    [XmlAttribute]
    public uint Modifiers { get; set; }

    // VK_* virtual key code passed to RegisterHotKey.
    [XmlAttribute]
    public uint VirtualKey { get; set; }

    [XmlAttribute]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Disambiguator for multiple bindings sharing the same (Action, Parameter).
    /// 0 = primary/legacy row; 1, 2, ... = additional bindings added by the user.
    /// Missing in legacy XML, deserialises to 0.
    /// </summary>
    [XmlAttribute]
    public int BindingID { get; set; }

    /// <summary>
    /// Tombstone flag: true means the user explicitly removed this binding through the UI.
    /// Tombstones are kept in the persisted list (instead of being deleted) so the default-seeder
    /// in AppSettings.EnsureDefaultHotkeys can tell that a default was removed on purpose and must
    /// not be re-added on the next launch.
    /// Filtered out of UI display and hotkey registration.
    /// </summary>
    [XmlAttribute]
    public bool RemovedByUser { get; set; }

    public bool IsBound => VirtualKey != 0 && Modifiers != 0;

    /// <summary>
    /// "Any binding for this (Action, Parameter)" lookup.
    /// Use the 3-arg overload when row identity matters for persistence/status.
    /// </summary>
    public bool Matches(HotkeyAction action, string? parameter) =>
        Action == action && string.Equals(Parameter, parameter ?? string.Empty, StringComparison.Ordinal);

    // Strict identity check including the per-row BindingID disambiguator.
    public bool Matches(HotkeyAction action, string? parameter, int bindingID) =>
        Action == action
        && string.Equals(Parameter, parameter ?? string.Empty, StringComparison.Ordinal)
        && BindingID == bindingID;
}
