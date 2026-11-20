using System.Collections.ObjectModel;
using Gum.Forms.Controls;
using Gum.Wireframe;
using Molib.Integration;
using Molib.Motion;

namespace Molib.Interaction;

/// <summary>
/// Touch inertia and snapping of one scroll viewer, created by <see cref="ScrollViewerExtensions.UseInertia"/>.
/// </summary>
/// <remarks>
/// <para>
/// Gum scrolls the content with the finger, the wheel, the bars and the right stick, and stops the moment the
/// finger lifts. While a finger is down on the viewer, molib records how its scroll values change; after the release
/// the content coasts on and slows down, or a spring takes it to the nearest snap point. Gum keeps the values inside
/// the range of the bars, so the content stops at the edge without a rubber band.
/// </para>
/// <para>
/// The viewer tells molib about a press (<c>PushPreview</c>) and about a change of its values
/// (<c>ScrollChanged</c>), so a viewer that does not move costs nothing per frame. ADR: interaction/scrolling —
/// Decision.
/// </para>
/// <para>
/// <see cref="ScrollVelocity"/> measures the fling and <see cref="SnapPoints"/> finds where the viewer may come to
/// rest; this class moves the values and reacts to the viewer, the focus and paging.
/// </para>
/// </remarks>
public sealed class ScrollInertia
{
    private const float StopSpeed = 5f;
    private const float SettleDelay = 0.15f;
    private const float RestDistance = 0.5f;
    private const float RestSpeed = 5f;
    private const float FieldMargin = 16f;
    private const double Tolerance = 0.001;

    private readonly Scrolling _owner;
    private readonly ScrollVelocity _velocity = new();
    private readonly SnapPoints _snaps;
    private AxisMotion _horizontal;
    private AxisMotion _vertical;
    private bool _isTracking;
    private bool _isTouch;
    private int _pageAtPush = -1;
    private float _settleSeconds = -1;
    private TextBoxBase? _fieldToReveal;
    private bool _isWriting;
    private bool _isCounted;
    private double _knownX;
    private double _knownY;
    private double _beforeX;
    private double _beforeY;
    private int _changeFrame = -1;

    internal ScrollInertia(Scrolling owner, ScrollViewer viewer, InertiaOptions options)
    {
        _owner = owner;
        Viewer = viewer;
        Options = options;
        _snaps = new SnapPoints(viewer, options);
        _knownX = viewer.HorizontalScrollBarValue;
        _knownY = viewer.VerticalScrollBarValue;
        viewer.Visual.PushPreview += HandlePushPreview;
        viewer.ScrollChanged += HandleScrollChanged;
        if (options.Snap == ScrollSnap.Pages)
        {
            owner.Input.SetPaging(viewer.Visual, Page);
        }
    }

    /// <summary>
    /// What moves one axis of the scroll values.
    /// </summary>
    private enum AxisMode
    {
        /// <summary>Nothing: Gum or the game moves the value.</summary>
        Rest,

        /// <summary>Inertia after a fling.</summary>
        Coast,

        /// <summary>A spring towards a snap point or a text field.</summary>
        Spring,
    }

    /// <summary>
    /// The scroll viewer that coasts and snaps.
    /// </summary>
    public ScrollViewer Viewer { get; }

    /// <summary>
    /// How the viewer coasts and where it comes to rest.
    /// </summary>
    public InertiaOptions Options { get; }

    /// <summary>
    /// The item or page the viewer came to rest on last, or <c>-1</c> before the first snap.
    /// </summary>
    public int SnapIndex { get; private set; } = -1;

    /// <summary>
    /// Whether the owner keeps this inertia in its list of active ones.
    /// </summary>
    internal bool IsListed { get; set; }

    private bool IsBusy => _isTracking || _settleSeconds >= 0 || _fieldToReveal is not null
        || _horizontal.Mode != AxisMode.Rest || _vertical.Mode != AxisMode.Rest;

