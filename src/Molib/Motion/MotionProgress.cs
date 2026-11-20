using System.Diagnostics;

namespace Molib.Motion;

/// <summary>
/// Progress of one animation from 0 to 1, moved by a spring or a tween; owned and reused by its target.
/// </summary>
/// <remarks>
/// <para>
/// The spring is the damped oscillator <c>x'' = -k·x - c·x'</c> with <c>x = 1 - p</c>, mass 1 and
/// <c>c = 2ζ√k</c>, solved exactly for any time step in each of its three regimes, so the result does not depend on
/// the frame rate. It rests when <c>|x| &lt; 0.001</c> and <c>|v| &lt; 0.01</c>; then <c>p = 1</c> exactly.
/// </para>
/// <para>ADR: motion/motion-core — Spring, Tween.</para>
/// </remarks>
internal sealed class MotionProgress
{
    private const float RestDistance = 0.001f;
    private const float RestSpeed = 0.01f;
    private const float CriticalTolerance = 0.001f;

    private float _elapsed;
    private float _velocity;

    /// <summary>
    /// Creates an idle progress for <paramref name="target"/>.
    /// </summary>
    /// <param name="target">Receives the progress each frame.</param>
    public MotionProgress(IMotionTarget target)
    {
        Target = target;
        Preset = MotionPreset.Instant;
        Progress = 1;
    }

    /// <summary>
    /// Receives the progress each frame and the end of the animation.
    /// </summary>
    public IMotionTarget Target { get; }

    /// <summary>
    /// How the current animation moves.
    /// </summary>
    public MotionPreset Preset { get; private set; }

    /// <summary>
    /// Current progress: 0 at the start, 1 at the target.
    /// </summary>
    public float Progress { get; private set; }

    /// <summary>
    /// Whether the core is moving this progress; set by <see cref="MotionCore"/>.
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Whether the progress is in the active list of <see cref="MotionCore"/>; set by the core.
    /// </summary>
    public bool IsListed { get; set; }

    /// <summary>
    /// Grows on every start and stop, so the core can tell a finished animation from a restarted one.
    /// </summary>
    public int Generation { get; private set; }

    /// <summary>
    /// Starts over from <c>p = 0</c> at rest with <paramref name="preset"/>.
    /// </summary>
    /// <param name="preset">A spring or a tween.</param>
    /// <remarks>The velocity is not carried over. ADR: motion/motion-core — Retargeting.</remarks>
    public void Restart(MotionPreset preset)
    {
        Debug.Assert(preset.Kind != MotionKind.Instant, "An instant preset never starts an animation.");
        Preset = preset;
        Progress = 0;
        _velocity = 0;
        _elapsed = 0;
        Generation++;
    }

    /// <summary>
    /// Marks the progress as stopped without an end, so a pending end notification is dropped.
    /// </summary>
    public void Invalidate() => Generation++;

    /// <summary>
    /// Jumps to the target.
    /// </summary>
    /// <returns>Always <see langword="true"/>: the animation is over.</returns>
    public bool Finish()
    {
        Progress = 1;
        _velocity = 0;
        return true;
    }

    /// <summary>
    /// Moves the progress by <paramref name="deltaSeconds"/>.
    /// </summary>
    /// <param name="deltaSeconds">Scaled frame time; 0 keeps the progress where it is.</param>
    /// <returns>Whether the animation reached its target and rests there.</returns>
    public bool Advance(float deltaSeconds)
    {
        if (Preset.Kind == MotionKind.Tween)
        {
            _elapsed += deltaSeconds;
            if (_elapsed >= Preset.Duration)
            {
                return Finish();
            }

            Debug.Assert(Preset.Ease is not null, "A tween preset resolves its curve when it is created.");
            Progress = Preset.Ease(_elapsed / Preset.Duration, 0, 1, 1);
            return false;
        }

        float offset = 1 - Progress;
        float speed = -_velocity;
        StepSpring(Preset.Stiffness, Preset.DampingRatio, deltaSeconds, ref offset, ref speed);
        if (MathF.Abs(offset) < RestDistance && MathF.Abs(speed) < RestSpeed)
        {
            return Finish();
        }

        Progress = 1 - offset;
        _velocity = -speed;
        return false;
    }

    /// <summary>
    /// Advances <c>x'' = -k·x - c·x'</c> exactly by <paramref name="time"/>.
    /// </summary>
    /// <param name="stiffness">k.</param>
    /// <param name="dampingRatio">ζ.</param>
    /// <param name="time">Step in seconds.</param>
    /// <param name="offset">Distance from the target x, updated in place.</param>
    /// <param name="speed">Its derivative x', updated in place.</param>
    /// <remarks>Scroll snapping runs the same spring on the scroll value itself, with the velocity of the fling.
    /// ADR: motion/motion-core — Scrolling.</remarks>
    internal static void StepSpring(float stiffness, float dampingRatio, float time, ref float offset, ref float speed)
    {
        float omega = MathF.Sqrt(stiffness);
        float x = offset;
        float v = speed;

        if (dampingRatio < 1 - CriticalTolerance)
        {
            float decayRate = dampingRatio * omega;
            float dampedOmega = omega * MathF.Sqrt(1 - (dampingRatio * dampingRatio));
            float sineAmount = (v + (decayRate * x)) / dampedOmega;
            float decay = MathF.Exp(-decayRate * time);
            float cosine = MathF.Cos(dampedOmega * time);
            float sine = MathF.Sin(dampedOmega * time);
            float wave = (x * cosine) + (sineAmount * sine);
            offset = decay * wave;
            speed = decay * ((dampedOmega * ((sineAmount * cosine) - (x * sine))) - (decayRate * wave));
        }
        else if (dampingRatio > 1 + CriticalTolerance)
        {
            float spread = omega * MathF.Sqrt((dampingRatio * dampingRatio) - 1);
            float slowRate = (-dampingRatio * omega) + spread;
            float fastRate = (-dampingRatio * omega) - spread;
            float fastAmount = (v - (slowRate * x)) / (fastRate - slowRate);
            float slowAmount = x - fastAmount;
            float slowDecay = MathF.Exp(slowRate * time);
            float fastDecay = MathF.Exp(fastRate * time);
            offset = (slowAmount * slowDecay) + (fastAmount * fastDecay);
            speed = (slowAmount * slowRate * slowDecay) + (fastAmount * fastRate * fastDecay);
        }
        else
        {
            float linearAmount = v + (omega * x);
            float decay = MathF.Exp(-omega * time);
            offset = (x + (linearAmount * time)) * decay;
            speed = (v - (omega * linearAmount * time)) * decay;
        }
    }
}
