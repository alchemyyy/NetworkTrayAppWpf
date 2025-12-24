using System.Runtime.InteropServices;

namespace NetworkTrayAppWpf.Interop;

/// <summary>
/// User32.dll interop declarations for window management and acrylic effects.
/// </summary>
internal static class User32
{
    public const int WM_USER = 0x0400;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONUP = 0x0208;
    public const int WM_SETTINGCHANGE = 0x001A;
    public const int SPI_SETWORKAREA = 0x002F;

    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_MAXIMIZEBOX = 0x10000;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;

    public const int SM_CXSMICON = 49;
    public const int LOGPIXELSX = 88;

    [DllImport("user32.dll", PreserveSig = true)]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy,
        WindowPosFlags uFlags);

    [Flags]
    public enum WindowPosFlags : uint
    {
        SWP_NOSIZE = 0x0001,
        SWP_NOMOVE = 0x0002,
        SWP_NOZORDER = 0x0004,
        SWP_NOACTIVATE = 0x0010,
    }

    [DllImport("user32.dll", PreserveSig = true)]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", PreserveSig = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", PreserveSig = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", PreserveSig = true)]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll", PreserveSig = true)]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll", PreserveSig = true)]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll", PreserveSig = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll", PreserveSig = true)]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    // Acrylic/composition attributes
    [DllImport("user32.dll", PreserveSig = true)]
    internal static extern int SetWindowCompositionAttribute(
        IntPtr hwnd,
        ref WindowCompositionAttribData data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttribData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    internal enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19,
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public AccentState AccentState;
        public AccentFlags AccentFlags;
        public uint GradientColor;
        public uint AnimationId;
    }

    [Flags]
    public enum AccentFlags
    {
        None = 0x0,
        DrawLeftBorder = 0x20,
        DrawTopBorder = 0x40,
        DrawRightBorder = 0x80,
        DrawBottomBorder = 0x100,
        DrawAllBorders = DrawLeftBorder | DrawTopBorder | DrawRightBorder | DrawBottomBorder
    }

    internal enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }
}
