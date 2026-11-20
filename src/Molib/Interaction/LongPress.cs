using Gum.Forms.Controls;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// The long press of one element, created by <see cref="InteractiveGueExtensions.UseLongPress"/>.
/// </summary>
/// <remarks>
/// <para>
/// It starts with the <c>Push</c> of the element and ends with <c>LosePush</c> (Gum raises it on the release and when
/// the pointer leaves the element), with a move of the pointer by more than 10 canvas units, or with a move of the
/// element itself by more than 10 units, which is what a scrolling parent does. Thresholds are in canvas units, so a
/// press feels the same on a phone and on a monitor relative to the interface.
/// </para>
/// <para>
/// Gum still raises <c>Click</c> on the release after a long press, and molib cannot take it back, so
/// <see cref="ConsumedClick"/> tells the click handler to skip it. Gum raises <c>Push</c> of a visual for the mouse
/// and touch only, so there is no long press on the keyboard or a gamepad.
/// ADR: interaction/gestures — Long press, Thresholds.
/// </para>
/// </remarks>
public sealed class LongPress
{
    private const float MoveTolerance = 10;

    private readonly Gestures _owner;
    private float _heldSeconds;
    private float _pressX;
    private float _pressY;
    private float _elementX;
    private float _elementY;
    private bool _isHeld;

    internal LongPress(Gestures owner, InteractiveGue element, LongPressOptions options)
    {
        _owner = owner;
        Element = element;
        Options = options;
        element.Push += HandlePush;
        element.LosePush += HandleLosePush;
    }

    /// <summary>
    /// Whether the long press is still on its element.
    /// </summary>
    internal bool IsAttached { get; private set; } = true;

    /// <summary>
    /// The element that is held.
    /// </summary>
    public InteractiveGue Element { get; }

    /// <summary>
    /// How long the press has to be held.
    /// </summary>
    public LongPressOptions Options { get; }

    /// <summary>
    /// Whether the long press fired during the current press, so the <c>Click</c> that Gum raises on the release
    /// must be skipped. Stays <see langword="true"/> until the next <c>Push</c> of the element.
    /// </summary>
    public bool ConsumedClick { get; private set; }

    /// <summary>
    /// Whether the owner keeps this long press in its list of held ones.
    /// </summary>
    internal bool IsListed { get; set; }

    /// <summary>
    /// Takes the long press off the element: its handlers leave the element's events, and the element may get a new
    /// long press. Does nothing if it is detached already.
    /// </summary>
    /// <remarks>
    /// The handlers hold this object and, through it, the service; an element that outlives its screen would
    /// otherwise keep them. The disposal of the service detaches every long press. ADR: interaction/gestures — Long
    /// press.
    /// </remarks>
    public void Detach() => _owner.Remove(this);

    /// <summary>
    /// Raised once per press, when the pointer has been held on the element for
    /// <see cref="LongPressOptions.Duration"/>.
    /// </summary>
    public event EventHandler? Triggered;

    /// <summary>
    /// Counts the hold time and fires the long press.
    /// </summary>
    /// <param name="seconds">Real frame time.</param>
    /// <returns>Whether the press is still held and not fired.</returns>
    internal bool Update(float seconds)
    {
        if (!_isHeld)
        {
            return false;
        }

        ICursor cursor = FrameworkElement.MainCursor;
        if (!cursor.PrimaryDown || HasMoved(cursor))
        {
            _isHeld = false;
            return false;
        }

        _heldSeconds += seconds;
        if (_heldSeconds < Options.Duration)
        {
            return true;
        }

        _isHeld = false;
        ConsumedClick = true;
        Triggered?.Invoke(this, EventArgs.Empty);
        return false;
    }

    /// <summary>
    /// Leaves the events of the element and forgets a press that is held.
    /// </summary>
    internal void Unsubscribe()
    {
        Element.Push -= HandlePush;
        Element.LosePush -= HandleLosePush;
        IsAttached = false;
        _isHeld = false;
    }

    private static float Distance(float x, float y) => MathF.Sqrt((x * x) + (y * y));

    private void HandlePush(object? sender, EventArgs e)
    {
        ICursor cursor = FrameworkElement.MainCursor;
        ConsumedClick = false;
        _isHeld = true;
        _heldSeconds = 0;
        _pressX = cursor.XRespectingGumZoomAndBounds();
        _pressY = cursor.YRespectingGumZoomAndBounds();
        _elementX = Element.AbsoluteLeft;
        _elementY = Element.AbsoluteTop;
        _owner.Activate(this);
    }

    private void HandleLosePush(object? sender, EventArgs e) => _isHeld = false;

    private bool HasMoved(ICursor cursor) =>
        Distance(cursor.XRespectingGumZoomAndBounds() - _pressX, cursor.YRespectingGumZoomAndBounds() - _pressY)
            > MoveTolerance
        || Distance(Element.AbsoluteLeft - _elementX, Element.AbsoluteTop - _elementY) > MoveTolerance;
}
