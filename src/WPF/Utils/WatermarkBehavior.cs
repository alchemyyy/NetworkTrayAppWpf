using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

namespace NetworkTrayAppWPF.WPF.Utils;

/// <summary>
/// Attached behavior that paints dimmed placeholder text inside a TextBox when the box is empty
/// and not keyboard-focused.
/// Implementation uses a single AdornerLayer-hosted overlay TextBlock so the host TextBox's own
/// content host stays untouched (no template surgery, no Background swap).
/// </summary>
public static class WatermarkBehavior
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(WatermarkBehavior),
        new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject d) => (string?)d.GetValue(TextProperty);

    public static void SetText(DependencyObject d, string? value) => d.SetValue(TextProperty, value);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox tb) return;

        // Wire the host once on first non-null assignment; subsequent text swaps just refresh the adorner.
        if (e.OldValue == null && e.NewValue != null) AttachHooks(tb);

        // Re-evaluate immediately so a change pushed at any time updates the visible watermark.
        Refresh(tb);
    }

    private static void AttachHooks(TextBox tb)
    {
        tb.Loaded += (_, _) => Refresh(tb);
        tb.Unloaded += (_, _) => RemoveAdorner(tb);
        tb.GotKeyboardFocus += (_, _) => Refresh(tb);
        tb.LostKeyboardFocus += (_, _) => Refresh(tb);
        tb.TextChanged += (_, _) => Refresh(tb);
        tb.IsVisibleChanged += (_, _) => Refresh(tb);
    }

    private static void Refresh(TextBox tb)
    {
        string? watermark = GetText(tb);
        bool show = !string.IsNullOrEmpty(watermark)
                && string.IsNullOrEmpty(tb.Text)
                && tb is { IsKeyboardFocused: false, IsVisible: true };

        if (show)
            EnsureAdorner(tb, watermark!);
        else
            RemoveAdorner(tb);
    }

    private static void EnsureAdorner(TextBox tb, string watermark)
    {
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(tb);
        if (layer == null) return;

        Adorner[]? existing = layer.GetAdorners(tb);
        if (existing != null)
        {
            foreach (Adorner a in existing)
            {
                if (a is WatermarkAdorner wa)
                {
                    wa.UpdateText(watermark);
                    return;
                }
            }
        }

        layer.Add(new WatermarkAdorner(tb, watermark));
    }

    private static void RemoveAdorner(TextBox tb)
    {
        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(tb);

        Adorner[]? existing = layer?.GetAdorners(tb);
        if (existing == null) return;

        foreach (Adorner a in existing)
        {
            if (a is WatermarkAdorner wa)
                layer?.Remove(wa);
        }
    }

    private sealed class WatermarkAdorner : Adorner
    {
        private readonly TextBlock _label;

        public WatermarkAdorner(TextBox host, string text) : base(host)
        {
            IsHitTestVisible = false;

            _label = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                IsHitTestVisible = false,
                // Reduce opacity for the dimmed-default look while inheriting the host's themed
                // foreground - keeps the placeholder readable in both light and dark themes
                // without binding to a separate brush resource.
                Opacity = 0.45,
            };

            // Bind every text-rendering property live to the host TextBox so adorner construction
            // ahead of full style application doesn't snapshot Padding/FontSize/etc. as their
            // CLR defaults (which is how we previously ended up with 0 left padding).
            // Padding goes through a 2x horizontal scale: TextBox's TextBoxView and TextBlock's
            // line layout offset glyphs differently from the element edge, so matching the host's
            // raw Padding leaves the placeholder visually short - doubling left/right lines up.
            BindHost(_label, TextBlock.PaddingProperty, host, Control.PaddingProperty,
                HorizontalDoubleThicknessConverter.Instance);
            BindHost(_label, TextBlock.ForegroundProperty, host, Control.ForegroundProperty);
            BindHost(_label, TextBlock.FontSizeProperty, host, Control.FontSizeProperty);
            BindHost(_label, TextBlock.FontFamilyProperty, host, Control.FontFamilyProperty);
            BindHost(_label, TextBlock.FontStyleProperty, host, Control.FontStyleProperty);
            BindHost(_label, TextBlock.FontWeightProperty, host, Control.FontWeightProperty);
            BindHost(_label, TextBlock.TextAlignmentProperty, host, TextBox.TextAlignmentProperty);

            AddVisualChild(_label);
            AddLogicalChild(_label);
        }

        private static void BindHost(
            DependencyObject target, DependencyProperty targetProperty,
            DependencyObject source, DependencyProperty sourceProperty,
            IValueConverter? converter = null)
        {
            BindingOperations.SetBinding(target, targetProperty, new Binding
            {
                Source = source,
                Path = new PropertyPath(sourceProperty),
                Mode = BindingMode.OneWay,
                Converter = converter,
            });
        }

        public void UpdateText(string text) => _label.Text = text;

        protected override int VisualChildrenCount => 1;

        protected override Visual GetVisualChild(int index) => _label;

        protected override Size MeasureOverride(Size constraint)
        {
            _label.Measure(constraint);
            return ((TextBox)AdornedElement).RenderSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            _label.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }

    private sealed class HorizontalDoubleThicknessConverter : IValueConverter
    {
        public static readonly HorizontalDoubleThicknessConverter Instance = new();

        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is Thickness t ? new Thickness(t.Left * 2, t.Top, t.Right * 2, t.Bottom) : value!;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            Binding.DoNothing;
    }
}
