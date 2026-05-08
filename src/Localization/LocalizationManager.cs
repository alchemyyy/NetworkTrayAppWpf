using System.ComponentModel;
using System.Globalization;
using Binding = System.Windows.Data.Binding;

namespace NetworkTrayAppWPF.Localization;

/// <summary>
/// Single source of truth for the active UI culture and the bridge between
/// .resx-backed string lookups and WPF data binding.
/// XAML uses {loc:Loc Key}, which expands into a Binding on this manager's indexer;
/// raising PropertyChanged for Binding.IndexerName invalidates every such binding,
/// so changing CurrentCulture re-resolves all visible strings without rebuilding the visual tree.
/// </summary>
public sealed class LocalizationManager : INotifyPropertyChanged
{
    public static LocalizationManager Instance { get; } = new();

    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    private LocalizationManager() { }

    /// <summary>
    /// Active UI culture for all resource lookups.
    /// Setting this updates Strings.Culture, the thread/default cultures, and notifies bindings.
    /// </summary>
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (Equals(_currentCulture, value)) return;

            _currentCulture = value;

            // Keep the strongly-typed Strings accessor in lockstep so code-side lookups
            // (Strings.SomeKey) follow the same culture as XAML lookups.
            Strings.Culture = value;

            // Apply to the current thread and to any future threads spawned by the app.
            Thread.CurrentThread.CurrentCulture = value;
            Thread.CurrentThread.CurrentUICulture = value;
            CultureInfo.DefaultThreadCurrentCulture = value;
            CultureInfo.DefaultThreadCurrentUICulture = value;

            // Binding.IndexerName ("Item[]") is the WPF convention for "every indexer entry changed":
            // a single notification refreshes every {loc:Loc ...} binding in the visual tree.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
            CultureChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Raised after CurrentCulture changes. Subscribers that don't bind through XAML
    /// (e.g. code-built tray menu headers) can use this to re-resolve their strings.
    /// </summary>
    public event EventHandler? CultureChanged;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Looks up a localized string by its .resx key in the active culture.
    /// Returns the key itself when missing so a typo surfaces visibly in the UI rather than silently blanking.
    /// XAML reaches this via {loc:Loc Key} which expands to {Binding [Key], Source=Instance}.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;

            string? resolved = Strings.ResourceManager.GetString(key, _currentCulture);
            return resolved ?? key;
        }
    }

    /// <summary>
    /// One-shot called once during App.OnStartup. Pushes the initial culture through the same code path
    /// that runtime changes use so Strings.Culture and the thread cultures are aligned from the very first lookup.
    /// </summary>
    public void Initialize(CultureInfo? culture = null)
    {
        CultureInfo target = culture ?? CultureInfo.CurrentUICulture;

        // Force notification even if target equals the field default
        // so Strings.Culture and the thread cultures get set on first run.
        Strings.Culture = target;
        Thread.CurrentThread.CurrentCulture = target;
        Thread.CurrentThread.CurrentUICulture = target;
        CultureInfo.DefaultThreadCurrentCulture = target;
        CultureInfo.DefaultThreadCurrentUICulture = target;
        _currentCulture = target;
    }
}
