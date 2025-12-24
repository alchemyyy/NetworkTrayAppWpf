using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NetworkTrayAppWpf.Interop;

/// <summary>
/// Extension methods for WPF windows - cloaking, rounded corners, extended styles.
/// </summary>
internal static class WindowExtensions
{
    private static readonly bool IsWindows11 = Environment.OSVersion.Version.Build >= 22000;

    /// <summary>
    /// Gets the Win32 handle for a WPF window.
    /// </summary>
    public static IntPtr GetHandle(this Window window)
    {
        return new WindowInteropHelper(window).Handle;
    }

    /// <summary>
    /// Cloaks/uncloaks a window using DWM. Cloaked windows are hidden but their HWND still exists.
    /// This is more efficient than destroying/recreating windows.
    /// </summary>
    public static void Cloak(this Window window, bool hide = true)
    {
        int attributeValue = hide ? 1 : 0;
        try
        {
            DwmApi.DwmSetWindowAttribute(
                window.GetHandle(),
                DwmApi.DWMA_CLOAK,
                ref attributeValue,
                Marshal.SizeOf(attributeValue));
        }
        catch
        {
            // DWM may not be available in some configurations
        }
    }

    /// <summary>
    /// Enables rounded corners on Windows 11.
    /// </summary>
    public static void EnableRoundedCorners(this Window window)
    {
        if (!IsWindows11) return;

        try
        {
            int attributeValue = (int)DwmApi.DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
            DwmApi.DwmSetWindowAttribute(
                window.GetHandle(),
                DwmApi.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref attributeValue,
                Marshal.SizeOf(attributeValue));
        }
        catch
        {
            // Ignore if not supported
        }
    }

    /// <summary>
    /// Enables dark mode title bar on Windows 11.
    /// </summary>
    public static void SetDarkMode(this Window window, bool enabled)
    {
        try
        {
            int attributeValue = enabled ? 1 : 0;
            DwmApi.DwmSetWindowAttribute(
                window.GetHandle(),
                DwmApi.DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref attributeValue,
                Marshal.SizeOf(attributeValue));
        }
        catch
        {
            // Ignore if not supported
        }
    }

    /// <summary>
    /// Adds the tool window extended style (removes from taskbar/Alt+Tab).
    /// </summary>
    public static void ApplyToolWindowStyle(this Window window)
    {
        IntPtr handle = window.GetHandle();
        IntPtr currentStyle = User32.GetWindowLongPtr(handle, User32.GWL_EXSTYLE);
        User32.SetWindowLongPtr(handle, User32.GWL_EXSTYLE,
            new IntPtr(currentStyle.ToInt64() | User32.WS_EX_TOOLWINDOW));
    }

    /// <summary>
    /// Adds the no-activate extended style.
    /// </summary>
    public static void ApplyNoActivateStyle(this Window window)
    {
        IntPtr handle = window.GetHandle();
        IntPtr currentStyle = User32.GetWindowLongPtr(handle, User32.GWL_EXSTYLE);
        User32.SetWindowLongPtr(handle, User32.GWL_EXSTYLE,
            new IntPtr(currentStyle.ToInt64() | User32.WS_EX_NOACTIVATE));
    }

    /// <summary>
    /// Removes the maximize box from the window.
    /// </summary>
    public static void RemoveMaximizeBox(this Window window)
    {
        IntPtr handle = window.GetHandle();
        IntPtr currentStyle = User32.GetWindowLongPtr(handle, User32.GWL_STYLE);
        User32.SetWindowLongPtr(handle, User32.GWL_STYLE,
            new IntPtr(currentStyle.ToInt64() & ~User32.WS_MAXIMIZEBOX));
    }
}
