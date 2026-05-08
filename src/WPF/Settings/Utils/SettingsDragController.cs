using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace NetworkTrayAppWPF.WPF.Settings.Utils;

/// <summary>
/// Browser-tab-style live drag-to-reorder for an <see cref="ItemsControl"/>.
/// The grabbed card renders as a floating ghost via an <see cref="Adorner"/>,
/// while non-source cards animate via <see cref="TranslateTransform"/>.Y to show the predicted drop position.
/// The backing collection is mutated exactly once on mouse-up via <c>move</c>.
///
/// Generic over the item-key type. The <c>keyFromContainer</c> callback resolves a container's data-context
/// to the same key the gripper's Tag carries;
/// the optional <c>clampTarget</c> hook lets callers refuse drops past a frontier;
/// the optional <c>afterDrop</c> hook persists the new order if needed
/// (callers that defer persistence to a separate Apply step pass null).
///
/// The <c>owner</c> Window is the input-plumbing target for mouse capture and the global
/// move/up/lost-capture/deactivate handlers - it is the host window that contains the list.
/// </summary>
internal sealed class SettingsDragController(
    Window owner,
    ItemsControl list,
    Func<int> count,
    Action<int, int> move,
    Func<FrameworkElement, object?> keyFromContainer,
    Func<int, int>? clampTarget = null,
    Action? afterDrop = null)
{
    private Point _candidateStart;
    private object? _candidateKey;

    private bool _active;
    private int _sourceIndex = -1;
    private int _currentTargetIndex = -1;
    private double _sourceHeight;
    private double _pointerToCardOffsetY;
    private FrameworkElement[] _containers = [];
    private TranslateTransform[] _transforms = [];
    private double[] _cardTops = [];
    private double[] _cardHeights = [];
    private DragGhostAdorner? _ghost;
    private UIElement? _activeCover;

    public void OnGripperMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_active) return;

        if (sender is not FrameworkElement { Tag: { } key }) return;

        _candidateStart = e.GetPosition(owner);
        _candidateKey = key;
    }

    /// <summary>
    /// Drops a pending drag candidate without aborting an in-flight drag.
    /// Called when a double-click promotes a row into rename mode after the first click already armed a candidate
    /// - the user holding-and-moving on the second click would otherwise begin dragging while editing.
    /// </summary>
    public void CancelCandidate() => _candidateKey = null;

    public void OnGripperMouseMove(object sender, MouseEventArgs e)
    {
        if (_active || _candidateKey == null) return;

        if (e.LeftButton != MouseButtonState.Pressed) { _candidateKey = null; return; }

        Point pos = e.GetPosition(owner);
        Vector delta = pos - _candidateStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        Begin(sender as FrameworkElement, e);
    }

    private void Begin(FrameworkElement? gripper, MouseEventArgs e)
    {
        object? key = _candidateKey;
        _candidateKey = null;
        if (key == null || gripper == null) return;

        if (count() < 2) return;

        FrameworkElement? sourceContainer = FindContainerForKey(key);
        if (sourceContainer == null) return;

        int sourceIndex = list.ItemContainerGenerator.IndexFromContainer(sourceContainer);
        if (sourceIndex < 0) return;

        int n = count();
        FrameworkElement[] containers = new FrameworkElement[n];
        TranslateTransform[] transforms = new TranslateTransform[n];
        double[] tops = new double[n];
        double[] heights = new double[n];
        for (int i = 0; i < n; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) return;

            containers[i] = c;
            tops[i] = c.TransformToAncestor(list).Transform(default).Y;
            heights[i] = c.ActualHeight;

            if (c.RenderTransform is TranslateTransform existing)
            {
                existing.Y = 0;
                transforms[i] = existing;
            }
            else
            {
                TranslateTransform tt = new();
                c.RenderTransform = tt;
                transforms[i] = tt;
            }
        }

        _containers = containers;
        _transforms = transforms;
        _cardTops = tops;
        _cardHeights = heights;
        _sourceIndex = sourceIndex;
        _currentTargetIndex = sourceIndex;
        _sourceHeight = heights[sourceIndex];

        // Push the source container behind all others so cards animating UP into the source's slot
        // (originally lower-index siblings)
        // still render above the cover that lives inside the source container's grid.
        // Without this, the cover would occlude any card whose original index < sourceIndex.
        for (int i = 0; i < n; i++)
            System.Windows.Controls.Panel.SetZIndex(containers[i], i == sourceIndex ? 0 : 1);

        FrameworkElement source = containers[sourceIndex];
        Point pointerInSource = e.GetPosition(source);
        _pointerToCardOffsetY = pointerInSource.Y;

        // Find the inner card border (used as the ghost's VisualBrush source so the ghost doesn't capture
        // the cover we'll show in a moment) and the slot cover (a sibling Rectangle inside the source
        // container's Grid; sits above the card content but below other containers in the StackPanel z-order).
        DataTemplate dt = list.ItemTemplate;
        FrameworkElement ghostSource = dt.FindName("DragCardContent", source) as FrameworkElement ?? source;
        _activeCover = dt.FindName("DragSlotCover", source) as UIElement;

        AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list);
        if (layer == null) return;

        double sourceLeftInList = source.TransformToAncestor(list).Transform(default).X;
        _ghost = new DragGhostAdorner(list, ghostSource, sourceLeftInList, tops[sourceIndex]);
        layer.Add(_ghost);

        if (_activeCover != null) _activeCover.Visibility = Visibility.Visible;

        owner.PreviewMouseMove += OnWindowMouseMove;
        owner.PreviewMouseLeftButtonUp += OnWindowMouseUp;
        owner.LostMouseCapture += OnLostCapture;
        owner.Deactivated += OnOwnerDeactivated;
        Mouse.Capture(owner, CaptureMode.SubTree);

        _active = true;
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (!_active) return;

        Point p = e.GetPosition(list);
        UpdateGhost(p);
        UpdateTargetIndex(p);
    }

    private void UpdateGhost(Point cursorInList)
    {
        if (_ghost == null) return;

        double y = cursorInList.Y - _pointerToCardOffsetY;
        double minY = -_sourceHeight * 0.25;
        double maxY = Math.Max(minY, list.ActualHeight - _sourceHeight * 0.75);
        if (y < minY) y = minY;

        if (y > maxY) y = maxY;

        _ghost.SetOffsetY(y);
    }

    private void UpdateTargetIndex(Point cursorInList)
    {
        int n = _containers.Length;
        double cursorY = cursorInList.Y;

        // Walk non-source cards in order; count how many predicted midpoints lie above the cursor.
        // Predicted top = original top, minus sourceHeight if the card was originally below the source.
        int insertion = 0;
        for (int i = 0; i < n; i++)
        {
            if (i == _sourceIndex) continue;

            double predictedTop = _cardTops[i];
            if (i > _sourceIndex) predictedTop -= _sourceHeight;

            double predictedMid = predictedTop + _cardHeights[i] / 2.0;
            if (cursorY > predictedMid)
                insertion++;
            else
                break;
        }

        int target = Math.Max(0, Math.Min(n - 1, insertion));
        if (target == _currentTargetIndex) return;

        _currentTargetIndex = target;
        ApplyShifts(target);
    }

    private void ApplyShifts(int target)
    {
        for (int i = 0; i < _containers.Length; i++)
        {
            if (i == _sourceIndex) continue;

            double shift = 0;
            if (target > _sourceIndex && i > _sourceIndex && i <= target)
                shift = -_sourceHeight;
            else if (target < _sourceIndex && i < _sourceIndex && i >= target) shift = _sourceHeight;

            DoubleAnimation anim = new()
            {
                To = shift,
                Duration = TimeSpan.FromMilliseconds(TimeConstants.SettingsDragAnimationDurationMs),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd
            };
            _transforms[i].BeginAnimation(TranslateTransform.YProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }
    }

    private void OnWindowMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_active) return;

        int target = clampTarget?.Invoke(_currentTargetIndex) ?? _currentTargetIndex;
        int source = _sourceIndex;
        Teardown();
        if (target != source && source >= 0 && target >= 0)
        {
            move(source, target);
            afterDrop?.Invoke();
        }
    }

    private void OnLostCapture(object sender, MouseEventArgs e)
    {
        if (!_active) return;

        Teardown();
    }

    private void OnOwnerDeactivated(object? sender, EventArgs e)
    {
        if (!_active) return;

        Teardown();
    }

    private void Teardown()
    {
        try
        {
            foreach (TranslateTransform t in _transforms)
            {
                t.BeginAnimation(TranslateTransform.YProperty, null);
                t.Y = 0;
            }

            foreach (FrameworkElement t in _containers)
                System.Windows.Controls.Panel.SetZIndex(t, 0);

            if (_ghost != null)
            {
                AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list);
                layer?.Remove(_ghost);
                _ghost = null;
            }

            if (_activeCover != null)
            {
                _activeCover.Visibility = Visibility.Collapsed;
                _activeCover = null;
            }

            owner.PreviewMouseMove -= OnWindowMouseMove;
            owner.PreviewMouseLeftButtonUp -= OnWindowMouseUp;
            owner.LostMouseCapture -= OnLostCapture;
            owner.Deactivated -= OnOwnerDeactivated;

            if (Equals(Mouse.Captured, owner)) owner.ReleaseMouseCapture();
        }
        finally
        {
            _active = false;
            _sourceIndex = -1;
            _currentTargetIndex = -1;
            _containers = [];
            _transforms = [];
            _cardTops = [];
            _cardHeights = [];
        }
    }

    private FrameworkElement? FindContainerForKey(object key)
    {
        for (int i = 0; i < list.Items.Count; i++)
        {
            if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement c
                && Equals(keyFromContainer(c), key))
                return c;
        }
        return null;
    }
}

internal sealed class DragGhostAdorner : Adorner
{
    private readonly Rectangle _rect;
    private readonly Size _size;
    private readonly double _x;
    private double _y;

    public DragGhostAdorner(UIElement adornedElement, FrameworkElement source, double x, double initialY)
        : base(adornedElement)
    {
        _size = new Size(source.ActualWidth, source.ActualHeight);
        _x = x;
        _y = initialY;

        VisualBrush brush = new(source)
        {
            Stretch = Stretch.None,
            AutoLayoutContent = false
        };
        _rect = new Rectangle
        {
            Width = _size.Width,
            Height = _size.Height,
            Fill = brush,
            Opacity = 0.92,
            Effect = new DropShadowEffect
            {
                BlurRadius = 14,
                ShadowDepth = 3,
                Opacity = 0.35
            },
            IsHitTestVisible = false
        };
        AddVisualChild(_rect);
        IsHitTestVisible = false;
    }

    public void SetOffsetY(double y)
    {
        _y = y;
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _rect;

    protected override Size MeasureOverride(Size constraint)
    {
        _rect.Measure(_size);
        return _size;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _rect.Arrange(new Rect(new Point(_x, _y), _size));
        return finalSize;
    }
}
