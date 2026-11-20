namespace Molib.Motion;

/// <summary>
/// Owner of a <see cref="MotionProgress"/>: receives the progress each frame and the end of the animation.
/// </summary>
/// <remarks>
/// The core does not know what is animated; it only moves the progress and calls the owner.
/// ADR: motion/motion-core — Decision.
/// </remarks>
internal interface IMotionTarget
{
    /// <summary>
    /// Whether the animation must jump to its end in this frame, for example because its call was cancelled.
    /// </summary>
    public bool ShouldFinishNow { get; }

    /// <summary>
    /// Applies the progress of this frame: 0 is the start, 1 the target; a spring or a curve may overshoot 1.
    /// </summary>
    /// <param name="progress">Current progress.</param>
    public void ApplyProgress(float progress);

    /// <summary>
    /// Called once after the last <see cref="ApplyProgress"/> with 1, after the core finished walking its list:
    /// the owner may start a new animation here.
    /// </summary>
    public void CompleteMotion();
}
