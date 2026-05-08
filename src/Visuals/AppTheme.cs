using System.IO;
using System.Xml;
using System.Xml.Serialization;
using NetworkTrayAppWPF.Models;
using Microsoft.Win32;
using Color = System.Windows.Media.Color;

namespace NetworkTrayAppWPF.Visuals;

/// <summary>
/// A pair of hex-string colors (light + dark theme variants), XML-serializable.
///
/// Storage: <see cref="LightHex"/> and <see cref="DarkHex"/> are the only persisted state
/// (round-tripped to theme.xml as XML attributes).
/// The <see cref="Light"/> and <see cref="Dark"/> accessors parse on demand and return WPF <see cref="Color"/>;
/// <see cref="For"/> picks one based on the active theme.
/// </summary>
public class ThemeColor
{
    [XmlAttribute] public string LightHex { get; set; } = "#000000";
    [XmlAttribute] public string DarkHex { get; set; } = "#000000";

    [XmlIgnore] public Color Light => ParseHex(LightHex);
    [XmlIgnore] public Color Dark => ParseHex(DarkHex);

    /// <summary>Picks the light or dark variant for the active theme.</summary>
    public Color For(bool isLightTheme) => isLightTheme ? Light : Dark;

    public ThemeColor() { }

    /// <summary>
    /// Builds from 6-char RGB or 8-char ARGB hex strings (one per theme).
    /// '#' prefix is optional; case-insensitive. Throws on bad input - intended for compile-time-known literals.
    /// </summary>
    public ThemeColor(string lightHex, string darkHex)
    {
        LightHex = Normalize(lightHex);
        DarkHex = Normalize(darkHex);
    }

    /// <summary>
    /// Builds from a single 6-char RGB or 8-char ARGB hex string, applied to both light and dark variants.
    /// </summary>
    public ThemeColor(string hex) : this(hex, hex) { }

    public ThemeColor(Color light, Color dark)
    {
        LightHex = ToHex(light);
        DarkHex = ToHex(dark);
    }

    private static string Normalize(string hex)
    {
        string normalized = hex.StartsWith('#') ? hex : "#" + hex;
        // Validate eagerly so a bad literal trips at construction, not on first .Light access.
        ParseHex(normalized);
        return normalized;
    }

    private static string ToHex(Color c) => c.A == 255
        ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
        : $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color ParseHex(string hex)
    {
        string h = hex.TrimStart('#');
        return h.Length switch
        {
            6 => Color.FromArgb(0xFF,
                Convert.ToByte(h[..2], 16),
                Convert.ToByte(h[2..4], 16),
                Convert.ToByte(h[4..6], 16)),
            8 => Color.FromArgb(
                Convert.ToByte(h[..2], 16),
                Convert.ToByte(h[2..4], 16),
                Convert.ToByte(h[4..6], 16),
                Convert.ToByte(h[6..8], 16)),
            _ => throw new ArgumentException($"Hex must be 6 (RGB) or 8 (ARGB) chars, got '{hex}'.", nameof(hex)),
        };
    }
}

