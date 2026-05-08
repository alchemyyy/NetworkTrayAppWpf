using System.Windows.Controls;
using System.Windows.Threading;
using NetworkTrayAppWPF.Interop;
using NetworkTrayAppWPF.Models;
using Point = System.Windows.Point;

namespace NetworkTrayAppWPF;

/// <summary>
/// Manages the tray-icon lifecycle for the host application.
/// Owns the underlying ShellNotifyIcon and surfaces its events to the host.
/// Throttles icon updates so a hot-path updater can call <see cref="Update"/> without flicker.
///
/// Skeleton design: this class deliberately does not know how to render an icon.
/// The host supplies a delegate that produces a fresh <see cref="Icon"/> + tooltip pair on demand,
/// either through <see cref="Update"/> (throttled) or <see cref="ShellNotifyIcon"/> via direct setters.
/// Subscribe to <see cref="LeftClick"/>, <see cref="RightClick"/>, etc. to wire host actions.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly ShellNotifyIcon _shellIcon;
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    // Throttling state.
    private bool _isOnCooldown;
    private bool _updatePending;

    // Producer the host registers via Update; called immediately, and once more after the cooldown expires
    // if at least one Update came in while the icon was on cooldown.
    private Func<(Icon? icon, string tooltip)>? _getValues;

    /// <summary>
    /// Cooldown between icon updates in milliseconds.
    /// Defaults to <see cref="TimeConstants.TrayIconUpdateRateDefaultMs"/>.
    /// </summary>
    public int UpdateCooldownMs { get; set; } = TimeConstants.TrayIconUpdateRateDefaultMs;

    /// <summary>Raised when the tray icon receives left mouse down.</summary>
    public event Action? LeftMouseDown;

    /// <summary>Raised when the tray icon is left-clicked.</summary>
    public event Action? LeftClick;

    /// <summary>Raised when the tray icon is left double-clicked.</summary>
    public event Action? LeftDoubleClick;

    /// <summary>Raised when the tray icon is right-clicked, with the cursor position.</summary>
    public event Action<Point>? RightClick;

    /// <summary>Raised when the icon needs to be refreshed (e.g. taskbar restarted).</summary>
    public event Action? RefreshNeeded;

    /// <summary>
    /// Raised right before the shell shows the tooltip.
    /// Host can refresh tooltip text via <see cref="SetTooltip"/> here without going through <see cref="Update"/>.
    /// </summary>
    public event Action? TooltipPopup;

    /// <summary>Whether the tray icon is visible.</summary>
    public bool IsVisible
    {
        get => _shellIcon.IsVisible;
        set => _shellIcon.IsVisible = value;
    }

    public TrayIconManager()
    {
        // Capture the construction-thread dispatcher so Update() can marshal off-thread callers
        // back onto the window-owning thread. Shell_NotifyIconW is thread-affine and the cooldown flags
        // are unsynchronized - both want a single owner.
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _shellIcon = new ShellNotifyIcon();

        _shellIcon.LeftMouseDown += () => LeftMouseDown?.Invoke();
        _shellIcon.LeftClick += () => LeftClick?.Invoke();
        _shellIcon.LeftDoubleClick += () => LeftDoubleClick?.Invoke();
        _shellIcon.RightClick += point => RightClick?.Invoke(point);
        _shellIcon.RefreshNeeded += () => RefreshNeeded?.Invoke();
        _shellIcon.TooltipPopup += () => TooltipPopup?.Invoke();
    }

    /// <summary>
    /// Sets the tray icon directly, bypassing the cooldown. Useful for one-shot updates and tooltips
    /// outside the throttled <see cref="Update"/> path.
    /// </summary>
    public void SetIcon(Icon icon) => _shellIcon.SetIcon(icon);

    /// <summary>
    /// Sets the tray-icon tooltip text directly. ShellNotifyIcon dedupes internally so repeated calls
    /// with the same text are cheap.
    /// </summary>
    public void SetTooltip(string tooltip) => _shellIcon.SetTooltip(tooltip);

    /// <summary>
    /// Throttled icon refresh. The producer delegate is called immediately the first time, and again after
    /// the cooldown expires if at least one Update arrived during the cooldown window.
    /// Safe to call from any thread; off-thread calls are marshaled onto the dispatcher so Shell_NotifyIconW
    /// and the throttle flags stay single-owner.
    /// </summary>
    public void Update(Func<(Icon? icon, string tooltip)> getValues)
    {
        if (!_dispatcher.CheckAccess())
        {
            if (_dispatcher.HasShutdownStarted) return;
            _ = _dispatcher.BeginInvoke(() => Update(getValues));
            return;
        }

        _getValues = getValues;

        if (_isOnCooldown)
        {
            _updatePending = true;
            return;
        }

        ApplyUpdate();
        _ = StartCooldown();
    }

    /// <summary>
    /// Shows a context menu at the specified position.
    /// In <see cref="ContextMenuPosition.Classic"/> the menu opens at <paramref name="position"/>;
    /// in <see cref="ContextMenuPosition.Modern"/> the position is ignored and the menu docks
    /// to the bottom-right of the work area.
    /// </summary>
    public void ShowContextMenu(ContextMenu menu, Point position, ContextMenuPosition placement) =>
        _shellIcon.ShowContextMenu(menu, position, placement);

    private void ApplyUpdate()
    {
        if (_getValues == null) return;

        (Icon? icon, string tooltip) = _getValues();
        if (icon != null) _shellIcon.SetIcon(icon);
        _shellIcon.SetTooltip(tooltip);
    }

    private async Task StartCooldown()
    {
        try
        {
            _isOnCooldown = true;
            await Task.Delay(UpdateCooldownMs);
            _isOnCooldown = false;

            // If updates came in during cooldown, refresh once more from the producer.
            if (_updatePending && _getValues != null)
            {
                _updatePending = false;
                ApplyUpdate();
                _ = StartCooldown();
            }
        }
        catch (Exception ex)
        {
            _isOnCooldown = false;
            WPFLog.Log($"TrayIconManager.StartCooldown: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _shellIcon.Dispose();
    }
}
