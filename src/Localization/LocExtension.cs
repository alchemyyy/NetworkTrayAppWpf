using System.Windows.Markup;
using Binding = System.Windows.Data.Binding;
using BindingMode = System.Windows.Data.BindingMode;

namespace NetworkTrayAppWPF.Localization;

/// <summary>
/// XAML markup extension that binds a UI property to a localized string.
/// Usage in XAML:
///   xmlns:loc="clr-namespace:NetworkTrayAppWPF.Localization"
///   Text="{loc:Loc MyResourceKey}"
/// Expands into a one-way Binding on LocalizationManager.Instance[Key];
/// the manager raises Binding.IndexerName ("Item[]") on culture change so the binding refreshes.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class LocExtension : MarkupExtension
{
    /// <summary>
    /// Resource key from Strings.resx. Required.
    /// </summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public LocExtension() { }

    public LocExtension(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key)) return string.Empty;

        Binding binding = new($"[{Key}]")
        {
            Source = LocalizationManager.Instance,
            Mode = BindingMode.OneWay,
        };

        // Delegating to Binding.ProvideValue lets this extension serve both DependencyProperty targets
        // (returns a BindingExpression) and plain CLR property targets (returns the resolved value).
        return binding.ProvideValue(serviceProvider);
    }
}
