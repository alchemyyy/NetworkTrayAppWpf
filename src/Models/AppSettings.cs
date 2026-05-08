using System.IO;
using System.Xml;
using System.Xml.Serialization;
using NetworkTrayAppWPF.Interop;
using Color = System.Windows.Media.Color;

namespace NetworkTrayAppWPF.Models;

public enum ThemeMode
{
    System,
    Light,
    Dark,
}

public enum TrayIconStyle
{
    Dynamic,
    Static,
}

/// <summary>
/// Action taken when the tray icon is clicked.
/// Skeleton ships with a no-op placeholder; extend with project-specific actions in your fork.
/// </summary>
public enum TrayClickAction
{
    Nothing,
    OpenSettings,
    OpenAdapterSettings,
}

/// <summary>
/// Where the tray right-click menu appears.
/// Classic opens at the cursor position (the OS default for tray menus).
/// Modern docks the menu in the bottom-right corner of the primary work area with an 8px inset,
/// matching the Windows 11 system-flyout pattern.
/// </summary>
public enum ContextMenuPosition
{
    Classic,
    Modern,
}

/// <summary>
/// A user-overridable theme color with independent light and dark variants.
/// Either side may be null, meaning "unset" - the upstream resolver falls back to the per-color default.
/// While a color picker is open, TemporaryLightColor / TemporaryDarkColor short-circuit
/// the persisted hex values so the rest of the app sees the in-flight edit through the same Resolve path
/// without mutating (and risking persistence of) the saved hex until the user accepts.
/// Callers wire one or more change handlers via the (Action) ctor or Subscribe;
/// every mutation of LightHex / DarkHex / Temporary* fires the multicast handler.
/// </summary>
public class NullableThemeColor
{
    private string? _lightHex;
    private string? _darkHex;
    private Color? _tempLight;
    private Color? _tempDark;
    private Action? _changed;

    // Required for XmlSerializer. Production callers should prefer the (Action) overload, or attach via Subscribe.
    public NullableThemeColor() { }

    // onChanged is invoked on every actual change (LightHex / DarkHex / Temporary*).
    public NullableThemeColor(Action onChanged) => Subscribe(onChanged);

    public void Subscribe(Action onChanged) => _changed += onChanged;

    public void Unsubscribe(Action onChanged) => _changed -= onChanged;

    [XmlElement]
    public string? LightHex
    {
        get => _lightHex;
        set
        {
            if (_lightHex == value) return;
            _lightHex = value;
            _changed?.Invoke();
        }
    }

    [XmlElement]
    public string? DarkHex
    {
        get => _darkHex;
        set
        {
            if (_darkHex == value) return;
            _darkHex = value;
            _changed?.Invoke();
        }
    }

    // Live-preview override for the light variant, set by the color picker on every edit
    // and cleared when the picker accepts (committed to LightHex) or aborts.
    // Never serialized.
    [XmlIgnore]
    public Color? TemporaryLightColor
    {
        get => _tempLight;
        set
        {
            if (_tempLight == value) return;
            _tempLight = value;
            _changed?.Invoke();
        }
    }

    // Live-preview override for the dark variant. Same lifecycle as TemporaryLightColor.
    [XmlIgnore]
    public Color? TemporaryDarkColor
    {
        get => _tempDark;
        set
        {
            if (_tempDark == value) return;
            _tempDark = value;
            _changed?.Invoke();
        }
    }

    public bool IsUnset => string.IsNullOrEmpty(LightHex) && string.IsNullOrEmpty(DarkHex);

    public Color? LightColor => TemporaryLightColor ?? TryParse(LightHex);
    public Color? DarkColor => TemporaryDarkColor ?? TryParse(DarkHex);

    private static Color? TryParse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;

