using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using NetworkTrayAppWPF.Models;
using Point = System.Windows.Point;

namespace NetworkTrayAppWPF.Interop;

/// <summary>
/// Low-level shell notification icon implementation using Win32 APIs.
/// Pure interop wrapper - no business logic or throttling.
/// </summary>
internal sealed class ShellNotifyIcon : IDisposable
{
    public event Action? LeftMouseDown;
    public event Action? LeftClick;
    public event Action? LeftDoubleClick;
    public event Action<Point>? RightClick;
    public event Action? RefreshNeeded;
    /// <summary>
    /// Raised when the shell is about to display the icon's tooltip (NIN_POPUPOPEN).
    /// Use to refresh tooltip text against live state right before it becomes visible.
    /// </summary>
    public event Action? TooltipPopup;

    private const int WM_CALLBACKMOUSEMSG = User32.WM_USER + 1024;

    // Persistent GUID for this icon - reduces flicker on updates.
    // Derived from AppIdentity.AppGuid so two apps forked from the same skeleton can't
    // collide on the same icon identity in the shell registry (which would cause NIM_ADD
    // to fail and cross-app NIM_DELETEs to yank the wrong icon).
    private static readonly Guid IconGuid = new(AppIdentity.AppGuid);

    private readonly Win32Window _window;
    private readonly DispatcherTimer _taskbarRecreateTimer;
    private bool _isCreated;
    private bool _isVisible;
    private bool _disposed;
    private string _tooltipText = string.Empty;
    private Icon? _currentIcon;
    private bool _isContextMenuOpen;

    // Prevent double-click issues on Windows 11.
    private bool _hasProcessedButtonUp;
    private bool HasProcessedButtonUp
    {
        get
        {
            bool hasProcessedButtonUp = _hasProcessedButtonUp;
            _hasProcessedButtonUp = false;
            return hasProcessedButtonUp;
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

        // Re-registers the icon after the taskbar restarts.
        _taskbarRecreateTimer = new DispatcherTimer(DispatcherPriority.Normal)
        {
            Interval = TimeSpan.FromMilliseconds(TimeConstants.TaskbarRecreateCheckIntervalMs)
        };
        _taskbarRecreateTimer.Tick += OnTaskbarRecreateTimerTick;
    }

    public void SetIcon(Icon icon)
    {
        if (icon == _currentIcon) return;

        _currentIcon = icon;
        Update();
    }

    public void SetTooltip(string text)
    {
        if (text == _tooltipText) return;

        _tooltipText = text;
        Update();
    }

    private NOTIFYICONDATAW MakeData()
    {
        return new NOTIFYICONDATAW
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = _window.Handle,
            uFlags = NotifyIconFlags.NIF_MESSAGE
                | NotifyIconFlags.NIF_ICON
                | NotifyIconFlags.NIF_TIP
                | NotifyIconFlags.NIF_SHOWTIP
                | NotifyIconFlags.NIF_GUID,
            uCallbackMessage = WM_CALLBACKMOUSEMSG,
            hIcon = _currentIcon?.Handle ?? IntPtr.Zero,
            szTip = _tooltipText.Length > 127 ? _tooltipText[..127] : _tooltipText,
            guidItem = IconGuid
        };
    }

    private void Update()
    {
        if (_disposed) return;

        NOTIFYICONDATAW data = MakeData();

        if (!_isVisible)
        {
            if (_isCreated)
            {
                Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
                _isCreated = false;
            }
            return;
        }

        // Fast path: shell still knows about us, just push the new data.
        if (_isCreated && Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_MODIFY, ref data)) return;

