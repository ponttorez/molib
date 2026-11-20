using System.Diagnostics;
using Gum.Converters;
using Gum.GueDeriving;
using Gum.Managers;
using Gum.Wireframe;
using RenderingLibrary.Graphics;

namespace Molib.Motion;

/// <summary>
/// Showing or hiding of one element: moves its transparency and vertical offset, then hides or removes it.
/// </summary>
/// <remarks>
/// <para>
/// The animation remembers the values the element had before the first call (position, transparency, render
/// target, input flags) and puts them back at the end, so the next <c>ShowAsync</c> starts from them. A later call
/// on the same element turns the animation around from its current pose.
/// </para>
/// <para>
/// Transparency of a whole subtree needs a render target: <c>Alpha</c> of an <c>InvisibleRenderable</c> affects its
/// children only while it draws through one. An element with its own color fades by itself instead.
/// ADR: motion/show-hide — Transparency.
/// </para>
/// </remarks>
internal sealed class ShowHideAnimation : IMotionTarget
{
    private readonly ShowHideAnimator _owner;
    private ShowHideCall? _call;
    private HideAction _then;
    private bool _isShowing;
    private Fading _fading;
    private InvisibleRenderable? _invisible;
    private bool _wasRenderTarget;
    private int _baseAlpha;
    private int _baseStrokeAlpha;
    private bool _slides;
    private float _baseY;
    private InteractiveGue? _interactive;
    private bool _hadEvents;
    private bool _exposedChildrenEvents;
    private bool _isInputBlocked;
    private float _opacity;
    private float _offset;
    private float _fromOpacity;
    private float _toOpacity;
    private float _fromOffset;
    private float _toOffset;

    /// <summary>
    /// Creates an idle animation; <see cref="ShowHideAnimator"/> keeps it in a pool.
    /// </summary>
    /// <param name="owner">The animator that runs it.</param>
    public ShowHideAnimation(ShowHideAnimator owner)
    {
        _owner = owner;
        Progress = new MotionProgress(this);
    }

    /// <summary>
    /// How the element fades.
    /// </summary>
    private enum Fading
    {
        /// <summary>The element has no transparency molib can change.</summary>
        None,

        /// <summary>Alpha of the <c>InvisibleRenderable</c>, drawn through a render target.</summary>
        RenderTarget,

        /// <summary>Own <c>Alpha</c> of a sprite, nine-slice, text or polygon.</summary>
        OwnAlpha,

        /// <summary><c>FillAlpha</c> and <c>StrokeAlpha</c> of a rectangle or a circle.</summary>
        FillAndStroke,
    }

    /// <summary>
    /// The element being shown or hidden, or <see langword="null"/> while the animation is in the pool.
    /// </summary>
    public GraphicalUiElement? Element { get; private set; }

    /// <summary>
    /// The progress the motion core moves.
    /// </summary>
    public MotionProgress Progress { get; }

    /// <inheritdoc/>
    bool IMotionTarget.ShouldFinishNow => _call?.IsCancelRequested == true;

    /// <summary>
    /// Takes <paramref name="element"/> and remembers the values it has now.
    /// </summary>
    /// <param name="element">Element to animate.</param>
    /// <param name="isShown">Whether the element is shown now; a hidden one starts from the hidden pose of
    /// <paramref name="preset"/>.</param>
    /// <param name="preset">Preset of the first call.</param>
    public void Begin(GraphicalUiElement element, bool isShown, ShowHide preset)
    {
        Debug.Assert(Element is null, "A pooled animation is idle.");
        Element = element;
        _baseY = element.Y;
        _slides = element.YUnits.GetIsPixelBased();
        _interactive = element as InteractiveGue;
        _hadEvents = _interactive?.HasEvents == true;
        _exposedChildrenEvents = _interactive?.ExposeChildrenEvents != false;
        _isInputBlocked = false;
        CaptureFading(element);
        _opacity = isShown || !preset.Fades ? 1 : 0;
        _offset = isShown ? 0 : preset.OffsetY;
    }

