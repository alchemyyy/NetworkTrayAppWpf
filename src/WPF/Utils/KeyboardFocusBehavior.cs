using System.Windows;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace NetworkTrayAppWPF.WPF.Utils;

/// <summary>
/// Attached behavior that shows focus visuals only on keyboard navigation.
/// Mark elements with ShowKeyboardFocus="True" to include them in keyboard navigation with focus visuals.
/// All elements start with FocusVisualStyle=null; it's applied dynamically only on Tab navigation.
/// </summary>
public static class KeyboardFocusBehavior
{
    private static bool _isKeyboardNavigation;
    private static Style? _focusVisualStyle;

    /// <summary>
    /// The focus visual style to apply on keyboard navigation.
    /// Set this on the Window.
    /// </summary>
    public static readonly DependencyProperty FocusStyleProperty =
        DependencyProperty.RegisterAttached(
            "FocusStyle",
            typeof(Style),
            typeof(KeyboardFocusBehavior),
            new PropertyMetadata(null, OnFocusStyleChanged));

    public static Style? GetFocusStyle(DependencyObject obj) =>
        (Style?)obj.GetValue(FocusStyleProperty);

    public static void SetFocusStyle(DependencyObject obj, Style? value) =>
        obj.SetValue(FocusStyleProperty, value);

    private static void OnFocusStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window) _focusVisualStyle = e.NewValue as Style;
    }

    /// <summary>
    /// Mark an element to show keyboard focus visual when navigated via Tab.
    /// Elements without this property (or set to False) won't show focus visuals.
    /// </summary>
    public static readonly DependencyProperty ShowKeyboardFocusProperty =
        DependencyProperty.RegisterAttached(
            "ShowKeyboardFocus",
            typeof(bool),
            typeof(KeyboardFocusBehavior),
            new PropertyMetadata(false));

    public static bool GetShowKeyboardFocus(DependencyObject obj) =>
        (bool)obj.GetValue(ShowKeyboardFocusProperty);

    public static void SetShowKeyboardFocus(DependencyObject obj, bool value) =>
        obj.SetValue(ShowKeyboardFocusProperty, value);

    /// <summary>
    /// Enables keyboard-only focus visual management on the window.
    /// </summary>
    public static readonly DependencyProperty EnableKeyboardOnlyFocusProperty =
        DependencyProperty.RegisterAttached(
            "EnableKeyboardOnlyFocus",
            typeof(bool),
            typeof(KeyboardFocusBehavior),
            new PropertyMetadata(false, OnEnableKeyboardOnlyFocusChanged));

    public static bool GetEnableKeyboardOnlyFocus(DependencyObject obj) =>
        (bool)obj.GetValue(EnableKeyboardOnlyFocusProperty);

    public static void SetEnableKeyboardOnlyFocus(DependencyObject obj, bool value) =>
        obj.SetValue(EnableKeyboardOnlyFocusProperty, value);

    private static void OnEnableKeyboardOnlyFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;

        if ((bool)e.NewValue)
        {
            window.PreviewKeyDown += Window_PreviewKeyDown;
            window.PreviewMouseDown += Window_PreviewMouseDown;
            window.AddHandler(
                Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(Window_PreviewGotKeyboardFocus), true);
            window.AddHandler(
                Keyboard.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(Window_LostKeyboardFocus), true);
        }
        else
        {
            window.PreviewKeyDown -= Window_PreviewKeyDown;
            window.PreviewMouseDown -= Window_PreviewMouseDown;
            window.RemoveHandler(
                Keyboard.PreviewGotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(Window_PreviewGotKeyboardFocus));
            window.RemoveHandler(
                Keyboard.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(Window_LostKeyboardFocus));
        }
    }

    private static void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        _isKeyboardNavigation = e.Key switch
        {
            Key.Tab or Key.Up or Key.Down or Key.Left or Key.Right or Key.Enter or Key.Space or Key.Escape or Key.Home
                or Key.End or Key.PageUp or Key.PageDown or Key.F4 => true,
            _ => _isKeyboardNavigation
        };
    }

    private static void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e) => _isKeyboardNavigation = false;

    private static void Window_PreviewGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.NewFocus is not FrameworkElement element) return;

        // Set focus visual BEFORE focus completes
        element.FocusVisualStyle = _isKeyboardNavigation
                                   && _focusVisualStyle != null
                                   && GetShowKeyboardFocus(element)
            ? _focusVisualStyle : null;
    }

    private static void Window_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (e.OldFocus is FrameworkElement element) element.FocusVisualStyle = null;
    }
}
