using System.Runtime.CompilerServices;
using FlatRedBall.Glue.StateInterpolation;

namespace Molib.Motion;

/// <summary>
/// Describes how an animation moves: a spring, a Gum tween curve, or no animation at all.
/// </summary>
/// <remarks>
/// A preset is immutable: create it once and reuse it. Tween curves are the ones Gum uses for its own animations,
/// so an animation from code looks the same as one from the Gum editor. ADR: motion/motion-core — Presets.
/// </remarks>
public sealed class MotionPreset
{
    private MotionPreset(
        MotionKind kind,
        float stiffness,
        float dampingRatio,
        float duration,
        InterpolationType interpolation,
        Easing easing)
    {
        Kind = kind;
        Stiffness = stiffness;
        DampingRatio = dampingRatio;
        Duration = duration;
        Interpolation = interpolation;
        Easing = easing;
        if (kind == MotionKind.Tween)
        {
            Ease = Tweener.GetInterpolationFunction(interpolation, easing);
        }
    }

    /// <summary>
    /// Critically damped spring for hover, press and focus: noticeable movement in about 0.15 s.
    /// </summary>
    public static MotionPreset Snappy { get; } = Spring(600, 1);

    /// <summary>
    /// Critically damped spring for layers that appear and disappear: noticeable movement in about 0.25 s.
    /// </summary>
    public static MotionPreset Smooth { get; } = Spring(250, 1);

    /// <summary>
    /// Underdamped spring that overshoots its target, for accents such as toasts and badges.
    /// </summary>
    public static MotionPreset Bouncy { get; } = Spring(400, 0.6f);

    /// <summary>
    /// No animation: the final value is applied at once.
    /// </summary>
    public static MotionPreset Instant { get; } =
        new(MotionKind.Instant, 0, 0, 0, InterpolationType.Linear, Easing.Out);

    /// <summary>
    /// How this preset moves the progress.
    /// </summary>
    public MotionKind Kind { get; }

    /// <summary>
    /// Spring stiffness k (mass 1); 0 unless <see cref="Kind"/> is <see cref="MotionKind.Spring"/>.
    /// </summary>
    public float Stiffness { get; }

    /// <summary>
    /// Spring damping ratio ζ: 1 reaches the target fastest without overshoot, below 1 overshoots; 0 unless
    /// <see cref="Kind"/> is <see cref="MotionKind.Spring"/>.
    /// </summary>
    public float DampingRatio { get; }

    /// <summary>
    /// Tween length in seconds; 0 unless <see cref="Kind"/> is <see cref="MotionKind.Tween"/>.
    /// </summary>
    public float Duration { get; }

    /// <summary>
    /// Gum curve of the tween.
    /// </summary>
    public InterpolationType Interpolation { get; }

    /// <summary>
    /// Which end of the Gum curve is eased.
    /// </summary>
    public Easing Easing { get; }

    /// <remarks>
    /// Resolved once: Gum returns a new delegate from every <c>Tweener.GetInterpolationFunction</c> call, so it
    /// must not be called per frame. ADR: motion/motion-core — Tween.
    /// </remarks>
    internal TweeningFunction? Ease { get; }

    /// <summary>
    /// Creates a spring preset.
    /// </summary>
    /// <param name="stiffness">Stiffness k, greater than 0. Higher is faster.</param>
    /// <param name="dampingRatio">Damping ratio ζ, greater than 0: 1 without overshoot, below 1 with it.</param>
    /// <returns>The preset.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is not a positive number.</exception>
    public static MotionPreset Spring(float stiffness, float dampingRatio)
    {
        ThrowIfNotPositive(stiffness);
        ThrowIfNotPositive(dampingRatio);
        return new MotionPreset(MotionKind.Spring, stiffness, dampingRatio, 0, InterpolationType.Linear, Easing.Out);
    }

    /// <summary>
    /// Creates a tween preset with a Gum curve.
    /// </summary>
    /// <param name="duration">Length in seconds, greater than 0.</param>
    /// <param name="interpolation">Gum curve.</param>
    /// <param name="easing">Which end of the curve is eased.</param>
    /// <returns>The preset.</returns>
    /// <remarks><c>Back</c> and <c>Elastic</c> leave the 0–1 range, like an overshooting spring.</remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="duration"/> is not a positive number.</exception>
    public static MotionPreset Tween(float duration, InterpolationType interpolation, Easing easing)
    {
        ThrowIfNotPositive(duration);
        return new MotionPreset(MotionKind.Tween, 0, 0, duration, interpolation, easing);
    }

    private static void ThrowIfNotPositive(float value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "The value must be a positive number.");
        }
    }
}
