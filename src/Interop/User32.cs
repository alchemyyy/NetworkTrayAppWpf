using System.Runtime.InteropServices;

namespace NetworkTrayAppWPF.Interop;

/// <summary>
/// User32.dll and GDI32.dll interop declarations for tray icon and DPI handling.
/// </summary>
internal static class User32
{
    // Window messages used for tray icon callbacks
    public const int WM_USER = 0x0400;
    public const int WM_CONTEXTMENU = 0x007B;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;

    // WM_MOUSEACTIVATE: sent to a window when the user clicks it while it's inactive.
    // Returning MA_ACTIVATE (1) means "activate the window AND deliver this click as a normal mouse-down".
    // DefWindowProc for a custom-chrome (WindowStyle=None) modeless window can promote the result
    // to MA_ACTIVATEANDEAT (2) in some focus-handoff scenarios, swallowing the click - hence the override.
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int MA_ACTIVATE = 1;
    public const int MA_ACTIVATEANDEAT = 2;
    public const int MA_NOACTIVATE = 3;
    public const int MA_NOACTIVATEANDEAT = 4;

    // Virtual key codes for modifier detection
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;

    // System metrics and DPI
    public const int SM_CXSMICON = 49;
    public const int LOGPIXELSX = 88;

    // MessageBox flags
    public const uint MB_ICONERROR = 0x10;

    // Global hotkeys (RegisterHotKey / WM_HOTKEY)
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
    public const int WM_HOTKEY = 0x0312;
    public static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int hotKeyID, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int hotKeyID);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    public static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetricsForDpi(int nIndex, uint dpi);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    public static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    public static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);
}
