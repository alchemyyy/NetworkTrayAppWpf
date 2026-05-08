using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NetworkTrayAppWPF.Models;
using Color = System.Windows.Media.Color;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;

namespace NetworkTrayAppWPF.Visuals;

/// <summary>
/// Renders the network tray icon from a <see cref="NetworkIconState"/>.
/// Wi-Fi states are drawn as a dimmed full-bars backdrop plus a foreground glyph
/// at the actual bar count, so the icon reads "5 of 4 bars" the same way the OS shell does.
/// Ethernet and no-network states draw a single foreground glyph.
/// Foreground color is per-state (connected / no-internet / disconnected) and theme-aware,
/// with optional user overrides supplied by the caller via the *ColorOverride properties.
/// </summary>
public sealed class NetworkTrayIconRenderer : IDisposable
{
    // Backdrop opacity for the dimmed full-bars layer behind partial Wi-Fi glyphs
    private const double BackdropOpacity = 0.55;

    // Default theme colors per state (matching Windows 11 system tray defaults)
    private const string DarkConnected = "#FFFFFF";
    private const string DarkNoInternet = "#FFB900";
    private const string DarkDisconnected = "#808080";
    private const string LightConnected = "#000000";
    private const string LightNoInternet = "#996600";
    private const string LightDisconnected = "#666666";

    // Glyphs (Segoe Fluent Icons / MDL2)
    private const string GlyphEthernet = "\uE839";
    private const string GlyphWifi0 = "\uE871";  // outline only (0 bars)
    private const string GlyphWifi1 = "\uE872";
    private const string GlyphWifi2 = "\uE873";
    private const string GlyphWifi3 = "\uE874";
    private const string GlyphWifi4 = "\uE701";  // full signal (used as 4-bar AND as backdrop)
    private const string GlyphNoNetwork = "\uF384";