    /// <summary>
    /// Raised when the viewer comes to rest on another item or page than before; with <see cref="ScrollSnap.None"/>
    /// never.
    /// </summary>
    /// <remarks>Use it for the page dots of a carousel. ADR: interaction/scrolling — Snapping.</remarks>
    public event EventHandler<SnapChangedEventArgs>? SnapChanged;

    /// <summary>
    /// Whether the inertia is still on its viewer.
    /// </summary>
    internal bool IsAttached { get; private set; } = true;

    /// <summary>
    /// Takes the inertia off the viewer: the motion stops where it is, the handlers leave the viewer's events, a
    /// <see cref="ScrollSnap.Pages"/> viewer is no longer a paging area, and the viewer may get new inertia. Does
    /// nothing if it is detached already.
    /// </summary>
    /// <remarks>
    /// The handlers hold this object and, through it, the service; a viewer that outlives its screen would otherwise
    /// keep them. The disposal of the service detaches every inertia. ADR: interaction/scrolling — Attaching.
    /// </remarks>
    public void Detach() => _owner.Remove(this);

    /// <summary>
    /// Records the press, coasts or springs the values and finishes the wait before a snap.
    /// </summary>
    /// <param name="isPointerDown">Whether the primary button of the cursor or a finger is down.</param>
    /// <param name="seconds">Real frame time: measures the fling and the wait before a snap.</param>
    /// <param name="scaledSeconds">Frame time scaled by <c>MotionCore.TimeScale</c>: moves the values.</param>
    /// <returns>Whether the inertia still has work in the next frame.</returns>
    internal bool Update(bool isPointerDown, float seconds, float scaledSeconds)
    {
        if (_isTracking)
        {
            if (isPointerDown)
            {
                _velocity.Track(seconds, Viewer.HorizontalScrollBarValue, Viewer.VerticalScrollBarValue);
                return true;
            }

            _isTracking = false;
            Release();
        }

        if (_fieldToReveal is not null && !isPointerDown)
        {
            TextBoxBase field = _fieldToReveal;
            _fieldToReveal = null;
            RevealField(field);
        }

        if (_settleSeconds >= 0)
        {
            _settleSeconds += seconds;
            if (_settleSeconds >= SettleDelay)
            {
                _settleSeconds = -1;
                SnapToNearest();
            }
        }

        Move(ref _horizontal, vertical: false, scaledSeconds);
        Move(ref _vertical, vertical: true, scaledSeconds);
        UpdateCounting();
        return IsBusy;
    }

    /// <summary>
    /// Ends the record of a press without a fling: drag and drop took the pointer.
    /// </summary>
    /// <remarks>ADR: interaction/drag-drop — Scrolling during a drag.</remarks>
    internal void StopTracking() => _isTracking = false;

    /// <summary>
    /// Stops the motion, leaves the viewer's events and gives up the paging mark.
    /// </summary>
    internal void Unsubscribe()
    {
        _isTracking = false;
        _fieldToReveal = null;
        StopMotion();
        Viewer.Visual.PushPreview -= HandlePushPreview;
        Viewer.ScrollChanged -= HandleScrollChanged;
        if (Options.Snap == ScrollSnap.Pages)
        {
            _owner.Input.SetPaging(Viewer.Visual, page: null);
        }

        IsAttached = false;
    }

