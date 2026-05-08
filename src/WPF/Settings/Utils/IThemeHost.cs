namespace NetworkTrayAppWPF.WPF.Settings.Utils;

/// <summary>
/// Shell-owned facade for theme-induced chrome updates that reach into the host window's HWND.
/// The <see cref="Pages.ThemePage"/> calls back through this seam after a ThemeMode change
/// so the shell can re-apply the DWM dark-mode title bar attribute against its own window handle.
/// The page must not call DwmSetWindowAttribute directly because it has no HWND of its own.
/// </summary>
public interface IThemeHost
{
    /// <summary>
    /// Re-evaluate the effective light/dark state from AppSettings
    /// and push the DWM immersive-dark-mode attribute onto the host window's HWND.
    /// Safe to call before the window's HWND exists - the shell short-circuits in that case.
    /// </summary>
    void ApplyDwmDarkMode();
}
