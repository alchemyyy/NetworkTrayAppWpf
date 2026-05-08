namespace NetworkTrayAppWPF.Visuals;

/// <summary>
/// Canonical Segoe Fluent Icons codepoint strings used across the app.
/// Single source of truth referenced by tray-icon renderers and the XML-overridable
/// <see cref="AppTheme"/> glyph property defaults.
/// Add new entries here rather than scattering raw codepoints through the codebase.
/// </summary>
internal static class GlyphCatalog
{
    // ===========================================================================
    // Generic UI glyphs (defaults for the AppTheme.Glyph* properties; user-overridable via theme.xml)
    // ===========================================================================

    public const string SETTINGS = "\uE713";  // Setting (gear)
    public const string POWER    = "\uE7E8";  // Power
    public const string INFO     = "\uE946";  // Info
    public const string EXIT     = "\uE8BB";  // ChromeClose

    // Network tray app: Ethernet plug glyph baked into the static app.ico bitmap.
    public const string ETHERNET = "\uE839";  // Ethernet
}