    /// <summary>
    /// Points the animation at showing or hiding with <paramref name="preset"/>, starting from the current pose.
    /// </summary>
    /// <param name="show">Whether to show the element.</param>
    /// <param name="preset">Fade, slide and motion.</param>
    /// <param name="then">What to do with the element after hiding.</param>
    /// <param name="call">The call that waits for the result.</param>
    /// <returns>The call that was waiting before, to complete with <see langword="false"/> once the new one is set.
    /// </returns>
    public ShowHideCall? Retarget(bool show, ShowHide preset, HideAction then, ShowHideCall call)
    {
        Debug.Assert(Element is not null, "Begin comes first.");
        ReportProblems(Element, preset);
        ShowHideCall? previous = _call;
        _call = call;
        _isShowing = show;
        _then = then;

        float hiddenOpacity = preset.Fades ? 0 : 1;
        _fromOpacity = _opacity;
        _fromOffset = _offset;
        _toOpacity = show ? 1 : hiddenOpacity;
        _toOffset = show ? 0 : preset.OffsetY;

        SetInputBlocked(!show);
        if (_fading == Fading.RenderTarget && _invisible is not null && (_fromOpacity < 1 || _toOpacity < 1))
        {
            _invisible.IsRenderTarget = true;
        }

        WritePose();
        if (show)
        {
            Element.Visible = true;
        }

        return previous;
    }

    /// <inheritdoc/>
    void IMotionTarget.ApplyProgress(float progress)
    {
        _opacity = _fromOpacity + ((_toOpacity - _fromOpacity) * Math.Clamp(progress, 0f, 1f));
        _offset = _fromOffset + ((_toOffset - _fromOffset) * progress);
        WritePose();
    }

    /// <inheritdoc/>
    void IMotionTarget.CompleteMotion() => _owner.Complete(this);

    /// <summary>
    /// Hides or removes the element if it was hiding, puts back the values it had before the first call and
    /// releases it.
    /// </summary>
    /// <remarks>
    /// After showing, the values put back are the target pose itself. After hiding, the element is already
    /// invisible or out of the tree when they are put back, so nothing flashes.
    /// </remarks>
    /// <returns>The call that waited for the end.</returns>
    public ShowHideCall? End()
    {
        Debug.Assert(Element is not null, "Begin comes first.");
        GraphicalUiElement element = Element;
        if (!_isShowing)
        {
            if (_then == HideAction.Remove)
            {
                element.RemoveFromRoot();
            }
            else
            {
                element.Visible = false;
            }
        }

        _opacity = 1;
        _offset = 0;
        WritePose();
        SetInputBlocked(false);
        _invisible?.IsRenderTarget = _wasRenderTarget;

        ShowHideCall? call = _call;
        _call = null;
        Element = null;
        _interactive = null;
        _invisible = null;
        return call;
    }

    private static string NameOf(GraphicalUiElement element) =>
        string.IsNullOrEmpty(element.Name) ? element.GetType().Name : element.Name;

    private void CaptureFading(GraphicalUiElement element)
    {
        _invisible = null;
        _baseStrokeAlpha = 0;
        switch (element)
        {
            case SpriteRuntime sprite:
                _fading = Fading.OwnAlpha;
                _baseAlpha = sprite.Alpha;
                break;
            case NineSliceRuntime nineSlice:
                _fading = Fading.OwnAlpha;
                _baseAlpha = nineSlice.Alpha;
                break;
            case TextRuntime text:
                _fading = Fading.OwnAlpha;
                _baseAlpha = text.Alpha;
                break;
            case PolygonRuntime polygon:
                _fading = Fading.OwnAlpha;
                _baseAlpha = polygon.Alpha;
                break;
            case RectangleRuntime rectangle:
                _fading = Fading.FillAndStroke;
                _baseAlpha = rectangle.FillAlpha;
                _baseStrokeAlpha = rectangle.StrokeAlpha;
                break;
            case CircleRuntime circle:
                _fading = Fading.FillAndStroke;
                _baseAlpha = circle.FillAlpha;
                _baseStrokeAlpha = circle.StrokeAlpha;
                break;
            default:
                if (element.RenderableComponent is InvisibleRenderable invisible)
                {
                    _fading = Fading.RenderTarget;
                    _invisible = invisible;
                    _wasRenderTarget = invisible.IsRenderTarget;
                    _baseAlpha = invisible.Alpha;
                }
                else
                {
                    _fading = Fading.None;
                    _baseAlpha = 255;
                }

                break;
        }
    }

