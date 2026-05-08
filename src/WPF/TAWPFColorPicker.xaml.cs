using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using NetworkTrayAppWPF.Interop;
using NetworkTrayAppWPF.Models;
using NetworkTrayAppWPF.Services;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace NetworkTrayAppWPF.WPF;

/// <summary>
/// Modeless color picker window with paired ARGB / RGBA hex textboxes, an HSV saturation/value
/// free-pick plane, vertical hue and alpha gradient sliders, vertical R/G/B channel sliders,
/// and Default / Reset buttons. All five sliders run on the same Slider chassis (see
/// <see cref="ChannelSliderStyle"/> for R/G/B and <see cref="HueAlphaSliderStyle"/> for hue/alpha)
/// so a single set of channel-slider mouse / wheel / keyboard handlers covers every input path.
///
/// Shown via <see cref="Window.Show"/> (not <c>ShowDialog</c>) so the user can keep
/// interacting with the owning settings window while picking a color and watch the
/// edits land where they're applied.
///
/// Live-apply: every successful edit (typed, slider, free-pick, Reset, or Default) feeds an
/// <see cref="AsyncThrottler{TKey}"/> queue; the throttler collapses a flurry of intermediate
/// values to the latest one and dispatches <see cref="ColorChanged"/> back on the UI thread,
/// so callers can mutate their preview state (e.g. <c>NullableThemeColor.TemporaryLightColor</c>)
/// without the picker's own UI being gated on whatever AppSettings.Changed listeners do.
///
/// Commit contract: there is no explicit Apply step. The picker exposes <see cref="CurrentColor"/>
/// and <see cref="IsDirty"/> so the caller can persist the final edit when its own Closed
/// handler runs. Default loads the supplied factory color (the theme's fallback for the swatch);
/// Reset reverts to the baseline (the color the picker opened on). Neither closes the window.
/// </summary>
public partial class TAWPFColorPicker : Window
{
    // Single throttler slot - one picker only edits one color, so we don't need per-key partitioning.
    // 50ms cooldown caps ColorChanged fanout (AppSettings.Changed -> brush rebuild + swatch refresh)
    // at ~20Hz. A 0ms cooldown was tried first and felt laggier than synchronous
    // because latest-wins only collapses queued snapshots; the slot driver still runs every payload
    // back-to-back and just adds dispatcher marshaling overhead on top of the original fanout cost.
    // 50ms is the rate slider/free-pick drags actually need to feel smooth without saturating the UI.
    private const string ColorChangedThrottlerKey = "color";
    private readonly AsyncThrottler<string> _colorChangedThrottler = new(TimeConstants.ColorPickerChangeCooldownMs, StringComparer.Ordinal);

    // Set in OnPickerClosed so any payload that landed on the dispatcher AFTER the window's Closed
    // handlers ran (which clear NullableThemeColor.Temporary*) doesn't re-publish a stale color and
    // resurrect the temporary override. UI thread only - no volatile needed.
    private bool _closed;

    private readonly bool _hasAlpha;
    private Color _currentColor;

    /// <summary>
    /// Resolved at construction so the picker can mirror SettingsWindow's rounded-corner toggle
    /// (and unsubscribe cleanly on close). Null when AppSettings isn't in the Application properties bag
    /// - the picker still works, it just falls back to its default unrounded chrome.
    /// </summary>
    private readonly AppSettings? _settings;

    /// <summary>
    /// The picker's session baseline (the color the picker opened on). Reset reverts to this.
    /// Fixed for the lifetime of the picker - the new commit-on-close model never advances it.
    /// </summary>
    private Color _baseline;

    /// <summary>
    /// The factory-default color the Default button reverts to. Supplied by the caller so
    /// the picker doesn't need to know what "default" means for any particular swatch (theme
    /// fallback, hardcoded constant, etc).
    /// </summary>
    private readonly Color _defaultColor;

    // Reentry guards: setting one textbox's Text from code triggers TextChanged,
    // which would otherwise re-parse and overwrite the textbox the user is typing in
    // (and bounce caret position). One flag per box so the OTHER box still updates from a code-driven SetColor.
    private bool _suppressArgb;
    private bool _suppressRgba;

    // Suppresses ChannelSlider_ValueChanged while a code-driven SyncSlidersFromColor is in progress,
    // so seeding sliders from the current color (constructor or hex-textbox edit) doesn't bounce
    // back into ApplyColor and re-snap the slider to its rounded byte (which would clobber the
    // sub-integer precision of an in-flight drag).
    private bool _suppressSliderToColor;