    /// <summary>
    /// Reacts to the focus moving onto <paramref name="focused"/> inside the viewer.
    /// </summary>
    /// <param name="focused">The element that got the focus.</param>
    /// <param name="isNavigation">Whether the last device is a gamepad or the keyboard.</param>
    /// <param name="isTouch">Whether the cursor of Gum is a finger.</param>
    /// <remarks>
    /// Inertia stops, so it does not carry the focused element away. A gamepad or the keyboard: Gum has already
    /// scrolled to the element at once; molib puts the values back where they were before that jump and springs
    /// them to the snap point of the element. A text field focused by touch: after the finger lifts, molib scrolls
    /// it into the upper part of the viewer, above the Android keyboard.
    /// ADR: interaction/scrolling — Focus and scrolling, Snapping.
    /// </remarks>
    internal void OnFocusEntered(FrameworkElement focused, bool isNavigation, bool isTouch)
    {
        StopMotion();
        if (focused is TextBoxBase field && isTouch)
        {
            _fieldToReveal = field;
            _owner.Activate(this);
            return;
        }

        if (Options.Snap == ScrollSnap.None || !_snaps.TryGetMainAxis(out bool vertical))
        {
            return;
        }

        double point = 0;
        int index = isNavigation ? _snaps.Of(focused.Visual, vertical, out point) : -1;
        if (index < 0)
        {
            StartSettle();
            return;
        }

        if (_changeFrame == _owner.Frame)
        {
            Write(vertical, vertical ? _beforeY : _beforeX);
        }

        StartSpring(vertical, point, 0, index);
    }

    /// <remarks>
    /// Molib's own writes also raise <c>ScrollChanged</c>; any other change (the wheel, a bar, the right stick, the
    /// focus, the game) stops the coasting and, with snapping, waits for the values to settle. The value before the
    /// first change of a frame is kept: the focus snap starts from it. ADR: interaction/scrolling — Snapping.
    /// </remarks>
    private void HandleScrollChanged(object? sender, EventArgs e)
    {
        if (!_isWriting)
        {
            if (_changeFrame != _owner.Frame)
            {
                _changeFrame = _owner.Frame;
                _beforeX = _knownX;
                _beforeY = _knownY;
            }

            if (!_isTracking)
            {
                StopMotion();
                if (Options.Snap != ScrollSnap.None)
                {
                    StartSettle();
                }
            }
        }

        _knownX = Viewer.HorizontalScrollBarValue;
        _knownY = Viewer.VerticalScrollBarValue;
    }

    /// <remarks>
    /// <c>PushPreview</c> reaches the viewer before the element under the cursor, for any press over it: a touch on
    /// the content, a button inside, a bar. ADR: interaction/scrolling — Measuring velocity.
    /// </remarks>
    private void HandlePushPreview(object? sender, RoutedEventArgs e)
    {
        StopMotion();
        _isTracking = true;
        _isTouch = FrameworkElement.MainCursor.LastInputDevice == InputDevice.TouchScreen;
        _velocity.Reset(Viewer.HorizontalScrollBarValue, Viewer.VerticalScrollBarValue);
        _pageAtPush = Options.Snap == ScrollSnap.Pages && _snaps.TryGetMainAxis(out bool vertical)
            ? _snaps.Nearest(vertical, Read(vertical), out _)
            : -1;

        _owner.Activate(this);
    }

    /// <remarks>
    /// Only a finger flings: Gum does not drag the content with the mouse, and the wheel and the bars are exact.
    /// With snapping, a fling picks the snap point nearest to where the coasting would stop, <c>value + v0 /
    /// Friction</c>, and a spring takes over with the speed of the fling, so the content does not roll back at
    /// the end. ADR: interaction/scrolling — Measuring velocity, Snapping.
    /// </remarks>
    private void Release()
    {
        double velocityX = _isTouch ? _velocity.ReleaseVelocity(vertical: false) : 0;
        double velocityY = _isTouch ? _velocity.ReleaseVelocity(vertical: true) : 0;
        if (Options.Snap == ScrollSnap.None || !_snaps.TryGetMainAxis(out bool vertical))
        {
            Coast(vertical: false, velocityX);
            Coast(vertical: true, velocityY);
            return;
        }

        double velocity = vertical ? velocityY : velocityX;
        if (Math.Abs(velocity) >= StopSpeed)
        {
            double predicted = Read(vertical) + (velocity / Options.Friction);
            int index = _snaps.Nearest(vertical, predicted, out double point, _pageAtPush);
            StartSpring(vertical, point, velocity, index);
        }
        else
        {
            StartSettle();
        }

        Coast(!vertical, vertical ? velocityX : velocityY);
    }

