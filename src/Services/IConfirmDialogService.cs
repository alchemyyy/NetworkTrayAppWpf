namespace NetworkTrayAppWPF.Services;

/// <summary>
/// Shell-owned facade for raising the settings window's modal confirm overlay.
/// Implemented by the host (the <c>SettingsWindow</c>); the overlay element itself stays on the
/// shell so per-section UserControls don't have to duplicate it. UserControls and section helpers
/// receive this interface and call <see cref="ConfirmAsync"/> instead of poking the overlay
/// directly via fields.
/// </summary>
public interface IConfirmDialogService
{
    /// <summary>
    /// Show the confirm overlay with the supplied strings and resolve the returned task with the
    /// user's choice (true = confirm, false = cancel). Calls are expected to come in from the UI
    /// thread; only one prompt at a time is supported (matches the existing single-overlay UX).
    /// </summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText);
}
