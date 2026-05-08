using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Panel = System.Windows.Controls.Panel;
using Control = System.Windows.Controls.Control;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using RadioButton = System.Windows.Controls.RadioButton;

namespace NetworkTrayAppWPF.WPF.Utils;

/// <summary>
/// Routes arrow / Left / Right / Esc / Enter keys for the Settings window
/// so the whole window is navigable without a mouse:
///   * sidebar Up/Down cycles the nav RadioButtons (auto-checks on move),
///   * Down from the last nav item lands on a marked sidebar-footer button,
///   * sidebar Right jumps focus into the visible section's first focusable,
///   * content Up/Down moves focus to prev/next focusable in document order,
///   * content Left or Esc returns focus to the currently-checked nav item,
///   * closed ComboBox Up/Down navigates (does NOT silently cycle items),
///   * Enter on a closed ComboBox opens it (parity with Alt+Down/F4).
/// TextBoxes and open ComboBoxes are deliberately left alone
/// so caret movement, numeric increment handlers, hotkey-capture boxes, and rename Esc/Enter all keep working.
/// </summary>
public static class SettingsKeyboardNavigation
{
    public static readonly DependencyProperty EnableProperty =
        DependencyProperty.RegisterAttached(
            "Enable",
            typeof(bool),
            typeof(SettingsKeyboardNavigation),
            new PropertyMetadata(false, OnEnableChanged));

    public static bool GetEnable(DependencyObject obj) => (bool)obj.GetValue(EnableProperty);
    public static void SetEnable(DependencyObject obj, bool value) => obj.SetValue(EnableProperty, value);

    public static readonly DependencyProperty IsNavigationSidebarProperty =
        DependencyProperty.RegisterAttached(
            "IsNavigationSidebar",
            typeof(bool),
            typeof(SettingsKeyboardNavigation),
            new PropertyMetadata(false));

    public static bool GetIsNavigationSidebar(DependencyObject obj) =>
        (bool)obj.GetValue(IsNavigationSidebarProperty);
    public static void SetIsNavigationSidebar(DependencyObject obj, bool value) =>
        obj.SetValue(IsNavigationSidebarProperty, value);

    public static readonly DependencyProperty IsContentSectionProperty =
        DependencyProperty.RegisterAttached(
            "IsContentSection",
            typeof(bool),
            typeof(SettingsKeyboardNavigation),
            new PropertyMetadata(false));

    public static bool GetIsContentSection(DependencyObject obj) =>
        (bool)obj.GetValue(IsContentSectionProperty);
    public static void SetIsContentSection(DependencyObject obj, bool value) =>
        obj.SetValue(IsContentSectionProperty, value);

    public static readonly DependencyProperty IsSidebarFooterProperty =
        DependencyProperty.RegisterAttached(
            "IsSidebarFooter",
            typeof(bool),
            typeof(SettingsKeyboardNavigation),
            new PropertyMetadata(false));

    public static bool GetIsSidebarFooter(DependencyObject obj) =>
        (bool)obj.GetValue(IsSidebarFooterProperty);
    public static void SetIsSidebarFooter(DependencyObject obj, bool value) =>
        obj.SetValue(IsSidebarFooterProperty, value);

    // Opt-out marker for controls that own their own arrow-key handling
    // (e.g. the curve editor's per-node nudge / tab-cycle).
    // Window-level arrow routing is suppressed whenever focus is on or inside an element with this flag set
    // so PreviewKeyDown tunneling reaches the inner control instead of being short-circuited at the window.
    public static readonly DependencyProperty IsKeyboardCaptureProperty =
        DependencyProperty.RegisterAttached(
            "IsKeyboardCapture",
            typeof(bool),
            typeof(SettingsKeyboardNavigation),
            new PropertyMetadata(false));

    public static bool GetIsKeyboardCapture(DependencyObject obj) =>
        (bool)obj.GetValue(IsKeyboardCaptureProperty);
    public static void SetIsKeyboardCapture(DependencyObject obj, bool value) =>
        obj.SetValue(IsKeyboardCaptureProperty, value);

    private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;

