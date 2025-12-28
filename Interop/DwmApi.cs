using System.Runtime.InteropServices;

namespace NetworkTrayAppWpf.Interop;

/// <summary>
/// DWM (Desktop Window Manager) API for window cloaking and rounded corners.
/// </summary>
internal static partial class DwmApi
{
    internal const int DWMA_CLOAK = 13;
    internal const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    internal const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    internal enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    // Keep as DllImport - PreserveSig = false throws on error which LibraryImport doesn't support
    [DllImport("dwmapi.dll", PreserveSig = false)]
    internal static extern void DwmSetWindowAttribute(
        IntPtr hwnd,
        int attr,
        ref int attrValue,
        int attrSize);
}
