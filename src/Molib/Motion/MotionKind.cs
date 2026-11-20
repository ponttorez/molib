namespace Molib.Motion;

/// <summary>
/// How a <see cref="MotionPreset"/> moves the progress of an animation from 0 to 1.
/// </summary>
public enum MotionKind
{
    /// <summary>
    /// No animation: the final value is applied at once.
    /// </summary>
    Instant,

    /// <summary>
    /// A damped spring with <see cref="MotionPreset.Stiffness"/> and <see cref="MotionPreset.DampingRatio"/>.
    /// </summary>
    Spring,

    /// <summary>
    /// A Gum curve over <see cref="MotionPreset.Duration"/> seconds.
    /// </summary>
    Tween,
}