        try
        {
            string hexString = hex.TrimStart('#');
            return hexString.Length switch
            {
                6 => Color.FromRgb(
                    Convert.ToByte(hexString[..2], 16),
                    Convert.ToByte(hexString[2..4], 16),
                    Convert.ToByte(hexString[4..6], 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(hexString[..2], 16),
                    Convert.ToByte(hexString[2..4], 16),
                    Convert.ToByte(hexString[4..6], 16),
                    Convert.ToByte(hexString[6..8], 16)),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    public static string ToHex(Color c) =>
        c.A == 255 ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    // Resolves the override for the given theme.
    // Returns null when this side is unset so the upstream resolver falls through to the per-color default.
    // The unset side is never derived from the counterpart - editing only the light variant must not rewrite
    // what the dark variant displays (and vice versa).
    public Color? Resolve(bool isLightTheme) => isLightTheme ? LightColor : DarkColor;
}

/// <summary>
/// Root application settings class.
/// Skeleton scaffold with a few illustrative fields - extend with project-specific settings in your fork.
/// </summary>
[XmlRoot("AppSettings")]
public class AppSettings
{
    // General
    public bool RunOnStartup { get; set; } = true;
    public bool Autosave { get; set; } = true;

    // Context menu
    public ContextMenuPosition ContextMenuPosition { get; set; } = ContextMenuPosition.Modern;
    public int ContextMenuFontSize { get; set; } = 15;

    // Theme
    public ThemeMode ThemeMode { get; set; } = ThemeMode.System;
    public NullableThemeColor TextColor { get; set; } = new();
    public NullableThemeColor BackgroundColor { get; set; } = new();
    public TrayIconStyle TrayIconStyle { get; set; } = TrayIconStyle.Dynamic;
    public NullableThemeColor TrayIconColor { get; set; } = new();
    public bool EnableRoundedCorners { get; set; } = true;

    // Tray icon interaction. Click actions are surfaced through TrayIconPage; the host wires what each
    // action does. The skeleton's TrayClickAction enum is a placeholder set extend it with app-specific
    // actions, then update App.xaml.cs's tray click handlers to dispatch on the chosen action.
    public TrayClickAction TrayDoubleClickAction { get; set; } = TrayClickAction.OpenAdapterSettings;
    public TrayClickAction TrayCtrlLeftClickAction { get; set; } = TrayClickAction.OpenAdapterSettings;
    public TrayClickAction TrayAltLeftClickAction { get; set; } = TrayClickAction.OpenSettings;
    public TrayClickAction TrayCtrlRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltRightClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayCtrlDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;
    public TrayClickAction TrayAltDoubleLeftClickAction { get; set; } = TrayClickAction.Nothing;

    // Network app: which UI to surface on tray left-click and which adapter window to open
    // from the context menu. AvailableNetworks is the safest default - works under any Win10/11
    // build without depending on the undocumented shell-experience COM contracts.
    public FlyoutStyle FlyoutStyle { get; set; } = FlyoutStyle.Windows10;
    public AdapterSettingsStyle AdapterSettingsStyle { get; set; } = AdapterSettingsStyle.Explorer;

    // Network app: per-state tray icon color overrides. Each falls back to the per-theme default
    // (white/black for connected, amber for no-internet, gray for disconnected) when unset.
    public NullableThemeColor NetworkConnectedColor { get; set; } = new();
    public NullableThemeColor NetworkNoInternetColor { get; set; } = new();
    public NullableThemeColor NetworkDisconnectedColor { get; set; } = new();

    // Empty by default; defaults are seeded by EnsureDefaultHotkeys() after construction or load.
    // The previous in-place initializer collided with XmlSerializer's "append to existing list" behavior:
    // the deserializer adds <Binding> elements to the list returned by the getter, so any default
    // listed here would duplicate every time the saved settings.xml was reloaded.
    [XmlArray("Hotkeys")]
    [XmlArrayItem("Binding")]
    public List<HotkeyBinding> Hotkeys { get; set; } = [];

    // Raised when any setting is changed through the settings window.
    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();

    public AppSettings() => WireColorCallbacks();

    /// <summary>
    /// Bridges every NullableThemeColor override on this instance to the global Changed event,
    /// so any color edit (committed hex or live-preview Temporary*) flows out through the same
    /// notification path as every other setting change.
    /// Idempotent: Unsubscribe runs first, so re-wiring after XmlSerializer replaces the ctor-wired
    /// instances post-deserialization can't double-fire.
    /// Specific listeners that want per-color granularity should attach via NullableThemeColor.Subscribe directly.
    /// </summary>
    public void WireColorCallbacks()
    {
        Action onChanged = RaiseChanged;
        foreach (NullableThemeColor color in EnumerateColorOverrides())
        {
            color.Unsubscribe(onChanged);
            color.Subscribe(onChanged);
        }
    }

    private IEnumerable<NullableThemeColor> EnumerateColorOverrides()
    {
        yield return TextColor;
        yield return BackgroundColor;
        yield return TrayIconColor;
        yield return NetworkConnectedColor;
        yield return NetworkNoInternetColor;
        yield return NetworkDisconnectedColor;
    }

    public static string GetDefaultPath()
    {
        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appDataFolder, Program.ApplicationName);
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "settings.xml");
    }

    // The folder that holds settings.xml - same folder as a LocalAppData install of the app.
    // Used by the uninstaller's "delete settings" branch.
    public static string GetDefaultDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Program.ApplicationName);

    public void Save() => Save(GetDefaultPath());

    public void Save(string path)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            XmlSerializerNamespaces namespaces = new();
            namespaces.Add("", "");