        if ((bool)e.NewValue)
            window.PreviewKeyDown += Window_PreviewKeyDown;
        else
            window.PreviewKeyDown -= Window_PreviewKeyDown;
    }

    private static void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not Window window) return;

        if (Keyboard.FocusedElement is not DependencyObject focused) return;

        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
                HandleUpDown(window, focused, e);
                return;
            case Key.Right:
                HandleRight(window, focused, e);
                return;
            case Key.Left:
                HandleLeft(window, focused, e);
                return;
            case Key.Escape:
                HandleEscape(window, focused, e);
                return;
            case Key.Enter:
                HandleEnter(focused, e);
                return;
        }
    }

    private static void HandleUpDown(Window window, DependencyObject focused, KeyEventArgs e)
    {
        if (FindAncestorWith(focused, IsNavigationSidebarProperty) is Panel sidebar)
        {
            // Modifier chords (Ctrl+Up/Down for reorder, etc.) belong to per-list handlers, but only inside the sidebar
            // - inner controls like the curve editor want their own Ctrl+arrow semantics.
            if (Keyboard.Modifiers != ModifierKeys.None) return;

            CycleSidebar(window, sidebar, focused, forward: e.Key == Key.Down);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None) return;

        // Inner controls that own their arrow keys (e.g. the curve editor) opt out via IsKeyboardCapture;
        // without the early return the window-level row navigator would steal Up/Down
        // before PreviewKeyDown tunnels into them.
        if (FindAncestorWith(focused, IsKeyboardCaptureProperty) != null) return;

        // TextBoxes own their arrow keys (numeric spinners, hotkey capture, plain caret).
        // Open ComboBoxes own their dropdown navigation.
        if (focused is TextBox) return;

        if (focused is ComboBox { IsDropDownOpen: true }) return;

        if (FindAncestor<ComboBox>(focused) is { IsDropDownOpen: true }) return;

        if (FindAncestorWith(focused, IsContentSectionProperty) != null && focused is UIElement uie)
        {
            FocusNavigationDirection dir = e.Key == Key.Down
                ? FocusNavigationDirection.Next
                : FocusNavigationDirection.Previous;
            uie.MoveFocus(new TraversalRequest(dir));
            e.Handled = true;
        }
    }

    private static void CycleSidebar(Window window, Panel sidebar, DependencyObject focused, bool forward)
    {
        List<FrameworkElement> stops = CollectSidebarStops(window, sidebar);
        if (stops.Count == 0) return;

        // Find current focused stop (or its ancestor) in the chain.
        int currentIndex = -1;
        for (int i = 0; i < stops.Count; i++)
        {
            if (stops[i] == focused || IsDescendant(focused, stops[i]))
            {
                currentIndex = i;
                break;
            }
        }
        if (currentIndex < 0) currentIndex = 0;

        int target = currentIndex + (forward ? 1 : -1);
        if (target < 0 || target >= stops.Count) return;
        // No wrap.

        FrameworkElement next = stops[target];
        if (next is RadioButton rb)
        {
            // Auto-check fires NavItem_Checked which swaps the visible section.
            if (rb.IsChecked != true) rb.IsChecked = true;
        }
        next.Focus();
    }

    /// <summary>
    /// Sidebar focus chain in document order:
    /// every focusable RadioButton inside the IsNavigationSidebar panel,
    /// followed by the IsSidebarFooter element (if any).
    /// </summary>
    private static List<FrameworkElement> CollectSidebarStops(Window window, Panel sidebar)
    {
        List<FrameworkElement> stops = [];
        foreach (object? child in sidebar.Children)
        {
            if (child is RadioButton { Focusable: true, IsEnabled: true, Visibility: Visibility.Visible } rb)
                stops.Add(rb);
        }

        FrameworkElement? footer = FindFooterDescendant(window);
        if (footer != null) stops.Add(footer);

        return stops;
    }

    private static FrameworkElement? FindFooterDescendant(DependencyObject root)
    {
        if (root is FrameworkElement fe
            && GetIsSidebarFooter(fe)
            && fe is { Focusable: true, IsEnabled: true, Visibility: Visibility.Visible })
            return fe;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            FrameworkElement? found = FindFooterDescendant(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private static void HandleRight(Window window, DependencyObject focused, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        if (FindAncestorWith(focused, IsNavigationSidebarProperty) == null
            && !(focused is FrameworkElement fe && GetIsSidebarFooter(fe)))
        {
            // Right is only meaningful from sidebar; everything else (TextBox caret, etc.) wins.
            return;
        }

        FrameworkElement? section = FindVisibleContentSection(window);
        if (section == null) return;

        FrameworkElement? first = FindFirstFocusableDescendant(section);
        if (first == null) return;

        first.Focus();
        e.Handled = true;
    }

    private static void HandleLeft(Window window, DependencyObject focused, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None) return;

        // Inner-control opt-out so e.g. the curve editor can treat Left as "nudge time backwards"
        // instead of being yanked back to the sidebar.
        if (FindAncestorWith(focused, IsKeyboardCaptureProperty) != null) return;

        // TextBoxes own Left for caret movement.
        if (focused is TextBox) return;

        if (focused is ComboBox { IsDropDownOpen: true }) return;

        if (FindAncestor<ComboBox>(focused) is { IsDropDownOpen: true }) return;

        if (FindAncestorWith(focused, IsContentSectionProperty) != null)
        {
            FocusCheckedNavItem(window);
            e.Handled = true;
        }
    }

    private static void HandleEscape(Window window, DependencyObject focused, KeyEventArgs e)
    {
        // TextBoxes get Esc for their own purposes (rename revert, hotkey cancel, etc.).
        if (focused is TextBox) return;

        if (focused is ComboBox { IsDropDownOpen: true }) return;

        if (FindAncestor<ComboBox>(focused) is { IsDropDownOpen: true }) return;

        if (FindAncestorWith(focused, IsContentSectionProperty) != null)
        {
            FocusCheckedNavItem(window);
            e.Handled = true;
        }
    }

    private static void HandleEnter(DependencyObject focused, KeyEventArgs e)
    {
        // Open closed ComboBoxes - parity with Alt+Down / F4 muscle memory.
        if (focused is ComboBox { IsDropDownOpen: false, IsEnabled: true } cb)
        {
            cb.IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private static void FocusCheckedNavItem(Window window)
    {
        if (FindFirstWith(window, IsNavigationSidebarProperty) is not Panel sidebar) return;

        foreach (object? child in sidebar.Children)
        {
            if (child is RadioButton { IsChecked: true, Focusable: true, IsEnabled: true } rb)
            {
                rb.Focus();
                return;
            }
        }
    }

    // Sections were StackPanels prior to the per-section UserControl extraction;
    // post-extraction the marker can land on any FrameworkElement (UserControl root),
    // so the type guard is loosened accordingly.
    private static FrameworkElement? FindVisibleContentSection(DependencyObject root)
    {
        if (root is FrameworkElement fe && GetIsContentSection(fe) && fe.Visibility == Visibility.Visible) return fe;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            FrameworkElement? found = FindVisibleContentSection(VisualTreeHelper.GetChild(root, i));
            if (found != null) return found;
        }
        return null;
    }

    private static FrameworkElement? FindFirstFocusableDescendant(DependencyObject root)
    {
        if (root is FrameworkElement fe && IsFocusableLeaf(fe)) return fe;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            // Skip subtrees that are hidden/disabled.
            if (child is UIElement ui && (ui.Visibility != Visibility.Visible || !ui.IsEnabled)) continue;

            FrameworkElement? found = FindFirstFocusableDescendant(child);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsFocusableLeaf(FrameworkElement fe)
    {
        if (!fe.Focusable || !fe.IsEnabled || fe.Visibility != Visibility.Visible) return false;

        // Honour IsTabStop on Control-derived elements (Borders/Panels never expose it).
        return fe is not Control { IsTabStop: false };
    }

    private static DependencyObject? FindAncestorWith(DependencyObject? from, DependencyProperty marker)
    {
        DependencyObject? node = from;
        while (node != null)
        {
            if ((bool)node.GetValue(marker)) return node;

            node = GetParent(node);
        }
        return null;
    }

    private static DependencyObject? FindFirstWith(DependencyObject root, DependencyProperty marker)
    {
        if ((bool)root.GetValue(marker)) return root;

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject? found = FindFirstWith(VisualTreeHelper.GetChild(root, i), marker);
            if (found != null) return found;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject? from) where T : DependencyObject
    {
        DependencyObject? node = from;
        while (node != null)
        {
            if (node is T match) return match;

            node = GetParent(node);
        }
        return null;
    }

    private static bool IsDescendant(DependencyObject? candidate, DependencyObject ancestor)
    {
        DependencyObject? node = candidate;
        while (node != null)
        {
            if (node == ancestor) return true;

            node = GetParent(node);
        }
        return false;
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        // Visual parent works for visuals; logical parent covers e.g. ContentElements.
        if (node is Visual)
        {
            DependencyObject? v = VisualTreeHelper.GetParent(node);
            if (v != null) return v;
        }
        return LogicalTreeHelper.GetParent(node);
    }
}