        // Recovery path. Reached when either:
        //   - we never registered (first call, or a previous add failed), or
        //   - NIM_MODIFY just failed because the shell silently dropped the icon (sleep/resume,
        //     display-mode change, shell hiccup - none of which raise WM_TASKBARCREATED).
        // The persistent IconGuid means a re-add will be refused with E_FAIL
        // while the shell still holds a stale (GUID, hWnd) binding,
        // so issue a best-effort NIM_DELETE to clear it first.
        bool wasCreated = _isCreated;
        if (wasCreated) WPFLog.Log("ShellNotifyIcon.Update: NIM_MODIFY failed, falling back to delete+add recovery");
        _ = Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_DELETE, ref data);
        _isCreated = false;

        if (Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_ADD, ref data))
        {
            _isCreated = true;
            data.uTimeoutOrVersion = Shell32.NOTIFYICON_VERSION_4;
            Shell32.Shell_NotifyIconW(Shell32.NotifyIconMessage.NIM_SETVERSION, ref data);
        }
        else
        {
            int lastError = Marshal.GetLastWin32Error();
            WPFLog.Log($"ShellNotifyIcon.Update: NIM_ADD failed after recovery (lastError=0x{lastError:X8}); icon will retry on next update");
        }
    }

    private void WndProc(Message msg)
    {
        if (msg.Msg == WM_CALLBACKMOUSEMSG)
            CallbackMsgWndProc(msg);
        else if (msg.Msg == Shell32.WM_TASKBARCREATED)
        {
            // Taskbar recreated (explorer.exe restarted) - re-register icon
            ScheduleTaskbarRecreate();
        }
    }

    private void CallbackMsgWndProc(Message msg)
    {
        short notificationCode = (short)msg.LParam;

        switch (notificationCode)
        {
            case User32.WM_LBUTTONDOWN:
                LeftMouseDown?.Invoke();
                break;

            case (short)Shell32.NotifyIconNotification.NIN_SELECT:
            case User32.WM_LBUTTONUP:
                // Prevent double invocation on Windows 11 (barely works).
                if (!HasProcessedButtonUp)
                {
                    HasProcessedButtonUp = true;
                    LeftClick?.Invoke();
                }
                break;

            case User32.WM_LBUTTONDBLCLK:
                LeftDoubleClick?.Invoke();
                break;

            case User32.WM_RBUTTONUP:
            case User32.WM_CONTEXTMENU:
                Point cursorPosition = new(
                    (short)msg.WParam.ToInt32(),
                    msg.WParam.ToInt32() >> 16);
                RightClick?.Invoke(cursorPosition);
                break;

            case (short)Shell32.NotifyIconNotification.NIN_POPUPOPEN:
                TooltipPopup?.Invoke();
                break;
        }
    }

    private int _remainingTicks;

    private void ScheduleTaskbarRecreate()
    {
        _remainingTicks = 10;
        _taskbarRecreateTimer.Start();
        Update();
    }

    private void OnTaskbarRecreateTimerTick(object? sender, EventArgs e)
    {
        _remainingTicks--;
        if (_remainingTicks <= 0)
        {
            _taskbarRecreateTimer.Stop();
            RefreshNeeded?.Invoke();
        }
    }

    // Inset between the modern-placed menu and the work-area edges.
    private const double ModernMenuPadding = 8;

    /// <summary>
    /// Shows a context menu at the specified position.
    /// In <see cref="ContextMenuPosition.Classic"/> mode the menu opens at <paramref name="point"/>
    /// (physical screen pixels from the WM_RBUTTONUP packet);
    /// in <see cref="ContextMenuPosition.Modern"/> mode the cursor point is ignored,
    /// and the menu is anchored to the bottom-right of the primary work area, like the Win11 system flyouts.
    /// </summary>
    public void ShowContextMenu(ContextMenu contextMenu, Point point, ContextMenuPosition placement)
    {
        if (_isContextMenuOpen) return;

        _isContextMenuOpen = true;

        contextMenu.StaysOpen = true;
        contextMenu.Placement = PlacementMode.AbsolutePoint;

        if (placement == ContextMenuPosition.Modern)
        {
            // Pre-measure so we can place the menu inside the work area.
            // The menu is fully built with all items added,
            // so Measure produces a valid DesiredSize without opening the popup first.
            // SystemParameters.WorkArea is already in DIPs, matching WPF's coord space.
            contextMenu.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            System.Windows.Size desiredMenuSize = contextMenu.DesiredSize;

            Rect workArea = SystemParameters.WorkArea;

            // Center the menu on the tray icon in both axes, clamped inside the work area.
            // For a standard bottom taskbar, the icon lives below the work area,
            // so the vertical clamp pins the menu's bottom to workArea.Bottom - padding
            // while the horizontal center moves the menu directly above the icon.
            // Side/top taskbars get true centering along whichever axis the icon's center is in-bounds.
            // Falls back to the bottom-right corner when the icon's bounds aren't resolvable
            // (e.g. in the hidden overflow flyout, or not yet placed by the shell).
            double horizontalOffset = workArea.Right - desiredMenuSize.Width - ModernMenuPadding;
            double verticalOffset = workArea.Bottom - desiredMenuSize.Height - ModernMenuPadding;
            if (TryGetTrayIconRectInDips(out Rect iconRect))
            {
                double iconCenterX = (iconRect.Left + iconRect.Right) / 2.0;
                double iconCenterY = (iconRect.Top + iconRect.Bottom) / 2.0;
                double centeredLeft = iconCenterX - desiredMenuSize.Width / 2.0;
                double centeredTop = iconCenterY - desiredMenuSize.Height / 2.0;

                double minLeft = workArea.Left + ModernMenuPadding;
                double maxLeft = workArea.Right - desiredMenuSize.Width - ModernMenuPadding;
                if (maxLeft < minLeft) maxLeft = minLeft;
                horizontalOffset = Math.Clamp(centeredLeft, minLeft, maxLeft);

                double minTop = workArea.Top + ModernMenuPadding;
                double maxTop = workArea.Bottom - desiredMenuSize.Height - ModernMenuPadding;
                if (maxTop < minTop) maxTop = minTop;
                verticalOffset = Math.Clamp(centeredTop, minTop, maxTop);
            }
            contextMenu.HorizontalOffset = horizontalOffset;
            contextMenu.VerticalOffset = verticalOffset;
        }
        else
        {
            // Convert physical screen pixels to WPF DIPs.
            double dpiScale = GetDpiScale();
            contextMenu.HorizontalOffset = point.X / dpiScale;
            contextMenu.VerticalOffset = point.Y / dpiScale;
        }

        contextMenu.Opened += OnContextMenuOpened;
        contextMenu.Closed += OnContextMenuClosed;
        contextMenu.IsOpen = true;
    }

    /// <summary>
    /// Resolves the tray icon's screen rectangle and converts it from physical pixels to WPF DIPs.
    /// Returns false when the shell can't (or won't) report the bounds -
    /// typically when the icon is hidden in the overflow flyout, or hasn't been placed yet.
    /// </summary>
    private bool TryGetTrayIconRectInDips(out Rect rectDips)
    {
        NOTIFYICONIDENTIFIER id = new()
        {
            cbSize = Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = _window.Handle,
            guidItem = IconGuid,
        };

        if (Shell32.Shell_NotifyIconGetRect(ref id, out RECT rect) == 0)
        {
            double dpiScale = GetDpiScale();
            rectDips = new Rect(
                rect.Left / dpiScale,
                rect.Top / dpiScale,
                (rect.Right - rect.Left) / dpiScale,
                (rect.Bottom - rect.Top) / dpiScale);
            return true;
        }

        rectDips = default;
        return false;
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
                _ = User32.ReleaseDC(IntPtr.Zero, hdc);
                return dpi / 96.0;
            }
        }
        catch
        {
            // Fall through to default
        }
        return 1.0;
    }

    private static void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
        {
            // Take focus so menu works properly.
            if (PresentationSource.FromVisual(menu) is HwndSource source) User32.SetForegroundWindow(source.Handle);

            menu.Focus();
            menu.StaysOpen = false;

            // Disable exit animation for snappier feel.
            if (menu.Parent is Popup popup) popup.PopupAnimation = PopupAnimation.None;
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

        _taskbarRecreateTimer.Stop();
        IsVisible = false;
        _window.Dispose();
    }
}
