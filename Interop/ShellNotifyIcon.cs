using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;

namespace NetworkTrayAppWpf.Interop;

/// <summary>
/// Custom shell notification icon implementation using Win32 APIs.
/// This replaces the heavy H.NotifyIcon library with a minimal implementation.
/// </summary>
internal sealed class ShellNotifyIcon : IDisposable
{
    public event Action? LeftClick;
    public event Action<System.Windows.Point>? RightClick;

    private const int WM_CALLBACKMOUSEMSG = User32.WM_USER + 1024;

    private readonly Win32Window _window;
    private readonly DispatcherTimer _iconUpdateTimer;
    private bool _isCreated;
    private bool _isVisible;
    private bool _disposed;
    private string _tooltipText = string.Empty;
    private Icon? _currentIcon;
    private bool _isContextMenuOpen;

    // Prevent double-click issues on Windows 11
    private bool _hasProcessedButtonUp;
    private bool HasProcessedButtonUp
    {
        get
        {
            bool val = _hasProcessedButtonUp;
            _hasProcessedButtonUp = false;
            return val;
        }
        set => _hasProcessedButtonUp = value;
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (value != _isVisible)
            {
                _isVisible = value;
                Update();
            }
        }
    }

    public ShellNotifyIcon()
    {
        _window = new Win32Window();
        _window.Initialize(WndProc);

        // Timer for delayed icon updates after display changes
        _iconUpdateTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _iconUpdateTimer.Tick += OnIconUpdateTimerTick;

        // Subscribe to display changes
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void SetIcon(Icon icon)
    {
        _currentIcon = icon;
        Update();
    }

    public void SetTooltip(string text)
    {
        _tooltipText = text ?? string.Empty;
        Update();
    }

    private NOTIFYICONDATAW MakeData()
    {
        return new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW)),
            hWnd = _window.Handle,
            uFlags = NotifyIconFlags.NIF_MESSAGE | NotifyIconFlags.NIF_ICON | NotifyIconFlags.NIF_TIP | NotifyIconFlags.NIF_SHOWTIP,
            uCallbackMessage = WM_CALLBACKMOUSEMSG,
            hIcon = _currentIcon?.Handle ?? IntPtr.Zero,
            szTip = _tooltipText.Length > 127 ? _tooltipText[..127] : _tooltipText
        };
    }

    private void Update()
    {
        if (_disposed) return;

        var data = MakeData();

        if (_isVisible)
        {
            if (_isCreated)
            {
                if (!Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_MODIFY, ref data))
                {
                    // Shell may have restarted
                    _isCreated = false;
                    Update();
                }
            }
            else
            {
                if (Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_ADD, ref data))
                {
                    _isCreated = true;
                    data.uTimeoutOrVersion = Shell32.NOTIFYICON_VERSION_4;
                    Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_SETVERSION, ref data);
                }
            }
        }
        else if (_isCreated)
        {
            Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
            _isCreated = false;
        }
    }

    private void WndProc(System.Windows.Forms.Message msg)
    {
        if (msg.Msg == WM_CALLBACKMOUSEMSG)
        {
            CallbackMsgWndProc(msg);
        }
        else if (msg.Msg == Shell32.WM_TASKBARCREATED ||
                (msg.Msg == User32.WM_SETTINGCHANGE && (int)msg.WParam == User32.SPI_SETWORKAREA))
        {
            // Taskbar recreated or work area changed - schedule icon update
            ScheduleIconUpdate();
        }
    }

    private void CallbackMsgWndProc(System.Windows.Forms.Message msg)
    {
        short notification = (short)msg.LParam;

        switch (notification)
        {
            case (short)Shell32.NotifyIconNotification.NIN_SELECT:
            case User32.WM_LBUTTONUP:
                // Prevent double invocation on Windows 11
                if (!HasProcessedButtonUp)
                {
                    HasProcessedButtonUp = true;
                    LeftClick?.Invoke();
                }
                break;

            case User32.WM_RBUTTONUP:
            case User32.WM_CONTEXTMENU:
                var point = new System.Windows.Point(
                    (short)msg.WParam.ToInt32(),
                    msg.WParam.ToInt32() >> 16);
                RightClick?.Invoke(point);
                break;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        ScheduleIconUpdate();
    }

    private int _remainingTicks;

    private void ScheduleIconUpdate()
    {
        _remainingTicks = 10;
        _iconUpdateTimer.Start();
        Update();
    }

    private void OnIconUpdateTimerTick(object? sender, EventArgs e)
    {
        _remainingTicks--;
        if (_remainingTicks <= 0)
        {
            _iconUpdateTimer.Stop();
            Update();
        }
    }

    /// <summary>
    /// Shows a context menu at the specified position.
    /// </summary>
    public void ShowContextMenu(ContextMenu contextMenu, System.Windows.Point point)
    {
        if (_isContextMenuOpen) return;
        _isContextMenuOpen = true;

        // Convert physical screen pixels to WPF DIPs
        double dpiScale = GetDpiScale();

        contextMenu.StaysOpen = true;
        contextMenu.Placement = PlacementMode.AbsolutePoint;
        contextMenu.HorizontalOffset = point.X / dpiScale;
        contextMenu.VerticalOffset = point.Y / dpiScale;

        contextMenu.Opened += OnContextMenuOpened;
        contextMenu.Closed += OnContextMenuClosed;
        contextMenu.IsOpen = true;
    }

    /// <summary>
    /// Gets the current DPI scale factor (e.g., 1.0 for 100%, 1.25 for 125%, 1.5 for 150%).
    /// </summary>
    private static double GetDpiScale()
    {
        try
        {
            IntPtr hdc = User32.GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                int dpi = User32.GetDeviceCaps(hdc, User32.LOGPIXELSX);
                User32.ReleaseDC(IntPtr.Zero, hdc);
                return dpi / 96.0;
            }
        }
        catch
        {
            // Fall through to default
        }
        return 1.0;
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            // Take focus so menu works properly
            if (HwndSource.FromVisual(menu) is HwndSource source)
            {
                User32.SetForegroundWindow(source.Handle);
            }
            menu.Focus();
            menu.StaysOpen = false;

            // Disable exit animation for snappier feel
            if (menu.Parent is Popup popup)
            {
                popup.PopupAnimation = PopupAnimation.None;
            }
        }
    }

    private void OnContextMenuClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            menu.Opened -= OnContextMenuOpened;
            menu.Closed -= OnContextMenuClosed;
        }
        _isContextMenuOpen = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _iconUpdateTimer.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        IsVisible = false;
        _window.Dispose();
    }
}
