namespace NetworkTrayAppWpf;

/// <summary>
/// Represents the various network icon states that can be displayed.
/// </summary>
public enum NetworkIconState
{
    NoNetwork,
    EthernetConnected,
    EthernetNoInternet,
    EthernetDisconnected,
    WifiDisconnected,
    WifiConnecting,
    Wifi0Bars,
    Wifi1Bar,
    Wifi2Bars,
    Wifi3Bars,
    Wifi4Bars,
    Wifi0BarsNoInternet,
    Wifi1BarNoInternet,
    Wifi2BarsNoInternet,
    Wifi3BarsNoInternet,
    Wifi4BarsNoInternet
}

/// <summary>
/// Represents the style of network flyout to display on left-click.
/// </summary>
public enum FlyoutStyle
{
    Windows10,
    Windows11,
    QuickSettings,
    AvailableNetworks,
    Settings
}

/// <summary>
/// Represents the style of adapter settings window to open.
/// </summary>
public enum AdapterSettingsStyle
{
    /// <summary>
    /// Opens the Control Panel Network Connections applet (ncpa.cpl).
    /// </summary>
    ControlPanel,

    /// <summary>
    /// Opens Network Connections in an Explorer window using shell GUID.
    /// </summary>
    Explorer
}
