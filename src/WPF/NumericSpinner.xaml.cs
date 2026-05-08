using System.Globalization;
using System.Windows;
using System.Windows.Input;
using NetworkTrayAppWPF.WPF.Utils;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using UserControl = System.Windows.Controls.UserControl;

namespace NetworkTrayAppWPF.WPF;

/// <summary>
/// Reusable integer spinner with up/down chevron buttons, mouse-wheel and arrow-key adjustment,
/// modifier-aware step sizes, integer-only typed input, optional units suffix, and an optional
/// "inherit" sentinel where an empty text value maps to <see cref="InheritValue"/>.
/// </summary>
public partial class NumericSpinner : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(NumericSpinner),
        new FrameworkPropertyMetadata(0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.Journal,
            OnValueChanged));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(0, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(int.MaxValue, OnRangeChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(1));

    public static readonly DependencyProperty WheelStepProperty = DependencyProperty.Register(
        nameof(WheelStep), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(1));

    public static readonly DependencyProperty LargeStepProperty = DependencyProperty.Register(
        nameof(LargeStep), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(10));

    public static readonly DependencyProperty ExtraLargeStepProperty = DependencyProperty.Register(
        nameof(ExtraLargeStep), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(100));

    public static readonly DependencyProperty SuffixProperty = DependencyProperty.Register(
        nameof(Suffix), typeof(string), typeof(NumericSpinner),
        new PropertyMetadata(string.Empty, OnSuffixChanged));

    public static readonly DependencyProperty AllowInheritProperty = DependencyProperty.Register(
        nameof(AllowInherit), typeof(bool), typeof(NumericSpinner),
        new PropertyMetadata(false, OnInheritDescriptorChanged));

    public static readonly DependencyProperty InheritValueProperty = DependencyProperty.Register(
        nameof(InheritValue), typeof(int), typeof(NumericSpinner),
        new PropertyMetadata(-1, OnInheritDescriptorChanged));

    public static readonly DependencyProperty PlaceholderTextProperty = DependencyProperty.Register(
        nameof(PlaceholderText), typeof(string), typeof(NumericSpinner),
        new PropertyMetadata(string.Empty, OnPlaceholderTextChanged));

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Base step for an unmodified up/down arrow press, and for spinner button clicks.</summary>
    public int Step
    {
        get => (int)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    /// <summary>Step applied per mouse-wheel notch.</summary>
    public int WheelStep
    {
        get => (int)GetValue(WheelStepProperty);
        set => SetValue(WheelStepProperty, value);
    }

    /// <summary>Step applied when Ctrl is held with up/down arrow.</summary>
    public int LargeStep
    {
        get => (int)GetValue(LargeStepProperty);
        set => SetValue(LargeStepProperty, value);
    }

    /// <summary>Step applied when Ctrl+Shift is held with up/down arrow.</summary>
    public int ExtraLargeStep
    {
        get => (int)GetValue(ExtraLargeStepProperty);
        set => SetValue(ExtraLargeStepProperty, value);
    }

    /// <summary>Optional unit label (e.g. "ms", "%") rendered to the right of the number.</summary>
    public string Suffix
    {
        get => (string)GetValue(SuffixProperty);
        set => SetValue(SuffixProperty, value);
    }

    /// <summary>
    /// When true, an empty text field is a valid state mapped to <see cref="InheritValue"/>;
    /// stepping from inherit lands on <see cref="Minimum"/> for up and <see cref="Maximum"/> for down.
    /// </summary>
    public bool AllowInherit
    {
        get => (bool)GetValue(AllowInheritProperty);
        set => SetValue(AllowInheritProperty, value);
    }

    /// <summary>Sentinel <see cref="Value"/> that represents the "inherit/empty" state.</summary>
    public int InheritValue
    {
        get => (int)GetValue(InheritValueProperty);
        set => SetValue(InheritValueProperty, value);
    }

    /// <summary>
    /// Dimmed placeholder text shown inside the value box when it is empty
    /// (i.e. <see cref="AllowInherit"/> is true and <see cref="Value"/> equals <see cref="InheritValue"/>)
    /// and the box is not keyboard-focused. Vanishes the instant focus enters or content appears.
    /// </summary>
    public string PlaceholderText
    {
        get => (string)GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    private bool _suppressValuePush;

    public NumericSpinner() => InitializeComponent();

    /// <summary>Fired after <see cref="Value"/> changes (typed entry, wheel, key, button, or external set).</summary>
    public event EventHandler<int>? ValueChanged;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NumericSpinner s) return;

        s.SyncTextFromValue();
        s.ValueChanged?.Invoke(s, (int)e.NewValue);
    }

    private static void OnSuffixChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NumericSpinner s) return;

        s.PART_Suffix.Text = (e.NewValue as string) ?? string.Empty;
    }

    private static void OnPlaceholderTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NumericSpinner s) return;

        WatermarkBehavior.SetText(s.PART_TextBox, (e.NewValue as string) ?? string.Empty);
        s.UpdateSuffixOpacity();
    }

    // AllowInherit / InheritValue can land after the binding has already pushed Value, so the
    // initial SyncTextFromValue ran with stale defaults and rendered the literal value instead of
    // the empty/inherit form. Re-syncing on either change pulls the textbox back to "" so the
    // watermark wins.
    private static void OnInheritDescriptorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not NumericSpinner s) return;

        s.SyncTextFromValue();
    }

    // Matches the suffix label's opacity to the watermark's so a row showing the placeholder
    // ("100", "300", etc.) reads as a single dimmed unit instead of "dimmed value + bright unit".
    // Constant kept in sync with WatermarkBehavior's internal Opacity value.
    private const double PlaceholderOpacity = 0.45;

    private void UpdateSuffixOpacity()
    {
        bool placeholderShowing = !string.IsNullOrEmpty(PlaceholderText)
            && string.IsNullOrEmpty(PART_TextBox.Text)
            && !PART_TextBox.IsKeyboardFocused;
        PART_Suffix.Opacity = placeholderShowing ? PlaceholderOpacity : 1.0;
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Re-clamp when bounds shift; relies on Value being settable.
        if (d is not NumericSpinner s) return;

        if (s.AllowInherit && s.Value == s.InheritValue) return;

        int clamped = Math.Clamp(s.Value, s.Minimum, s.Maximum);
        if (clamped != s.Value) s.Value = clamped;
    }

    private void SyncTextFromValue()
    {
        if (_suppressValuePush) return;

        string text = AllowInherit && Value == InheritValue
            ? string.Empty
            : Value.ToString(CultureInfo.InvariantCulture);
        if (PART_TextBox.Text != text)
        {
            PART_TextBox.Text = text;
            PART_TextBox.CaretIndex = text.Length;
        }
        UpdateSuffixOpacity();
    }

    private void PushValue(int newValue)
    {
        _suppressValuePush = true;
        try { Value = newValue; }
        finally { _suppressValuePush = false; }
    }

    private int ArrowStepFromModifiers()
    {
        ModifierKeys mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        return ctrl switch
        {
            true when shift => ExtraLargeStep,
            true => LargeStep,
            _ => Step,
        };
    }

    private void Adjust(int delta)
    {
        if (string.IsNullOrWhiteSpace(PART_TextBox.Text)
            || !int.TryParse(PART_TextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int current))
        {
            // Empty/inherit: pick a starting endpoint that gives the user a sane first nudge.
            current = delta > 0 ? Minimum : Maximum;
        }

        int next = Math.Clamp(current + delta, Minimum, Maximum);
        PART_TextBox.Text = next.ToString(CultureInfo.InvariantCulture);
        PART_TextBox.CaretIndex = PART_TextBox.Text.Length;
        PushValue(next);
    }

    private void Commit()
    {
        if (string.IsNullOrWhiteSpace(PART_TextBox.Text))
        {
            if (AllowInherit)
            {
                PushValue(InheritValue);
                return;
            }
            // Fall back to current Value (or Minimum if Value is the inherit sentinel).
            int fallback = Value == InheritValue ? Minimum : Value;
            PART_TextBox.Text = fallback.ToString(CultureInfo.InvariantCulture);
            PushValue(fallback);
            return;
        }

        if (!int.TryParse(PART_TextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
        {
            // Garbled text: if inherit is allowed, treat as inherit; otherwise revert to the current Value.
            if (AllowInherit)
            {
                PART_TextBox.Text = string.Empty;
                PushValue(InheritValue);
            }
            else
                PART_TextBox.Text = Value.ToString(CultureInfo.InvariantCulture);

            return;
        }

        int clamped = Math.Clamp(v, Minimum, Maximum);
        if (clamped != v) PART_TextBox.Text = clamped.ToString(CultureInfo.InvariantCulture);
        PushValue(clamped);
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        foreach (char c in e.Text)
            if (!char.IsDigit(c)) { e.Handled = true; return; }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Only steal the wheel when focused; otherwise let it bubble up to a parent ScrollViewer.
        if (!PART_TextBox.IsKeyboardFocused) return;

        // Mirror ArrowStepFromModifiers: Ctrl+Shift -> ExtraLargeStep, Ctrl -> LargeStep, else WheelStep.
        ModifierKeys mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        int magnitude = ctrl switch
        {
            true when shift => ExtraLargeStep,
            true => LargeStep,
            _ => WheelStep,
        };
        Adjust(e.Delta > 0 ? magnitude : -magnitude);
        e.Handled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Adjust(ArrowStepFromModifiers());
                e.Handled = true;
                break;
            case Key.Down:
                Adjust(-ArrowStepFromModifiers());
                e.Handled = true;
                break;
            case Key.Enter:
                Commit();
                e.Handled = true;
                break;
        }
    }

    private void OnTextBoxLostFocus(object sender, RoutedEventArgs e) => Commit();

    private void OnTextBoxFocusChanged(object sender, RoutedEventArgs e) => UpdateSuffixOpacity();

    private void OnSpinUpClick(object sender, RoutedEventArgs e) => Adjust(Step);

    private void OnSpinDownClick(object sender, RoutedEventArgs e) => Adjust(-Step);
}