    // Slider currently being click-track-dragged. WPF's native thumb-drag captures fine on its own;
    // this field tracks the OTHER path - a click on the visible track (not the thumb) - so PreviewMouseMove
    // knows which slider to keep updating until the button releases.
    private Slider? _draggingSlider;

    // Last non-gray hue, in degrees [0, 360). HSV is undefined when saturation is 0 (any pure gray),
    // so an RGB->HSV round-trip on a gray would collapse hue back to 0 (red) and flip the free-pick
    // background. Holding the last meaningful hue here lets the user move through the gray axis
    // (drag down the value gradient, type a gray hex) without losing the picker's hue context.
    private double _freePickHue;

    // True between MouseLeftButtonDown and MouseLeftButtonUp on the free-pick area; PreviewMouseMove
    // uses it to decide whether to keep updating the color from the cursor. Captured-mouse pattern
    // mirrors the channel-slider drag - gives us the drag through cursor leaves and re-enters.
    private bool _freePickDragging;

    // Persistent gradient and per-thumb brushes the picker mutates as the color changes, instead of
    // allocating fresh brushes on every ApplyColor call. The alpha gradient's two stops are rgb-tinted
    // (transparent-rgb at top, opaque-rgb at bottom) so the bar visualises what alpha would look like
    // against the current color. Foreground brushes feed the hue / alpha thumbs via the
    // HueAlphaSliderStyle template chain (Track.Thumb Background = Foreground).
    private readonly LinearGradientBrush _alphaGradient = new()
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(0, 1),
    };
    private readonly SolidColorBrush _hueThumbBrush = new(Colors.Red);
    private readonly SolidColorBrush _alphaThumbBrush = new(Colors.Black);

    // Alpha thumb's border brush; mutated to a luminance-inverted grayscale of the current rgb so
    // the rectangle stroke stays visible against any fill (white border on dark fills, black border
    // on light fills, smooth grays in between). Hue thumb keeps the themed default since its fill
    // never approaches the neutral axis where contrast against ThemeForeground would suffer.
    private readonly SolidColorBrush _alphaThumbBorderBrush = new(Colors.White);

    /// <summary>
    /// Fires on every edit (typed, slider, free-pick, Reset, or Default) that successfully
    /// resolves to a color. Used by the caller for live-preview before the picker closes.
    /// </summary>
    public event EventHandler<Color>? ColorChanged;

    /// <summary>The latest edited color. Read by the caller's Closed handler to commit.</summary>
    public Color CurrentColor => _currentColor;

    /// <summary>True when the user has edited away from the session baseline.
    /// Caller checks this on close to decide whether to persist <see cref="CurrentColor"/>.</summary>
    public bool IsDirty => _currentColor != _baseline;

    /// <param name="title">Title shown in the window's titlebar.</param>
    /// <param name="hasAlpha">When false, the alpha channel is locked at 0xFF
    /// regardless of what the user types into the alpha bytes.</param>
    /// <param name="startingColor">Initial color and the Reset baseline.
    /// When null, defaults to opaque black.</param>
    /// <param name="defaultColor">The factory color the Default button loads. When null,
    /// falls back to <paramref name="startingColor"/> so Default behaves like Reset.</param>
    public TAWPFColorPicker(string title, bool hasAlpha, Color? startingColor = null, Color? defaultColor = null)
    {
        InitializeComponent();

        Title = title;
        TitleText.Text = title;
        _hasAlpha = hasAlpha;

        Color seed = startingColor ?? Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
        if (!hasAlpha) seed = Color.FromArgb(0xFF, seed.R, seed.G, seed.B);
        _currentColor = seed;
        _baseline = seed;

        Color factory = defaultColor ?? seed;
        if (!hasAlpha) factory = Color.FromArgb(0xFF, factory.R, factory.G, factory.B);
        _defaultColor = factory;

        // Build the alpha gradient once and hand it to the slider as its Background. The two stops
        // are reused on every UpdateAlphaGradient call - mutating Color is cheaper than swapping the
        // entire brush, and avoids brush-frozen surprises.
        _alphaGradient.GradientStops.Add(new GradientStop(Colors.Transparent, 0.0));
        _alphaGradient.GradientStops.Add(new GradientStop(Colors.Black, 1.0));
        AlphaSlider.Background = _alphaGradient;

        // Slider.Foreground is the source of the thumb fill via the HueAlphaSliderStyle template;
        // assign the persistent brushes so update calls only need to touch their .Color.
        // Slider.BorderBrush is the source of the thumb stroke (separate from the slider's outer
        // chrome border, which uses ThemeBorder directly) - alpha gets the dynamic luminance brush
        // so the grip outline tracks the fill; hue keeps the style's themed default.
        HueSlider.Foreground = _hueThumbBrush;
        AlphaSlider.Foreground = _alphaThumbBrush;
        AlphaSlider.BorderBrush = _alphaThumbBorderBrush;

        // Lock the alpha column out of interaction when the caller didn't opt in. The IsEnabled
        // trigger in HueAlphaSliderStyle dims it to 0.5 opacity so it's clearly read-only.
        AlphaSlider.IsEnabled = hasAlpha;

        WireChannelSliderEvents();

        // _freePickHue is the source of truth for the hue slider value, the hue thumb fill, and the
        // free-pick area's hue background; seed it from the starting color so a saturated startingColor
        // (e.g. opening the picker on an existing green theme) doesn't open with the hue slider stuck
        // at 0/red just because the field default is 0. Pure-gray seeds keep the default.
        RefreshHueFromColor();

        SyncTextBoxes();
        SyncSlidersFromColor();
        SyncValueLabelsFromColor();
        UpdateAlphaGradient();
        UpdateChannelSliderThumbs();
        UpdatePreview();

        // Pick up the EnableRoundedCorners toggle from the global settings so the picker chrome
        // matches the rest of the app's surfaces. Apply now (RootBorder + WindowChrome) - DWM
        // requires the HWND so it runs in OnSourceInitialized.
        _settings = AppServices.Settings;
        ApplyOuterCornerRadius();
        if (_settings != null) _settings.Changed += OnAppSettingsChanged;

        Closed += OnPickerClosed;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDwmRoundedCorners();
    }

    private void OnAppSettingsChanged()
    {
        // BeginInvoke matches SettingsWindow's idiom - the Changed event can fire from any thread
        // (including the color-callback chain), and DWM/WindowChrome mutations need the UI thread.
        // UpdateChannelSliderThumbs piggy-backs so a theme switch (which changes ThemeBackground)
        // re-evaluates the alpha thumb border against the new backdrop without waiting for the
        // user's next ApplyColor; a small wasted recompute on non-theme settings is acceptable.
        Dispatcher.BeginInvoke(() =>
        {
            ApplyOuterCornerRadius();
            UpdateChannelSliderThumbs();
        });
    }

    private void OnPickerClosed(object? sender, EventArgs e)
    {
        // Wired in the constructor before any consumer-supplied Closed handler, so this runs first.
        // Setting _closed BEFORE the throttler is disposed means any payload already mid-flight on
        // the dispatcher queue (and any future RunAsync the disposer might race against) sees the
        // flag and bails before re-firing ColorChanged into a teardown-in-progress consumer.
        _closed = true;
        _colorChangedThrottler.Dispose();
        if (_settings != null) _settings.Changed -= OnAppSettingsChanged;
    }

    /// <summary>
    /// Mirrors <see cref="SettingsWindow.ApplyOuterCornerRadius"/>: kept imperative because WindowChrome
    /// is a bare DependencyObject and DynamicResource lookups against it don't reliably propagate.
    /// Re-applies the DWM corner preference too, which overrides WindowChrome on Win11.
    /// </summary>
    private void ApplyOuterCornerRadius()
    {
        double r = _settings?.EnableRoundedCorners == true ? 8 : 0;
        CornerRadius radius = new(r);

        System.Windows.Shell.WindowChrome? chrome =
            System.Windows.Shell.WindowChrome.GetWindowChrome(this);
        if (chrome != null) chrome.CornerRadius = radius;

        RootBorder.CornerRadius = radius;
        ApplyDwmRoundedCorners();
    }

    private void ApplyDwmRoundedCorners()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int value = _settings?.EnableRoundedCorners == true
                ? DWMAPI.DWMWCP_ROUND
                : DWMAPI.DWMWCP_DONOTROUND;
            DWMAPI.DwmSetWindowAttribute(hwnd, DWMAPI.DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
        }
        catch
        {
            // DWM call may fail on older Windows; non-fatal.
        }
    }

    private void Default_Click(object sender, RoutedEventArgs e)
    {
        // Force a full re-sync even when Default is a no-op vs the current value, so the
        // caller's Temporary slot is guaranteed to track the factory color (and the dirty
        // flag flips correctly when the user opened on a non-default saved value).
        ApplyColor(_defaultColor, force: true);
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        // Force a full re-sync (both textboxes + sliders + preview + ColorChanged) even if Reset is a no-op
        // versus the current value, so the caller's Temporary slot is guaranteed to track the baseline.
        ApplyColor(_baseline, force: true);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ArgbBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressArgb) return;
        if (!TryParseHex(ArgbBox.Text, argbOrder: true, out Color parsed)) return;

        if (!_hasAlpha) parsed = Color.FromArgb(0xFF, parsed.R, parsed.G, parsed.B);
        ApplyColor(parsed, sourceArgbBox: true);
    }

    private void RgbaBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressRgba) return;
        if (!TryParseHex(RgbaBox.Text, argbOrder: false, out Color parsed)) return;

        if (!_hasAlpha) parsed = Color.FromArgb(0xFF, parsed.R, parsed.G, parsed.B);
        ApplyColor(parsed, sourceRgbaBox: true);
    }

    private void ApplyColor(
        Color color,
        bool sourceArgbBox = false,
        bool sourceRgbaBox = false,
        Slider? sourceSlider = null,
        bool sourceFreePick = false,
        bool force = false)
    {
        if (!force && color == _currentColor) return;

        _currentColor = color;

        // Skip writing the textbox the user is typing in to keep their caret position
        // and avoid clobbering in-progress text. The other box always re-syncs from the new color.
        if (!sourceArgbBox) WriteArgbBox();
        if (!sourceRgbaBox) WriteRgbaBox();

        // Refresh the hue cache + free-pick visuals from the new color so downstream consumers (hue
        // slider sync, hue thumb fill) see a coherent hue. Skipped entirely on a free-pick drag
        // (sourceFreePick) because the indicator was already placed under the cursor and _freePickHue
        // is held constant on that path - rerunning RGB->HSV would drift it via rounding. Skipped on
        // a hue-slider drag (refreshHueFromColor=false) for the same precision-preservation reason.
        if (!sourceFreePick) UpdateFreePickFromColor(refreshHueFromColor: sourceSlider != HueSlider);

        // Sync every non-dragging slider so the five thumbs stay coherent across input sources. The
        // dragging slider is held at its current float position to preserve sub-byte drag precision.
        SyncSlidersFromColor(except: sourceSlider);

        UpdateAlphaGradient();
        UpdateChannelSliderThumbs();
        SyncValueLabelsFromColor();
        UpdatePreview();
        EnqueueColorChangedNotification();
    }

    private void RefreshHueFromColor()
    {
        (double hue, double sat, double _) = RGBToHSV(_currentColor.R, _currentColor.G, _currentColor.B);
        if (sat > 0) _freePickHue = hue;
    }

    /// <summary>
    /// Pushes the current color into the latest-pending-wins throttler so a flurry of edits
    /// (slider drag, free-pick drag, hex-typing) collapses to a single ColorChanged on the
    /// consumer side. The picker's OWN visuals (textboxes, sliders, free-pick indicator,
    /// preview swatch) already updated synchronously above this call - only the outbound
    /// notification is decoupled, so AppSettings.Changed fanout can't backpressure the
    /// picker's input rate.
    /// </summary>
    private void EnqueueColorChangedNotification()
    {
        // Capture the snapshot now (UI thread). Latest-pending-wins guarantees that when the
        // payload finally runs, it carries the freshest color the user produced before the
        // throttler picked it up - intermediate snapshots get GC'd along with their replaced payloads.
        Color snapshot = _currentColor;
        _ = _colorChangedThrottler.RunAsync(ColorChangedThrottlerKey, async _ =>
        {
            // Throttler runs payloads on the thread pool; the ColorChanged consumer (ThemePage)
            // touches WPF brushes and AppSettings, so dispatch back to the picker's UI thread.
            // Awaiting InvokeAsync means the throttler treats the dispatcher work as part of the
            // payload, so the next queued snapshot waits until this consumer cycle completes.
            await Dispatcher.InvokeAsync(() =>
            {
                if (_closed) return;
                ColorChanged?.Invoke(this, snapshot);
            });
        });
    }

    private void SyncTextBoxes()
    {
        WriteArgbBox();
        WriteRgbaBox();
    }

    private void WriteArgbBox()
    {
        _suppressArgb = true;
        try { ArgbBox.Text = FormatArgb(_currentColor); }
        finally { _suppressArgb = false; }
    }

    private void WriteRgbaBox()
    {
        _suppressRgba = true;
        try { RgbaBox.Text = FormatRgba(_currentColor); }
        finally { _suppressRgba = false; }
    }

    // Paint the free-pick area's node indicator with the current color so it reads as the live
    // preview of the edit, mirroring how curve-editor nodes are filled with their series color.
    private void UpdatePreview() => FreePickIndicator.Fill = new SolidColorBrush(_currentColor);

    private static string FormatArgb(Color color) => $"{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    private static string FormatRgba(Color color) => $"{color.R:X2}{color.G:X2}{color.B:X2}{color.A:X2}";

    private IEnumerable<Slider> EnumerateChannelSliders()
    {
        yield return RSlider;
        yield return GSlider;
        yield return BSlider;
        yield return HueSlider;
        yield return AlphaSlider;
    }

    private void WireChannelSliderEvents()
    {
        foreach (Slider slider in EnumerateChannelSliders())
        {
            slider.ValueChanged += ChannelSlider_ValueChanged;
            slider.PreviewMouseLeftButtonDown += ChannelSlider_PreviewMouseLeftButtonDown;
            slider.PreviewMouseMove += ChannelSlider_PreviewMouseMove;
            slider.PreviewMouseLeftButtonUp += ChannelSlider_PreviewMouseLeftButtonUp;
            slider.PreviewMouseWheel += ChannelSlider_PreviewMouseWheel;
        }
    }

    private void SyncSlidersFromColor(Slider? except = null)
    {
        // The dragging slider is excluded so its sub-byte float position isn't snapped back to the
        // rounded byte the rest of the picker is tracking - same precision-preservation rationale as
        // the original RGB-only path. Non-dragging sliders sync to the new color so external sources
        // (hex box, free-pick drag, sibling slider) keep all five thumbs coherent. Hue's value comes
        // from _freePickHue (already refreshed by ApplyColor before this call) so a pure-gray rgb
        // doesn't collapse the hue slider back to 0.
        _suppressSliderToColor = true;
        try
        {
            if (except != RSlider) RSlider.Value = _currentColor.R;
            if (except != GSlider) GSlider.Value = _currentColor.G;
            if (except != BSlider) BSlider.Value = _currentColor.B;
            if (except != HueSlider) HueSlider.Value = _freePickHue;
            if (except != AlphaSlider) AlphaSlider.Value = _currentColor.A;
        }
        finally { _suppressSliderToColor = false; }
    }

    private void SyncValueLabelsFromColor()
    {
        RValueLabel.Text = _currentColor.R.ToString();
        GValueLabel.Text = _currentColor.G.ToString();
        BValueLabel.Text = _currentColor.B.ToString();
    }

    private void ChannelSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressSliderToColor) return;
        if (sender is not Slider slider) return;

        // Hue runs on a 0-360 scale and recomposes RGB through the HSV path - it's not a byte channel,
        // so it gets handled separately from R/G/B/A. Adopt the slider value into _freePickHue first so
        // a pure-gray current color (sat=0) still shifts the free-pick hue layer / thumb fill even
        // though the resulting RGB is unchanged - lets the user dial in a hue before adding chroma.
        string? tag = slider.Tag as string;
        Color next;
        if (tag == "H")
        {
            // Hue drives visuals beyond just RGB - the free-pick hue background and the hue thumb
            // fill both move with _freePickHue even when the resulting rgb is unchanged (any sat=0
            // gray short-circuits HSVToRGB back to the same gray). Force the apply so those
            // hue-only visuals refresh; ApplyColor's normal equality early-exit would otherwise
            // freeze the picker mid-drag whenever the user is dialling hue against a gray base.
            _freePickHue = Math.Clamp(slider.Value, 0, 360);
            (double _, double sat, double val) = RGBToHSV(_currentColor.R, _currentColor.G, _currentColor.B);
            Color hueRgb = HSVToRGB(_freePickHue, sat, val);
            next = Color.FromArgb(_currentColor.A, hueRgb.R, hueRgb.G, hueRgb.B);
            ApplyColor(next, sourceSlider: slider, force: true);
            return;
        }

        // Slider keeps a float value for smooth interpolation while the user drags; the color byte
        // it lands on is the rounded result. A 128.4 -> 128.6 micro-drag rounds to the same byte
        // and ApplyColor's equality early-exit makes it a no-op.
        byte channelValue = (byte)Math.Round(Math.Clamp(slider.Value, 0, 255));
        next = tag switch
        {
            "R" => Color.FromArgb(_currentColor.A, channelValue, _currentColor.G, _currentColor.B),
            "G" => Color.FromArgb(_currentColor.A, _currentColor.R, channelValue, _currentColor.B),
            "B" => Color.FromArgb(_currentColor.A, _currentColor.R, _currentColor.G, channelValue),
            "A" => Color.FromArgb(channelValue, _currentColor.R, _currentColor.G, _currentColor.B),
            _ => _currentColor,
        };

        ApplyColor(next, sourceSlider: slider);
    }

    private void ChannelSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // WPF's native Track handles thumb-on-the-cursor dragging fine. The case we have to handle
        // ourselves is a click on the visible track (away from the thumb): we want the thumb to
        // jump to that point AND keep tracking the cursor through subsequent drag - the symptom
        // the user reported was "position updates once" because nothing was capturing the mouse.
        if (sender is not Slider slider) return;

        Track? track = FindVisualChild<Track>(slider);
        if (track?.Thumb == null) return;

        Rect thumbBounds = new(
            track.Thumb.TranslatePoint(new Point(0, 0), slider),
            new Size(track.Thumb.ActualWidth, track.Thumb.ActualHeight));
        if (thumbBounds.Contains(e.GetPosition(slider))) return;

        _draggingSlider = slider;
        slider.CaptureMouse();
        UpdateSliderValueFromMousePosition(slider, track, e.GetPosition(slider));
        e.Handled = true;
    }

    private void ChannelSlider_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not Slider slider || _draggingSlider != slider) return;

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            Track? track = FindVisualChild<Track>(slider);
            if (track != null) UpdateSliderValueFromMousePosition(slider, track, e.GetPosition(slider));
        }
        else
            StopDragging(slider);
    }

    private void ChannelSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Slider slider || _draggingSlider != slider) return;
        StopDragging(slider);
    }

    private void StopDragging(Slider slider)
    {
        _draggingSlider = null;
        slider.ReleaseMouseCapture();
    }

    private void ChannelSlider_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not Slider slider) return;

        // One notch = SmallChange (1 byte), Ctrl held = LargeChange (16 bytes). Matches the keyboard
        // arrow / Page Up-Down convention so wheel feels like a hands-on-mouse equivalent of arrows.
        bool coarse = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        double step = coarse ? slider.LargeChange : slider.SmallChange;
        double notches = e.Delta / 120.0;
        slider.Value = Math.Clamp(slider.Value + notches * step, slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private static void UpdateSliderValueFromMousePosition(Slider slider, Track track, Point position)
    {
        // Vertical slider with default IsDirectionReversed=false maps the top of the usable track to
        // Maximum and the bottom to Minimum (R/G/B/Hue follow this). The alpha slider sets
        // IsDirectionReversed=true to put 255 at the bottom (matches the gradient), so the value
        // mapping flips with it. Half a thumb is unreachable on each end - the thumb's center can't
        // extend past the track edges - so strip that out so the cursor's Y matches the value at the
        // thumb's center.
        double thumbHeight = track.Thumb?.ActualHeight ?? 0;
        double trackStart = thumbHeight / 2;
        double trackEnd = slider.ActualHeight - thumbHeight / 2;
        double trackLength = trackEnd - trackStart;
        if (trackLength <= 0) return;

        double normalized = Math.Clamp((position.Y - trackStart) / trackLength, 0, 1);
        double range = slider.Maximum - slider.Minimum;
        slider.Value = slider.IsDirectionReversed
            ? slider.Minimum + normalized * range
            : slider.Maximum - normalized * range;
    }

    private void FreePickArea_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Layout-driven entry point. The constructor's UpdateFreePickFromColor call is a no-op
        // because the area has zero size before measure/arrange; this fires once layout settles
        // (and again on any later resize) so the indicator and hue layer reflect the seed color.
        UpdateFreePickFromColor();
    }

    private void FreePickArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _freePickDragging = true;
        FreePickArea.CaptureMouse();
        UpdateColorFromFreePickPosition(e.GetPosition(FreePickCanvas));
        e.Handled = true;
    }

    private void FreePickArea_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_freePickDragging) return;

        if (e.LeftButton == MouseButtonState.Pressed)
            UpdateColorFromFreePickPosition(e.GetPosition(FreePickCanvas));
        else
            StopFreePickDragging();
    }

    private void FreePickArea_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_freePickDragging) return;
        StopFreePickDragging();
    }

    private void StopFreePickDragging()
    {
        _freePickDragging = false;
        FreePickArea.ReleaseMouseCapture();
    }

    private void UpdateColorFromFreePickPosition(Point position)
    {
        double width = FreePickCanvas.ActualWidth;
        double height = FreePickCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double sat = Math.Clamp(position.X / width, 0, 1);
        double val = Math.Clamp(1 - position.Y / height, 0, 1);

        // Move the indicator first, in canvas coordinates, so it sticks to the cursor regardless of
        // any rounding the upcoming RGB conversion does. ApplyColor with sourceFreePick=true skips
        // the recompute path that would otherwise snap it back to the rounded-byte HSV.
        PositionFreePickIndicator(sat * width, (1 - val) * height);

        Color rgb = HSVToRGB(_freePickHue, sat, val);
        Color next = Color.FromArgb(_currentColor.A, rgb.R, rgb.G, rgb.B);
        ApplyColor(next, sourceFreePick: true);
    }

    private void UpdateFreePickFromColor(bool refreshHueFromColor = true)
    {
        double width = FreePickCanvas.ActualWidth;
        double height = FreePickCanvas.ActualHeight;
        if (width <= 0 || height <= 0) return;

        (double hue, double sat, double val) = RGBToHSV(_currentColor.R, _currentColor.G, _currentColor.B);

        // HSV's hue collapses to 0 whenever saturation is 0 (any pure gray), and we don't want the
        // background to flip back to red when the user types or drags into the gray axis. Only adopt
        // the freshly-derived hue when there is enough chroma to define one. The hue-slider drag path
        // also passes refreshHueFromColor=false so RGB->HSV->RGB rounding drift can't slowly walk the
        // user's intended hue away from the slider's actual value during a drag.
        if (refreshHueFromColor && sat > 0) _freePickHue = hue;

        Color hueOnly = HSVToRGB(_freePickHue, 1.0, 1.0);
        FreePickArea.Background = new SolidColorBrush(hueOnly);

        PositionFreePickIndicator(sat * width, (1 - val) * height);
    }

    private void UpdateAlphaGradient()
    {
        // Top stop = current rgb at alpha 0 (transparent), bottom stop = current rgb at alpha 0xFF
        // (opaque). The bar visualises the alpha axis against the picker's current color so the user
        // can see exactly what alpha would buy them. Stops are in the same order they were added in
        // the constructor: index 0 is the StartPoint side (top), index 1 is the EndPoint side (bottom).
        byte r = _currentColor.R;
        byte g = _currentColor.G;
        byte b = _currentColor.B;
        _alphaGradient.GradientStops[0].Color = Color.FromArgb(0x00, r, g, b);
        _alphaGradient.GradientStops[1].Color = Color.FromArgb(0xFF, r, g, b);
    }

    private void UpdateChannelSliderThumbs()
    {
        // Hue thumb shows the pure hue (S=V=1) at the slider's current position so the grip itself
        // reads as a colour-coded handle even when the current rgb is a desaturated tint.
        // Alpha thumb shows the current rgba (alpha included) so the grip previews exactly what the
        // user is dialling in - the bar gives context, the thumb gives the answer.
        Color hueColor = HSVToRGB(_freePickHue, 1.0, 1.0);
        _hueThumbBrush.Color = hueColor;
        _alphaThumbBrush.Color = _currentColor;

        // Alpha thumb's stroke flips between white and black at the perceptual-luminance midpoint
        // (Rec. 709 weights) of the EFFECTIVE VISIBLE thumb color, not the raw rgb. The thumb's
        // rgb is alpha-blended over the gradient bar, which is itself alpha-blended over the
        // window's themed background - at low alpha the user is mostly looking at that backdrop,
        // not the rgb. Snapping to extremes (instead of a continuous inverse) avoids the mid-gray
        // case where a linear inverse would converge to the same gray it's supposed to outline.
        Color visible = ComputeAlphaThumbVisibleColor();
        double luminance = 0.2126 * visible.R + 0.7152 * visible.G + 0.0722 * visible.B;
        _alphaThumbBorderBrush.Color = luminance < 128 ? Colors.White : Colors.Black;
    }

    private Color ComputeAlphaThumbVisibleColor()
    {
        // The thumb sits on top of the alpha gradient (bottom=opaque rgb, top=transparent rgb),
        // which itself sits over the window's themed background. The thumb's vertical position
        // tracks its alpha (IsDirectionReversed: value 255 = bottom = opaque), so the gradient
        // alpha at the thumb's pixels equals the thumb's own alpha. Both blends are linear:
        //     visible_gradient = rgb*a + bg*(1-a)
        //     visible_thumb    = rgb*a + visible_gradient*(1-a)
        //                      = rgb*a*(2-a) + bg*(1-a)^2
        // Reduces correctly at the extremes: a=1 -> rgb, a=0 -> bg.
        Color bg = ResolveSliderBackgroundColor();
        double a = _currentColor.A / 255.0;
        double rgbWeight = a * (2 - a);
        double bgWeight = (1 - a) * (1 - a);

        byte r = (byte)Math.Round(_currentColor.R * rgbWeight + bg.R * bgWeight);
        byte g = (byte)Math.Round(_currentColor.G * rgbWeight + bg.G * bgWeight);
        byte b = (byte)Math.Round(_currentColor.B * rgbWeight + bg.B * bgWeight);
        return Color.FromRgb(r, g, b);
    }

    private Color ResolveSliderBackgroundColor()
    {
        // The slider lives inside RootBorder which paints ThemeBackground; the alpha gradient
        // composites over that brush, so the same color is what shows through at low alpha.
        // Falls back to white when the resource isn't a SolidColorBrush (e.g. an unexpected theme
        // override) - any defined fallback beats throwing during a constructor-time sync.
        return TryFindResource("ThemeBackground") is SolidColorBrush brush
            ? brush.Color
            : Colors.White;
    }

    private void PositionFreePickIndicator(double centerX, double centerY)
    {
        Canvas.SetLeft(FreePickIndicator, centerX - FreePickIndicator.Width / 2);
        Canvas.SetTop(FreePickIndicator, centerY - FreePickIndicator.Height / 2);
        Canvas.SetLeft(FreePickIndicatorRing, centerX - FreePickIndicatorRing.Width / 2);
        Canvas.SetTop(FreePickIndicatorRing, centerY - FreePickIndicatorRing.Height / 2);
    }

    // Standard HSV -> RGB conversion. hue in degrees [0, 360), sat / val in [0, 1].
    // Returns an opaque color; callers substitute the alpha they want to preserve.
    private static Color HSVToRGB(double hue, double sat, double val)
    {
        if (sat <= 0)
        {
            byte gray = (byte)Math.Round(Math.Clamp(val, 0, 1) * 255);
            return Color.FromArgb(0xFF, gray, gray, gray);
        }

        double h = ((hue % 360) + 360) % 360 / 60.0;
        int sector = (int)Math.Floor(h);
        double f = h - sector;
        double p = val * (1 - sat);
        double q = val * (1 - sat * f);
        double t = val * (1 - sat * (1 - f));

        (double r, double g, double b) = sector switch
        {
            0 => (val, t, p),
            1 => (q, val, p),
            2 => (p, val, t),
            3 => (p, q, val),
            4 => (t, p, val),
            _ => (val, p, q),
        };

        return Color.FromArgb(
            0xFF,
            (byte)Math.Round(Math.Clamp(r, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(g, 0, 1) * 255),
            (byte)Math.Round(Math.Clamp(b, 0, 1) * 255));
    }

    // Standard RGB -> HSV conversion. Inputs in [0, 255]; outputs hue in [0, 360), sat / val in [0, 1].
    // Hue is undefined when sat is 0 - callers must decide whether to keep a previously remembered hue.
    private static (double Hue, double Sat, double Val) RGBToHSV(byte r, byte g, byte b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;
        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double val = max;
        double sat = max == 0 ? 0 : delta / max;
        double hue = 0;

        if (delta > 0)
        {
            if (max == rd) hue = 60.0 * (((gd - bd) / delta) % 6);
            else if (max == gd) hue = 60.0 * ((bd - rd) / delta + 2);
            else hue = 60.0 * ((rd - gd) / delta + 4);
        }

        if (hue < 0) hue += 360;
        return (hue, sat, val);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T typedChild) return typedChild;

            T? result = FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// Parses a hex string in either ARGB (AARRGGBB) or RGBA (RRGGBBAA) byte order.
    /// Accepts a leading '#' and is case-insensitive.
    /// 6-char input is treated as RGB with alpha defaulted to 0xFF (in either order, since alpha is absent).
    /// Returns false on any malformed input - the caller leaves the current color untouched.
    /// </summary>
    private static bool TryParseHex(string input, bool argbOrder, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string h = input.Trim().TrimStart('#');
        if (h.Length != 6 && h.Length != 8) return false;

        try
        {
            if (h.Length == 6)
            {
                byte r = Convert.ToByte(h[..2], 16);
                byte g = Convert.ToByte(h[2..4], 16);
                byte b = Convert.ToByte(h[4..6], 16);
                color = Color.FromArgb(0xFF, r, g, b);
                return true;
            }

            byte b0 = Convert.ToByte(h[..2], 16);
            byte b1 = Convert.ToByte(h[2..4], 16);
            byte b2 = Convert.ToByte(h[4..6], 16);
            byte b3 = Convert.ToByte(h[6..8], 16);
            color = argbOrder
                ? Color.FromArgb(b0, b1, b2, b3)
                : Color.FromArgb(b3, b0, b1, b2);
            return true;
        }
        catch (FormatException) { return false; }
    }
}
