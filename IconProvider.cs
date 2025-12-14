using System.Windows.Media;

namespace NetworkTrayAppWpf;

/// <summary>
/// Provides icon glyphs and colors for network states.
/// </summary>
public sealed class IconProvider(AppSettings settings)
{
    // Hardcoded colors for the theme that doesn't use custom settings
    private const string DarkThemeConnected = "#FFFFFF";
    private const string DarkThemeNoInternet = "#FFB900";
    private const string DarkThemeDisconnected = "#808080";
    private const string LightThemeConnected = "#000000";
    private const string LightThemeNoInternet = "#996600";
    private const string LightThemeDisconnected = "#666666";

    /// <summary>
    /// Whether the taskbar is using light theme.
    /// </summary>
    public bool IsLightTheme { get; set; }

    private const string GlyphEthernet = "\uE839";
    private const string GlyphWifi0 = "\uE871";
    private const string GlyphWifi1 = "\uE872";
    private const string GlyphWifi2 = "\uE873";
    private const string GlyphWifi3 = "\uE874";
    private const string GlyphWifi4 = "\uE701";
    private const string GlyphNoNetwork = "\uF384";

    public static FontFamily IconFontFamily { get; } = GetIconFontFamily();

    private static FontFamily GetIconFontFamily()
    {
        bool isWindows11 = Environment.OSVersion.Version.Build >= 22000;
        return new FontFamily(isWindows11 ? "Segoe Fluent Icons" : "Segoe MDL2 Assets");
    }

    public static string GetGlyph(NetworkIconState state) => state switch
    {
        NetworkIconState.NoNetwork => GlyphNoNetwork,
        NetworkIconState.EthernetConnected => GlyphEthernet,
        NetworkIconState.EthernetNoInternet => GlyphEthernet,
        NetworkIconState.EthernetDisconnected => GlyphEthernet,
        NetworkIconState.WifiDisconnected => GlyphWifi0,
        NetworkIconState.WifiConnecting => GlyphWifi1,
        NetworkIconState.Wifi0Bars or NetworkIconState.Wifi0BarsNoInternet => GlyphWifi0,
        NetworkIconState.Wifi1Bar or NetworkIconState.Wifi1BarNoInternet => GlyphWifi1,
        NetworkIconState.Wifi2Bars or NetworkIconState.Wifi2BarsNoInternet => GlyphWifi2,
        NetworkIconState.Wifi3Bars or NetworkIconState.Wifi3BarsNoInternet => GlyphWifi3,
        NetworkIconState.Wifi4Bars or NetworkIconState.Wifi4BarsNoInternet => GlyphWifi4,
        _ => GlyphNoNetwork
    };

    public Color GetColor(NetworkIconState state)
    {
        // Determine whether to use custom colors based on theme and setting
        bool useCustomColors = settings.Icon.ApplyColorsToLightTheme ? IsLightTheme : !IsLightTheme;

        string connectedColor = useCustomColors ? settings.Icon.ConnectedColor :
            (IsLightTheme ? LightThemeConnected : DarkThemeConnected);
        string noInternetColor = useCustomColors ? settings.Icon.NoInternetColor :
            (IsLightTheme ? LightThemeNoInternet : DarkThemeNoInternet);
        string disconnectedColor = useCustomColors ? settings.Icon.DisconnectedColor :
            (IsLightTheme ? LightThemeDisconnected : DarkThemeDisconnected);

        return state switch
        {
            NetworkIconState.NoNetwork => ParseColor(disconnectedColor),
            NetworkIconState.EthernetConnected => ParseColor(connectedColor),
            NetworkIconState.EthernetNoInternet => ParseColor(noInternetColor),
            NetworkIconState.EthernetDisconnected => ParseColor(disconnectedColor),
            NetworkIconState.WifiDisconnected => ParseColor(disconnectedColor),
            NetworkIconState.WifiConnecting => ParseColor(noInternetColor),
            NetworkIconState.Wifi0Bars or NetworkIconState.Wifi1Bar or
            NetworkIconState.Wifi2Bars or NetworkIconState.Wifi3Bars or
            NetworkIconState.Wifi4Bars => ParseColor(connectedColor),
            NetworkIconState.Wifi0BarsNoInternet or NetworkIconState.Wifi1BarNoInternet or
            NetworkIconState.Wifi2BarsNoInternet or NetworkIconState.Wifi3BarsNoInternet or
            NetworkIconState.Wifi4BarsNoInternet => ParseColor(noInternetColor),
            _ => IsLightTheme ? Colors.Black : Colors.White
        };
    }

    public SolidColorBrush GetBrush(NetworkIconState state)
    {
        return new SolidColorBrush(GetColor(state));
    }

    private static Color ParseColor(string hexColor)
    {
        try
        {
            if (hexColor.StartsWith('#'))
            {
                hexColor = hexColor[1..];
            }

            switch (hexColor.Length)
            {
                case 6:
                {
                    byte r = Convert.ToByte(hexColor[0..2], 16);
                    byte g = Convert.ToByte(hexColor[2..4], 16);
                    byte b = Convert.ToByte(hexColor[4..6], 16);
                    return Color.FromArgb(255, r, g, b);
                }
                case 8:
                {
                    byte a = Convert.ToByte(hexColor[0..2], 16);
                    byte r = Convert.ToByte(hexColor[2..4], 16);
                    byte g = Convert.ToByte(hexColor[4..6], 16);
                    byte b = Convert.ToByte(hexColor[6..8], 16);
                    return Color.FromArgb(a, r, g, b);
                }
            }
        }
        catch
        {
            // Fall through to default
        }

        return Colors.White;
    }
}