    private void Coast(bool vertical, double velocity)
    {
        if (Math.Abs(velocity) < StopSpeed)
        {
            return;
        }

        ref AxisMotion axis = ref AxisOf(vertical);
        axis.Mode = AxisMode.Coast;
        axis.Velocity = (float)velocity;
        UpdateCounting();
        _owner.Activate(this);
    }

    private void StartSpring(bool vertical, double target, double velocity, int snapIndex)
    {
        ref AxisMotion axis = ref AxisOf(vertical);
        axis.Target = target;
        axis.Offset = (float)(Read(vertical) - target);
        axis.Velocity = (float)velocity;
        axis.SnapIndex = snapIndex;
        if (MathF.Abs(axis.Offset) < RestDistance && MathF.Abs(axis.Velocity) < RestSpeed)
        {
            axis.Mode = AxisMode.Rest;
            Write(vertical, target);
            ReportSnap(snapIndex);
            return;
        }

        axis.Mode = AxisMode.Spring;
        UpdateCounting();
        _owner.Activate(this);
    }

    private void StartSettle()
    {
        _settleSeconds = 0;
        _owner.Activate(this);
    }

    private void StopMotion()
    {
        _horizontal.Mode = AxisMode.Rest;
        _vertical.Mode = AxisMode.Rest;
        _settleSeconds = -1;
        UpdateCounting();
    }

    private void SnapToNearest()
    {
        if (_snaps.TryGetMainAxis(out bool vertical))
        {
            int index = _snaps.Nearest(vertical, Read(vertical), out double point);
            StartSpring(vertical, point, 0, index);
        }
    }

    /// <remarks>
    /// Coasting: <c>v(t) = v0 · exp(-Friction · t)</c>, integrated exactly for the frame; it stops below 5 units/s or
    /// when Gum clamps the value at the edge. The spring is <c>MotionPreset.Smooth</c> run on the value itself.
    /// ADR: interaction/scrolling — Motion; motion/motion-core — Scrolling.
    /// </remarks>
    private void Move(ref AxisMotion axis, bool vertical, float seconds)
    {
        if (axis.Mode == AxisMode.Coast)
        {
            float friction = Options.Friction;
            float decay = MathF.Exp(-friction * seconds);
            double next = Read(vertical) + (axis.Velocity / friction * (1 - decay));
            axis.Velocity *= decay;
            Write(vertical, next);
            if (Math.Abs(Read(vertical) - next) > Tolerance || MathF.Abs(axis.Velocity) < StopSpeed)
            {
                axis.Mode = AxisMode.Rest;
            }
        }
        else if (axis.Mode == AxisMode.Spring)
        {
            float offset = axis.Offset;
            float speed = axis.Velocity;
            MotionPreset spring = MotionPreset.Smooth;
            MotionProgress.StepSpring(spring.Stiffness, spring.DampingRatio, seconds, ref offset, ref speed);
            if (MathF.Abs(offset) < RestDistance && MathF.Abs(speed) < RestSpeed)
            {
                axis.Mode = AxisMode.Rest;
                Write(vertical, axis.Target);
                ReportSnap(axis.SnapIndex);
                return;
            }

            axis.Offset = offset;
            axis.Velocity = speed;
            Write(vertical, axis.Target + offset);
        }
    }

    private void ReportSnap(int index)
    {
        if (index < 0 || index == SnapIndex)
        {
            return;
        }

        SnapIndex = index;
        _owner.Sound.Raise(UiSoundKind.SnapChanged, Viewer.Visual);
        SnapChanged?.Invoke(this, new SnapChangedEventArgs(index));
    }

    /// <summary>
    /// Running motion counts as one animation in <c>MotionCore.ActiveCount</c>.
    /// </summary>
    private void UpdateCounting()
    {
        bool isMoving = _horizontal.Mode != AxisMode.Rest || _vertical.Mode != AxisMode.Rest;
        if (isMoving == _isCounted)
        {
            return;
        }

        _isCounted = isMoving;
        if (isMoving)
        {
            _owner.Motion.BeginOwnMotion();
        }
        else
        {
            _owner.Motion.EndOwnMotion();
        }
    }