            XmlWriterSettings writerSettings = new()
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace
            };

            using FileStream stream = new(path, FileMode.Create);
            using XmlWriter writer = XmlWriter.Create(stream, writerSettings);
            XmlSerializer serializer = new(typeof(AppSettings));
            serializer.Serialize(writer, this, namespaces);
        }
        catch
        {
            // best-effort
        }
    }

    public static AppSettings LoadOrDefault() => LoadOrDefault(GetDefaultPath());

    public static AppSettings LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                using FileStream stream = new(path, FileMode.Open);
                XmlSerializer serializer = new(typeof(AppSettings));
                if (serializer.Deserialize(stream) is AppSettings loaded)
                {
                    // XmlSerializer replaces every NullableThemeColor property with a freshly-deserialized
                    // (parameterless-constructed) instance, dropping the ctor's wiring.
                    // Re-attach the bridge so loaded settings notify the global Changed event the same way
                    // fresh defaults do.
                    loaded.WireColorCallbacks();

                    // One-time cleanup of duplicate hotkey rows that may have accumulated from a prior build
                    // that re-seeded the default hotkey on every launch.
                    // Top up any defaults missing from the persisted list (e.g. when a new build ships a new
                    // default action). Skips entries the user has tombstoned via the UI (RemovedByUser=true)
                    // so an explicit removal isn't undone on the next launch.
                    bool changed = loaded.DedupeHotkeysByIdentity();
                    changed |= loaded.EnsureDefaultHotkeys();
                    if (changed) loaded.Save(path);
                    return loaded;
                }
            }
        }
        catch
        {
            // fall through to default
        }
        AppSettings defaults = new();
        defaults.EnsureDefaultHotkeys();
        defaults.Save(path);
        return defaults;
    }

    /// <summary>
    /// The set of built-in hotkey bindings seeded for fresh installs and topped up on every launch.
    /// Identity is (Action, Parameter, BindingID): defaults always live on BindingID 0 (the primary row),
    /// so a user-added secondary binding (BindingID >= 1) for the same action does not block re-seeding
    /// the primary row.
    /// Skeleton ships with one illustrative binding; replace with your project's own defaults.
    /// </summary>
    private static IReadOnlyList<HotkeyBinding> CreateDefaultHotkeys() =>
    [
        new HotkeyBinding
        {
            Action = HotkeyAction.OpenSettings,
            Parameter = string.Empty,
            Modifiers = User32.MOD_CONTROL | User32.MOD_WIN | User32.MOD_ALT,
            VirtualKey = 0x53, // VK_S
            Enabled = true,
            BindingID = 0,
        },
    ];

    /// <summary>
    /// True if the binding occupies the same identity slot as one of the built-in defaults
    /// (same Action, Parameter, and BindingID). Used by the settings UI to decide whether removing
    /// a binding should hard-delete it or keep it as a tombstone (RemovedByUser=true) so the default
    /// doesn't reappear on the next launch.
    /// </summary>
    public static bool IsDefaultHotkeyIdentity(HotkeyAction action, string parameter, int bindingID)
    {
        foreach (HotkeyBinding d in CreateDefaultHotkeys())
            if (d.Matches(action, parameter, bindingID)) return true;
        return false;
    }

    /// <summary>
    /// Removes redundant hotkey rows that share the same identity tuple (Action, Parameter, BindingID),
    /// keeping the first occurrence.
    /// Returns true when at least one row was dropped (caller should persist).
    /// </summary>
    public bool DedupeHotkeysByIdentity()
    {
        HashSet<(HotkeyAction, string, int)> seen = [];
        int writeIndex = 0;
        for (int readIndex = 0; readIndex < Hotkeys.Count; readIndex++)
        {
            HotkeyBinding b = Hotkeys[readIndex];
            (HotkeyAction, string, int) key = (b.Action, b.Parameter ?? string.Empty, b.BindingID);
            if (!seen.Add(key)) continue;

            if (writeIndex != readIndex) Hotkeys[writeIndex] = b;
            writeIndex++;
        }
        if (writeIndex == Hotkeys.Count) return false;

        Hotkeys.RemoveRange(writeIndex, Hotkeys.Count - writeIndex);
        return true;
    }

    /// <summary>
    /// Adds any built-in default hotkey bindings that aren't already represented in Hotkeys.
    /// "Represented" means: an existing entry with the same (Action, Parameter, BindingID) - including
    /// tombstoned entries with RemovedByUser=true - so a user who has explicitly removed a default
    /// is not re-seeded.
    /// Returns true when at least one default was newly added (caller should persist).
    /// </summary>
    public bool EnsureDefaultHotkeys()
    {
        bool added = false;
        foreach (HotkeyBinding d in CreateDefaultHotkeys())
        {
            bool present = false;
            foreach (HotkeyBinding existing in Hotkeys)
            {
                if (!existing.Matches(d.Action, d.Parameter, d.BindingID)) continue;

                present = true;
                break;
            }
            if (present) continue;

            Hotkeys.Add(new HotkeyBinding
            {
                Action = d.Action,
                Parameter = d.Parameter,
                Modifiers = d.Modifiers,
                VirtualKey = d.VirtualKey,
                Enabled = d.Enabled,
                BindingID = d.BindingID,
            });
            added = true;
        }
        return added;
    }
}