/// <summary>
/// Centralized theme: every color and glyph lives directly here as instance state, persisted to theme.xml.
/// Also handles system light/dark detection and exposes the theme-change event the rest of the app subscribes to.
///
/// All colors layer the same way:
/// a paired <see cref="ThemeColor"/> (light + dark) on this instance is the theme default;
/// per-color user overrides on <see cref="AppSettings"/> win when present.
/// Single-color chrome (close button, toggle switch, etc.) uses identical light and dark variants
/// until a real second value is needed.
///
/// Construction does no IO.
/// Use <see cref="LoadOrDefault"/> / <see cref="Load"/> to read theme.xml,
/// and <see cref="Save"/> / <see cref="SaveToDefaultPath"/> to write it.
/// </summary>
[XmlRoot("Theme")]
public sealed class AppTheme : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private bool _disposed;
    private bool _lastKnownIsLightTheme;

    /// <summary>Theme name for identification.</summary>
    [XmlAttribute] public string Name { get; set; } = "Default";

    /// <summary>Theme version for compatibility checking.</summary>
    [XmlAttribute] public int Version { get; set; } = 1;

    // ===========================================================================
    // Core chrome colors
    // ===========================================================================

    /// <summary>Primary background color for windows and panels.</summary>
    public ThemeColor Background { get; set; } = new("F3F3F3", "202020");

    /// <summary>Primary text and icon color.</summary>
    public ThemeColor Foreground { get; set; } = new("000000", "FFFFFF");

    /// <summary>Border color for windows and containers.</summary>
    public ThemeColor Border { get; set; } = new("E0E0E0", "454545");

    /// <summary>Separator line color.</summary>
    public ThemeColor Separator { get; set; } = new("E5E5E5", "3A3A3A");

    /// <summary>Hover state background color.</summary>
    public ThemeColor Hover { get; set; } = new("E9E9E9", "333333");

    /// <summary>Pressed state background color.</summary>
    public ThemeColor Pressed { get; set; } = new("DFDFDF", "2A2A2A");

    /// <summary>Background color for input controls.</summary>
    public ThemeColor ControlBackground { get; set; } = new("FFFFFF", "3C3C3C");

    /// <summary>Border color for input controls.</summary>
    public ThemeColor ControlBorder { get; set; } = new("808080", "444444");

    /// <summary>Disabled text/icon color.</summary>
    public ThemeColor DisabledForeground { get; set; } = new("808080");

    /// <summary>Windows accent color.</summary>
    public ThemeColor Accent { get; set; } = new("0078D4");

    /// <summary>Semi-transparent acrylic background.</summary>
    public ThemeColor Acrylic { get; set; } = new("D0F3F3F3", "D0202020");

    /// <summary>Secondary text color (slightly dimmed).</summary>
    public ThemeColor SecondaryForeground { get; set; } = new("222222", "DDDDDD");

    /// <summary>Footer background color.</summary>
    public ThemeColor FooterBackground { get; set; } = new("E8E8E8", "1A1A1A");

    /// <summary>Slider track background color.</summary>
    public ThemeColor SliderTrack { get; set; } = new("C0C0C0", "3A3A3A");

    /// <summary>Slider progress (filled) color.</summary>
    public ThemeColor SliderProgress { get; set; } = new("606060", "6A6A6A");

    /// <summary>Slider thumb color.</summary>
    public ThemeColor SliderThumb { get; set; } = new("404040", "F0F0F0");

    /// <summary>Button hover background.</summary>
    public ThemeColor ButtonHover { get; set; } = new("D5D5D5", "3A3A3A");

    /// <summary>Button pressed background.</summary>
    public ThemeColor ButtonPressed { get; set; } = new("CACACA", "4A4A4A");

    /// <summary>Icon foreground color.</summary>
    public ThemeColor IconForeground { get; set; } = new("222222", "DDDDDD");

    /// <summary>Win11 Settings card background (slightly lighter than body).</summary>
    public ThemeColor CardBackground { get; set; } = new("FBFBFB", "2B2B2B");

    /// <summary>Focused TextBox background, a shade darker than <see cref="ControlBackground"/>.</summary>
    public ThemeColor TextBoxFocused { get; set; } = new("F5F5F5", "363636");

    // ===========================================================================
    // Single-color chrome (same light and dark variants)
    // ===========================================================================

    /// <summary>Toggle switch on-state track color.</summary>
    public ThemeColor ToggleSwitchOnTrack { get; set; } = new("5B5B5B");

    /// <summary>Toggle switch on-state thumb color.</summary>
    public ThemeColor ToggleSwitchOnThumb { get; set; } = new("FFFFFF");

    /// <summary>Window close-button hover background.</summary>
    public ThemeColor CloseButtonHover { get; set; } = new("C42B1C");

    /// <summary>Window close-button pressed background.</summary>
    public ThemeColor CloseButtonPressed { get; set; } = new("A42B1C");

    /// <summary>Window close-button glyph color while active.</summary>
    public ThemeColor CloseButtonGlyphActive { get; set; } = new("FFFFFF");

    /// <summary>Modal flyout overlay backdrop.</summary>
    public ThemeColor FlyoutOverlayBackdrop { get; set; } = new("A0000000");

    // ===========================================================================
    // Glyphs (Segoe Fluent Icons codepoints; user-overridable via theme.xml)
    // ===========================================================================

    /// <summary>Settings/gear icon.</summary>
    public string GlyphSettings { get; set; } = GlyphCatalog.SETTINGS;

    /// <summary>Power/exit button icon.</summary>
    public string GlyphPower { get; set; } = GlyphCatalog.POWER;

    /// <summary>Info icon.</summary>
    public string GlyphInfo { get; set; } = GlyphCatalog.INFO;

    /// <summary>Close icon.</summary>
    public string GlyphExit { get; set; } = GlyphCatalog.EXIT;

    // ===========================================================================
    // Runtime state (not persisted)
    // ===========================================================================

    /// <summary>Whether the system is currently using light theme.</summary>
    [XmlIgnore] public bool IsLightTheme { get; private set; }

    /// <summary>Raised when the system theme changes. Parameter is true for light theme.</summary>
    public event Action<bool>? ThemeChanged;

    public AppTheme()
    {
        IsLightTheme = DetectSystemLightTheme();
        _lastKnownIsLightTheme = IsLightTheme;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    // ===========================================================================
    // Persistence
    // ===========================================================================

    /// <summary>
    /// Default theme.xml path inside the user's local app data folder.
    /// </summary>
    public static string GetDefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appFolder = Path.Combine(appData, Program.ApplicationName);
        Directory.CreateDirectory(appFolder);
        return Path.Combine(appFolder, "theme.xml");
    }

    /// <summary>
    /// Loads from XML; returns a default-initialized instance when the file is missing or unreadable.
    /// </summary>
    public static AppTheme LoadOrDefault(string filePath)
    {
        try
        {
            if (File.Exists(filePath)) return Load(filePath);
        }
        catch
        {
            // fall through and return default on any load error
        }
        return new AppTheme();
    }

    /// <summary>
    /// Loads from XML.
    /// Throws on IO or deserialization failure - use <see cref="LoadOrDefault"/> for a tolerant variant.
    /// </summary>
    public static AppTheme Load(string filePath)
    {
        using FileStream stream = new(filePath, FileMode.Open);
        XmlSerializer serializer = new(typeof(AppTheme));
        return (AppTheme?)serializer.Deserialize(stream) ?? new AppTheme();
    }

    /// <summary>Writes the current theme to XML at the given path.</summary>
    public void Save(string filePath)
    {
        XmlSerializerNamespaces namespaces = new();
        namespaces.Add("", ""); // suppress default xmlns

        XmlWriterSettings writerSettings = new()
        {
            Indent = true,
            IndentChars = "  ",
            NewLineChars = Environment.NewLine,
            NewLineHandling = NewLineHandling.Replace,
        };

        using FileStream stream = new(filePath, FileMode.Create);
        using XmlWriter writer = XmlWriter.Create(stream, writerSettings);
        XmlSerializer serializer = new(typeof(AppTheme));
        serializer.Serialize(writer, this, namespaces);
    }

    /// <summary>Writes the current theme to <see cref="GetDefaultPath"/>.</summary>
    public void SaveToDefaultPath() => Save(GetDefaultPath());

    // ===========================================================================
    // System theme detection
    // ===========================================================================

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // Theme changes come through as General category.
        if (e.Category != UserPreferenceCategory.General) return;

        bool newIsLightTheme = DetectSystemLightTheme();
        if (newIsLightTheme == _lastKnownIsLightTheme) return;

        _lastKnownIsLightTheme = newIsLightTheme;
        IsLightTheme = newIsLightTheme;
        ThemeChanged?.Invoke(newIsLightTheme);
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
            return false; // default to dark theme
        }
    }

    /// <summary>Detects whether the "Apps use light theme" setting is enabled.</summary>
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

    // ===========================================================================
    // Color resolvers (user override on AppSettings -> theme color on this instance)
    // ===========================================================================

    /// <summary>Foreground: <see cref="AppSettings.TextColor"/> override -> <see cref="Foreground"/>.</summary>
    public Color ResolveForeground(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.TextColor.Resolve(isLightTheme) is { } color) return color;
        return Foreground.For(isLightTheme);
    }

    /// <summary>Background: <see cref="AppSettings.BackgroundColor"/> override -> <see cref="Background"/>.</summary>
    public Color ResolveBackground(AppSettings? settings, bool isLightTheme)
    {
        if (settings?.BackgroundColor.Resolve(isLightTheme) is { } color) return color;
        return Background.For(isLightTheme);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
