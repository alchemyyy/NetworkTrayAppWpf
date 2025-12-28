using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;

namespace NetworkTrayAppWpf;

/// <summary>
/// Renders network tray icons with proper layered compositing.
/// Uses a similar technique to EarTrumpet for crisp, theme-aware icons, with proper "unfilled bars" backdrop rendering.
/// </summary>
public sealed class TrayIconRenderer(AppSettings settings) : IDisposable
{
    private Icon? _currentIcon;
    private bool _disposed;

    // Icon font families - lazy init to avoid static constructor COM issues with trimming
    private static Typeface? _segoeFluent;
    private static Typeface? _segoeMDL2;
    private static Typeface SegoeFluent => _segoeFluent ??= new(new System.Windows.Media.FontFamily("Segoe Fluent Icons"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static Typeface SegoeMDL2 => _segoeMDL2 ??= new(new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly bool IsWindows11 = Environment.OSVersion.Version.Build >= 22000;

    // Backdrop opacity for "unfilled" bars effect
    private const double BackdropOpacity = 0.55;

    // Glyphs for network states
    private const string GlyphEthernet = "\uE839";
    private const string GlyphWifi0 = "\uE871";  // 0 bars (outline only)
    private const string GlyphWifi1 = "\uE872";  // 1 bar filled
    private const string GlyphWifi2 = "\uE873";  // 2 bars filled
    private const string GlyphWifi3 = "\uE874";  // 3 bars filled
    private const string GlyphWifi4 = "\uE701";  // Full signal (all bars)
    private const string GlyphNoNetwork = "\uF384";

    /// <summary>
    /// Whether the taskbar is using light theme.
    /// </summary>
    public bool IsLightTheme { get; set; }

    /// <summary>
    /// Renders and returns an icon for the given network state.
    /// The returned icon should be disposed when no longer needed.
    /// </summary>
    public Icon CreateIcon(NetworkIconState state)
    {
        // Get taskbar DPI for proper sizing
        uint dpi = GetTaskbarDpi();
        int iconSize = GetIconSizeForDpi(dpi);

        // Get the color for this state
        Color foregroundColor = GetColor(state);

        // Determine which glyphs to render
        (string? backdropGlyph, string foregroundGlyph) = GetGlyphsForState(state);

        // Render the icon with optional backdrop layer
        Icon icon = RenderLayeredIcon(iconSize, backdropGlyph, foregroundGlyph, foregroundColor);

        // Dispose previous icon
        Icon? oldIcon = _currentIcon;
        _currentIcon = icon;
        oldIcon?.Dispose();

        return icon;
    }

    /// <summary>
    /// Returns the glyphs to use for layered rendering.
    /// For WiFi states with partial bars, returns (fullBarsGlyph, actualBarsGlyph).
    /// </summary>
    private static (string? backdropGlyph, string foregroundGlyph) GetGlyphsForState(NetworkIconState state)
    {
        return state switch
        {
            // WiFi states with partial bars - use full wifi as backdrop, actual bars as foreground
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi0BarsNoInternet => (GlyphWifi4, GlyphWifi0),
            NetworkIconState.Wifi1Bar or NetworkIconState.Wifi1BarNoInternet => (GlyphWifi4, GlyphWifi1),
            NetworkIconState.Wifi2Bars or NetworkIconState.Wifi2BarsNoInternet => (GlyphWifi4, GlyphWifi2),
            NetworkIconState.Wifi3Bars or NetworkIconState.Wifi3BarsNoInternet => (GlyphWifi4, GlyphWifi3),
            NetworkIconState.Wifi4Bars or NetworkIconState.Wifi4BarsNoInternet => (null, GlyphWifi4),

            // WiFi disconnected/connecting - show empty wifi with backdrop
            NetworkIconState.WifiDisconnected => (GlyphWifi4, GlyphWifi0),
            NetworkIconState.WifiConnecting => (GlyphWifi4, GlyphWifi1),

            // Ethernet states - no backdrop needed
            NetworkIconState.EthernetConnected or
            NetworkIconState.EthernetNoInternet or
            NetworkIconState.EthernetDisconnected => (null, GlyphEthernet),

            // No network - no backdrop needed
            _ => (null, GlyphNoNetwork)
        };
    }

    /// <summary>
    /// Renders a layered icon with optional semi-transparent backdrop.
    /// </summary>
    private Icon RenderLayeredIcon(int size, string? backdropGlyph, string foregroundGlyph, Color foregroundColor)
    {
        // Calculate backdrop color (same hue, reduced opacity)
        Color backdropColor = Color.FromArgb(
            (byte)(foregroundColor.A * BackdropOpacity),
            foregroundColor.R,
            foregroundColor.G,
            foregroundColor.B);

        // Create the drawing visual
        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // Font size - use most of the icon space for the glyph
            double fontSize = size;// optional scaling factor * 1.0;
            Typeface typeface = IsWindows11 ? SegoeFluent : SegoeMDL2;

            // Draw backdrop glyph if specified (at reduced opacity)
            if (!string.IsNullOrEmpty(backdropGlyph))
            {
                DrawGlyph(dc, backdropGlyph, typeface, fontSize, size, backdropColor);
            }

            // Draw foreground glyph (full opacity)
            DrawGlyph(dc, foregroundGlyph, typeface, fontSize, size, foregroundColor);
        }

        // Render to bitmap
        RenderTargetBitmap rtb = new(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        // Convert to Icon
        return BitmapToIcon(rtb);
    }

    /// <summary>
    /// Draws a centered glyph using the specified settings.
    /// </summary>
    private static void DrawGlyph(DrawingContext dc, string glyph, Typeface typeface, double fontSize, int canvasSize, Color color)
    {
        // Use 1.0 for pixelsPerDip since we're rendering at 96 DPI and scaling the size
        FormattedText formattedText = new(
            glyph,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            1.0);

        // Center the glyph
        double x = (canvasSize - formattedText.Width) / 2;
        double y = (canvasSize - formattedText.Height) / 2;

        dc.DrawText(formattedText, new System.Windows.Point(x, y));
    }

    /// <summary>
    /// Converts a WPF RenderTargetBitmap to a System.Drawing.Icon.
    /// </summary>
    private static Icon BitmapToIcon(RenderTargetBitmap rtb)
    {
        // Convert to PNG bytes first
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using MemoryStream pngStream = new();
        encoder.Save(pngStream);
        pngStream.Position = 0;

        // Create System.Drawing.Bitmap from PNG
        using Bitmap bitmap = new(pngStream);

        // Create icon from bitmap
        IntPtr hIcon = bitmap.GetHicon();
        Icon icon = Icon.FromHandle(hIcon);

        // Clone the icon so we can destroy the handle
        Icon clonedIcon = (Icon)icon.Clone();
        Interop.User32.DestroyIcon(hIcon);

        return clonedIcon;
    }

    /// <summary>
    /// Gets the color for the given network state.
    /// </summary>
    private Color GetColor(NetworkIconState state)
    {
        // Hardcoded theme colors (matching Windows 11 defaults)
        const string DarkThemeConnected = "#FFFFFF";
        const string DarkThemeNoInternet = "#FFB900";
        const string DarkThemeDisconnected = "#808080";
        const string LightThemeConnected = "#000000";
        const string LightThemeNoInternet = "#996600";
        const string LightThemeDisconnected = "#666666";

        // Determine whether to use custom colors
        bool useCustomColors = settings.Icon.ApplyColorsToLightTheme ? IsLightTheme : !IsLightTheme;

        string connectedColor = useCustomColors ? settings.Icon.ConnectedColor :
            (IsLightTheme ? LightThemeConnected : DarkThemeConnected);
        string noInternetColor = useCustomColors ? settings.Icon.NoInternetColor :
            (IsLightTheme ? LightThemeNoInternet : DarkThemeNoInternet);
        string disconnectedColor = useCustomColors ? settings.Icon.DisconnectedColor :
            (IsLightTheme ? LightThemeDisconnected : DarkThemeDisconnected);

        return state switch
        {
            NetworkIconState.NoNetwork => ParseColor(disconnectedColor, IsLightTheme),
            NetworkIconState.EthernetConnected => ParseColor(connectedColor, IsLightTheme),
            NetworkIconState.EthernetNoInternet => ParseColor(noInternetColor, IsLightTheme),
            NetworkIconState.EthernetDisconnected => ParseColor(disconnectedColor, IsLightTheme),
            NetworkIconState.WifiDisconnected => ParseColor(disconnectedColor, IsLightTheme),
            NetworkIconState.WifiConnecting => ParseColor(noInternetColor, IsLightTheme),
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi1Bar or
            NetworkIconState.Wifi2Bars or NetworkIconState.Wifi3Bars or
            NetworkIconState.Wifi4Bars => ParseColor(connectedColor, IsLightTheme),
            NetworkIconState.Wifi0BarsNoInternet or NetworkIconState.Wifi1BarNoInternet or
            NetworkIconState.Wifi2BarsNoInternet or NetworkIconState.Wifi3BarsNoInternet or
            NetworkIconState.Wifi4BarsNoInternet => ParseColor(noInternetColor, IsLightTheme),
            _ => IsLightTheme ? Colors.Black : Colors.White
        };
    }

    private static Color ParseColor(string hexColor, bool isLightTheme)
    {
        try
        {
            if (hexColor.StartsWith('#'))
                hexColor = hexColor[1..];

            return hexColor.Length switch
            {
                6 => Color.FromArgb(255,
                    Convert.ToByte(hexColor[0..2], 16),
                    Convert.ToByte(hexColor[2..4], 16),
                    Convert.ToByte(hexColor[4..6], 16)),
                8 => Color.FromArgb(
                    Convert.ToByte(hexColor[0..2], 16),
                    Convert.ToByte(hexColor[2..4], 16),
                    Convert.ToByte(hexColor[4..6], 16),
                    Convert.ToByte(hexColor[6..8], 16)),
                _ => isLightTheme ? Colors.Black : Colors.White
            };
        }
        catch
        {
            return isLightTheme ? Colors.Black : Colors.White;
        }
    }

    #region DPI and Size Helpers

    private static uint GetTaskbarDpi()
    {
        try
        {
            // Try to get DPI from the primary monitor
            IntPtr hdc = Interop.User32.GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                int dpi = Interop.User32.GetDeviceCaps(hdc, Interop.User32.LOGPIXELSX);
                Interop.User32.ReleaseDC(IntPtr.Zero, hdc);
                return (uint)dpi;
            }
        }
        catch
        {
            // Fall through to default
        }

        return 96; // Default DPI
    }

    private static int GetIconSizeForDpi(uint dpi)
    {
        // Use GetSystemMetricsForDpi for proper DPI-aware sizing
        // This returns the correct icon size for the specified DPI
        // SM_CXSMICON (49) is the small icon width metric
        try
        {
            return Interop.User32.GetSystemMetricsForDpi(Interop.User32.SM_CXSMICON, dpi);
        }
        catch
        {
            // Fallback for older Windows versions without GetSystemMetricsForDpi
            // Base small icon is 16px at 96 DPI
            return (int)(16 * dpi / 96.0);
        }
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _currentIcon?.Dispose();
        _currentIcon = null;
    }
}