    // Lazy init - avoids static-constructor COM issues with trimming.
    private static Typeface? _segoeFluent;
    private static Typeface? _segoeMDL2;
    private static Typeface SegoeFluent => _segoeFluent ??=
        new Typeface(new FontFamily("Segoe Fluent Icons"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static Typeface SegoeMDL2 => _segoeMDL2 ??=
        new Typeface(new FontFamily("Segoe MDL2 Assets"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly bool IsWindows11 = Environment.OSVersion.Version.Build >= 22000;

    private Icon? _currentIcon;
    private bool _disposed;
    private bool _isDirty = true;

    // Inputs - any change flips the dirty flag so the next CreateIcon re-renders.
    private bool _isLightTheme;
    private NetworkIconState _state = NetworkIconState.NoNetwork;
    private Color? _connectedOverride;
    private Color? _noInternetOverride;
    private Color? _disconnectedOverride;

    public bool IsLightTheme
    {
        get => _isLightTheme;
        set
        {
            if (_isLightTheme == value) return;
            _isLightTheme = value;
            _isDirty = true;
        }
    }

    public NetworkIconState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            _isDirty = true;
        }
    }

    /// <summary>Optional override for the "connected" color (Ethernet w/ internet, Wi-Fi w/ internet).</summary>
    public Color? ConnectedColorOverride
    {
        get => _connectedOverride;
        set
        {
            if (_connectedOverride == value) return;
            _connectedOverride = value;
            _isDirty = true;
        }
    }

    /// <summary>Optional override for the "no internet" color (any flavor of "connected but no internet").</summary>
    public Color? NoInternetColorOverride
    {
        get => _noInternetOverride;
        set
        {
            if (_noInternetOverride == value) return;
            _noInternetOverride = value;
            _isDirty = true;
        }
    }

    /// <summary>Optional override for the "disconnected" color (no network at all).</summary>
    public Color? DisconnectedColorOverride
    {
        get => _disconnectedOverride;
        set
        {
            if (_disconnectedOverride == value) return;
            _disconnectedOverride = value;
            _isDirty = true;
        }
    }

    public void InvalidateCache() => _isDirty = true;

    /// <summary>
    /// Renders and returns an Icon for the current State.
    /// Returns the cached icon when nothing has changed since the last render.
    /// </summary>
    public Icon CreateIcon()
    {
        if (!_isDirty && _currentIcon != null) return _currentIcon;

        _isDirty = false;

        uint dpi = IconRenderingHelper.GetTaskbarDpi();
        int iconSize = IconRenderingHelper.GetIconSizeForDpi(dpi);

        Color foregroundColor = ResolveColorForState(_state);
        (string? backdropGlyph, string foregroundGlyph) = GetGlyphsForState(_state);

        Icon icon = RenderLayeredIcon(iconSize, backdropGlyph, foregroundGlyph, foregroundColor);

        Icon? oldIcon = _currentIcon;
        _currentIcon = icon;
        oldIcon?.Dispose();

        return icon;
    }

    private static (string? backdropGlyph, string foregroundGlyph) GetGlyphsForState(NetworkIconState state)
    {
        return state switch
        {
            // Wi-Fi with partial bars: full glyph behind, actual bars in front.
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi0BarsNoInternet => (GlyphWifi4, GlyphWifi0),
            NetworkIconState.Wifi1Bar or NetworkIconState.Wifi1BarNoInternet => (GlyphWifi4, GlyphWifi1),
            NetworkIconState.Wifi2Bars or NetworkIconState.Wifi2BarsNoInternet => (GlyphWifi4, GlyphWifi2),
            NetworkIconState.Wifi3Bars or NetworkIconState.Wifi3BarsNoInternet => (GlyphWifi4, GlyphWifi3),
            NetworkIconState.Wifi4Bars or NetworkIconState.Wifi4BarsNoInternet => (null, GlyphWifi4),

            // Wi-Fi disconnected / connecting: empty wifi with full backdrop.
            NetworkIconState.WifiDisconnected => (GlyphWifi4, GlyphWifi0),
            NetworkIconState.WifiConnecting => (GlyphWifi4, GlyphWifi1),

            // Ethernet states: single glyph.
            NetworkIconState.EthernetConnected or
            NetworkIconState.EthernetNoInternet or
            NetworkIconState.EthernetDisconnected => (null, GlyphEthernet),

            // No network: single glyph.
            _ => (null, GlyphNoNetwork),
        };
    }

    private static Icon RenderLayeredIcon(
        int size, string? backdropGlyph, string foregroundGlyph, Color foregroundColor)
    {
        Color backdropColor = Color.FromArgb(
            (byte)(foregroundColor.A * BackdropOpacity),
            foregroundColor.R, foregroundColor.G, foregroundColor.B);

        DrawingVisual visual = new();
        using (DrawingContext dc = visual.RenderOpen())
        {
            double fontSize = size;
            Typeface typeface = IsWindows11 ? SegoeFluent : SegoeMDL2;

            if (!string.IsNullOrEmpty(backdropGlyph))
                DrawGlyph(dc, backdropGlyph, typeface, fontSize, size, backdropColor);

            DrawGlyph(dc, foregroundGlyph, typeface, fontSize, size, foregroundColor);
        }

        RenderTargetBitmap rtb = new(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);

        Icon icon = IconRenderingHelper.BitmapToIcon(rtb);
        rtb.Clear();
        return icon;
    }

    private static void DrawGlyph(
        DrawingContext dc, string glyph, Typeface typeface, double fontSize, int canvasSize, Color color)
    {
        FormattedText formattedText = new(
            glyph,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            new SolidColorBrush(color),
            1.0);

        double x = (canvasSize - formattedText.Width) / 2;
        double y = (canvasSize - formattedText.Height) / 2;

        dc.DrawText(formattedText, new Point(x, y));
    }

    private Color ResolveColorForState(NetworkIconState state)
    {
        return state switch
        {
            NetworkIconState.NoNetwork => DisconnectedColor,
            NetworkIconState.EthernetConnected => ConnectedColor,
            NetworkIconState.EthernetNoInternet => NoInternetColor,
            NetworkIconState.EthernetDisconnected => DisconnectedColor,
            NetworkIconState.WifiDisconnected => DisconnectedColor,
            NetworkIconState.WifiConnecting => NoInternetColor,
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi1Bar or
            NetworkIconState.Wifi2Bars or NetworkIconState.Wifi3Bars or
            NetworkIconState.Wifi4Bars => ConnectedColor,
            NetworkIconState.Wifi0BarsNoInternet or NetworkIconState.Wifi1BarNoInternet or
            NetworkIconState.Wifi2BarsNoInternet or NetworkIconState.Wifi3BarsNoInternet or
            NetworkIconState.Wifi4BarsNoInternet => NoInternetColor,
            _ => IsLightTheme ? Colors.Black : Colors.White,
        };
    }

    private Color ConnectedColor =>
        _connectedOverride ?? ParseHex(IsLightTheme ? LightConnected : DarkConnected);

    private Color NoInternetColor =>
        _noInternetOverride ?? ParseHex(IsLightTheme ? LightNoInternet : DarkNoInternet);

    private Color DisconnectedColor =>
        _disconnectedOverride ?? ParseHex(IsLightTheme ? LightDisconnected : DarkDisconnected);

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
            _ => Colors.White,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _currentIcon?.Dispose();
        _currentIcon = null;
    }
}
