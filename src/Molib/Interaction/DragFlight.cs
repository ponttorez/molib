using Gum.Wireframe;
using Molib.Motion;
using RenderingLibrary.Graphics;

namespace Molib.Interaction;

/// <summary>
/// The last move of the drag copy: into the center of the target after a drop, back to the source after a cancel,
/// fading out on the way.
/// </summary>
/// <remarks>ADR: interaction/drag-drop — Pointer: mouse and touch.</remarks>
internal sealed class DragFlight : IMotionTarget
{
    private readonly Action _complete;
    private GraphicalUiElement? _copy;
    private InvisibleRenderable? _renderable;
    private float _fromX;
    private float _fromY;
    private float _toX;
    private float _toY;
    private int _fromAlpha;

    /// <summary>
    /// Creates an idle flight.
    /// </summary>
    /// <param name="complete">Called when the flight ends.</param>
    public DragFlight(Action complete)
    {
        _complete = complete;
        Progress = new MotionProgress(this);
    }

    /// <summary>
    /// The progress the motion core moves.
    /// </summary>
    public MotionProgress Progress { get; }

    /// <inheritdoc/>
    bool IMotionTarget.ShouldFinishNow => false;

    /// <summary>
    /// Points the flight from where <paramref name="copy"/> is now to the canvas position given.
    /// </summary>
    /// <param name="copy">The drag copy.</param>
    /// <param name="toX">Final left edge in canvas units.</param>
    /// <param name="toY">Final top edge in canvas units.</param>
    public void Begin(GraphicalUiElement copy, float toX, float toY)
    {
        _copy = copy;
        _renderable = copy.RenderableComponent as InvisibleRenderable;
        _fromX = copy.X;
        _fromY = copy.Y;
        _fromAlpha = _renderable?.Alpha ?? 255;
        _toX = toX;
        _toY = toY;
    }

    /// <inheritdoc/>
    void IMotionTarget.ApplyProgress(float progress)
    {
        if (_copy is null)
        {
            return;
        }

        _copy.X = _fromX + ((_toX - _fromX) * progress);
        _copy.Y = _fromY + ((_toY - _fromY) * progress);
        _renderable?.Alpha = (int)(_fromAlpha * (1 - Math.Clamp(progress, 0f, 1f)));
    }

    /// <inheritdoc/>
    void IMotionTarget.CompleteMotion()
    {
        _copy = null;
        _renderable = null;
        _complete();
    }
}