    /// <summary>
    /// Turns the viewer one page with LB / RB or Ctrl+PageUp / Ctrl+PageDown and moves the focus onto the new page.
    /// </summary>
    /// <param name="step"><c>-1</c> or <c>+1</c>.</param>
    /// <remarks>
    /// Without the focus move, the next D-pad press would start from the element left on the old page, and Gum
    /// would scroll back to it. ADR: interaction/scrolling — Snapping.
    /// </remarks>
    private void Page(int step)
    {
        if (!_snaps.TryGetMainAxis(out bool vertical))
        {
            return;
        }

        float page = _snaps.Viewport(vertical);
        ref AxisMotion axis = ref AxisOf(vertical);
        int current = axis.Mode == AxisMode.Spring && axis.SnapIndex >= 0
            ? axis.SnapIndex
            : _snaps.Nearest(vertical, Read(vertical), out _);
        int index = Math.Clamp(current + step, 0, _snaps.LastPage(vertical));
        if (index == current)
        {
            return;
        }

        FocusPage(vertical, index * page, page);
        StopMotion();
        StartSpring(vertical, _snaps.PagePoint(vertical, index), 0, index);
    }

    private void FocusPage(bool vertical, float pageStart, float page)
    {
        if (_owner.Input.Focus.Target is not { } focus || !VisualTree.IsInside(focus.Visual, Viewer.Visual))
        {
            return;
        }

        GraphicalUiElement panel = Viewer.InnerPanel;
        ObservableCollection<GraphicalUiElement> children = panel.Children;
        for (int i = 0; i < children.Count; i++)
        {
            GraphicalUiElement child = children[i];
            float start = vertical ? child.AbsoluteTop - panel.AbsoluteTop : child.AbsoluteLeft - panel.AbsoluteLeft;
            if (!child.Visible || start < pageStart - RestDistance || start >= pageStart + page - RestDistance)
            {
                continue;
            }

            if (child is InteractiveGue { FormsControlAsObject: FrameworkElement { IsEnabled: true } control }
                && control is IInputReceiver)
            {
                control.IsFocused = true;
                return;
            }

            if (child is InteractiveGue container
                && FrameworkElement.HandleTab(TabDirection.Down, null, container, shouldAskParent: false, loop: false))
            {
                return;
            }
        }
    }

    private void RevealField(TextBoxBase field)
    {
        GraphicalUiElement panel = Viewer.InnerPanel;
        double maximum = Viewer.VerticalScrollBarMaximum;
        if (maximum <= 0 || !field.Visual.AbsoluteVisible || !VisualTree.IsInside(field.Visual, panel))
        {
            return;
        }

        double top = field.Visual.AbsoluteTop - panel.AbsoluteTop;
        if (top - Viewer.VerticalScrollBarValue <= _snaps.Viewport(vertical: true) / 3)
        {
            return;
        }

        StartSpring(vertical: true, Math.Clamp(top - FieldMargin, 0, maximum), 0, snapIndex: -1);
    }

    private ref AxisMotion AxisOf(bool vertical)
    {
        if (vertical)
        {
            return ref _vertical;
        }

        return ref _horizontal;
    }

    private double Read(bool vertical) => vertical ? Viewer.VerticalScrollBarValue : Viewer.HorizontalScrollBarValue;

    private void Write(bool vertical, double value)
    {
        _isWriting = true;
        try
        {
            if (vertical)
            {
                Viewer.VerticalScrollBarValue = value;
            }
            else
            {
                Viewer.HorizontalScrollBarValue = value;
            }
        }
        finally
        {
            _isWriting = false;
        }
    }

    /// <summary>
    /// Motion of one axis of the scroll values.
    /// </summary>
    private struct AxisMotion
    {
        public AxisMode Mode;
        public float Velocity;
        public float Offset;
        public double Target;
        public int SnapIndex;
    }
}
