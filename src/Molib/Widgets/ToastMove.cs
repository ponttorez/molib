using Molib.Motion;

namespace Molib.Widgets;

/// <summary>
/// Moves a toast to its place in the stack: its distance from the edge of the safe area.
/// </summary>
/// <remarks>
/// The appearing toast takes its place at once and slides in through show and hide; the others move to make room
/// with the <c>Smooth</c> spring. ADR: widgets/toasts — Animation.
/// </remarks>
internal sealed class ToastMove : IMotionTarget
{
    private readonly MotionCore _motion;
    private readonly Action<float> _apply;
    private float _from;
    private float _to;

    /// <summary>
    /// Creates a move that applies the distance through <paramref name="apply"/>.
    /// </summary>
    /// <param name="motion">The motion core that runs the spring.</param>
    /// <param name="apply">Puts the toast at a distance from the edge, in canvas units.</param>
    public ToastMove(MotionCore motion, Action<float> apply)
    {
        _motion = motion;
        _apply = apply;
        Progress = new MotionProgress(this);
    }

    /// <summary>
    /// The progress the motion core moves.
    /// </summary>
    public MotionProgress Progress { get; }

    /// <summary>
    /// Current distance from the edge.
    /// </summary>
    public float Offset { get; private set; }

    /// <inheritdoc/>
    bool IMotionTarget.ShouldFinishNow => false;

    /// <summary>
    /// Puts the toast at <paramref name="offset"/> without animation.
    /// </summary>
    /// <param name="offset">Distance from the edge.</param>
    public void Jump(float offset)
    {
        _motion.Stop(Progress);
        _from = offset;
        _to = offset;
        Offset = offset;
        _apply(offset);
    }

    /// <summary>
    /// Moves the toast from where it is now to <paramref name="offset"/>.
    /// </summary>
    /// <param name="offset">Distance from the edge.</param>
    public void MoveTo(float offset)
    {
        if (offset == _to)
        {
            return;
        }

        _from = Offset;
        _to = offset;
        _motion.Start(Progress, MotionPreset.Smooth);
    }

    /// <inheritdoc/>
    void IMotionTarget.ApplyProgress(float progress)
    {
        Offset = _from + ((_to - _from) * progress);
        _apply(Offset);
    }

    /// <inheritdoc/>
    void IMotionTarget.CompleteMotion()
    {
    }
}