    /// <remarks>ADR: motion/show-hide — Slide, Transparency.</remarks>
    private void ReportProblems(GraphicalUiElement element, ShowHide preset)
    {
        MolibDiagnostics diagnostics = _owner.Diagnostics;
        if (preset.Fades && _fading == Fading.None)
        {
            diagnostics.WarnOnce(
                "showHide.noAlpha",
                NameOf(element),
                $"'{NameOf(element)}' has no transparency molib can change; it shows and hides without fading.");
        }

        if (preset.Fades && (_fading is Fading.OwnAlpha or Fading.FillAndStroke) && element.Children.Count > 0)
        {
            diagnostics.WarnOnce(
                "showHide.childrenNotFaded",
                NameOf(element),
                $"'{NameOf(element)}' fades by its own color, so its children do not fade. Put it into a container "
                    + "and show the container instead.");
        }

        if (preset.OffsetY == 0)
        {
            return;
        }

        if (!_slides)
        {
            diagnostics.WarnOnce(
                "showHide.unsupportedUnits",
                NameOf(element),
                $"'{NameOf(element)}' does not measure Y in pixels, so it shows and hides without sliding.");
        }
        else if (element.Parent?.ChildrenLayout is ChildrenLayout.TopToBottomStack or ChildrenLayout.LeftToRightStack)
        {
            diagnostics.WarnOnce(
                "showHide.slideInStack",
                NameOf(element),
                $"'{NameOf(element)}' is in a stack: sliding it moves the siblings that follow it. Show elements "
                    + "that slide on top of the layout, outside stacks.");
        }
    }

    private void SetInputBlocked(bool isBlocked)
    {
        if (_interactive is null || _isInputBlocked == isBlocked)
        {
            return;
        }

        _isInputBlocked = isBlocked;
        _interactive.HasEvents = !isBlocked && _hadEvents;
        _interactive.ExposeChildrenEvents = !isBlocked && _exposedChildrenEvents;
    }

    private void WritePose()
    {
        Debug.Assert(Element is not null, "Begin comes first.");
        if (_slides)
        {
            Element.Y = _baseY + _offset;
        }

        int alpha = (int)MathF.Round(_baseAlpha * _opacity);
        switch (_fading)
        {
            case Fading.RenderTarget when _invisible is not null:
                if (Element is ContainerRuntime container)
                {
                    container.Alpha = alpha;
                }
                else
                {
                    _invisible.Alpha = alpha;
                }

                break;
            case Fading.OwnAlpha:
                WriteOwnAlpha(Element, alpha);
                break;
            case Fading.FillAndStroke:
                WriteFillAndStrokeAlpha(Element, alpha, (int)MathF.Round(_baseStrokeAlpha * _opacity));
                break;
        }
    }

    private static void WriteOwnAlpha(GraphicalUiElement element, int alpha)
    {
        switch (element)
        {
            case SpriteRuntime sprite:
                sprite.Alpha = alpha;
                break;
            case NineSliceRuntime nineSlice:
                nineSlice.Alpha = alpha;
                break;
            case TextRuntime text:
                text.Alpha = alpha;
                break;
            case PolygonRuntime polygon:
                polygon.Alpha = alpha;
                break;
        }
    }

    private static void WriteFillAndStrokeAlpha(GraphicalUiElement element, int fillAlpha, int strokeAlpha)
    {
        if (element is RectangleRuntime rectangle)
        {
            rectangle.FillAlpha = fillAlpha;
            rectangle.StrokeAlpha = strokeAlpha;
        }
        else if (element is CircleRuntime circle)
        {
            circle.FillAlpha = fillAlpha;
            circle.StrokeAlpha = strokeAlpha;
        }
    }
}
