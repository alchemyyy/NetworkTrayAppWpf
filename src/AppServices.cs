using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Services;
using NetworkTrayAppWPF.Visuals;

namespace NetworkTrayAppWPF;

/// <summary>
/// Strongly-typed slots for the handful of process-singleton services shared between App,
/// the windows, and a few service consumers.
/// Replaces the previous string-keyed <c>Application.Current.Properties</c> lookups - same lifetime
/// (set by App.OnStartup, lives for the process), same ownership story, but the dependency graph
/// is now compile-time greppable and consumers don't need <c>as T</c> / <c>!</c> casts.
/// All slots are nullable: a consumer that runs before its producer (or during a partially-initialised
/// startup that hit an exception) still sees <c>null</c> rather than throwing on cast.
/// </summary>
internal static class AppServices
{
    public static AppTheme? Theme { get; set; }
    public static AppSettings? Settings { get; set; }
    public static GlobalHotkeyService? HotkeyService { get; set; }
}
