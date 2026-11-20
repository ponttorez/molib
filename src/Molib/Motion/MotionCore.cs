using System.Diagnostics;
using System.Globalization;

namespace Molib.Motion;

/// <summary>
/// Moves every running molib animation once per frame: state transitions, showing and hiding, toasts.
/// </summary>
/// <remarks>
/// <para>
/// Named <c>MotionCore</c> rather than <c>Motion</c>: inside the <c>Molib</c> namespace that name would resolve to the
/// <c>Molib.Motion</c> namespace. The service exposes it as <c>MolibService.Motion</c>.
/// </para>
/// <para>
/// Only running animations are in the list, so a quiet frame returns at once. Animations that end in a frame are
/// reported to their owners after the walk over the list, so an owner may start a new animation from the report;
/// it moves from the next frame. ADR: motion/motion-core — Progress.
/// </para>
/// </remarks>
public sealed class MotionCore
{
    private readonly List<MotionProgress> _active = [];
    private readonly List<(MotionProgress Progress, int Generation)> _finished = [];
    private int _instantDepth;

    internal MotionCore()
    {
    }

    /// <summary>
    /// Multiplier of the frame time for every molib animation, for debugging: 1 by default, 0.25 is four times
    /// slower, 0 pauses.
    /// </summary>
    /// <remarks>Applied after the frame time is limited to 0.1 s. ADR: motion/motion-core — Time.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a number.</exception>
    public float TimeScale
    {
        get;
        set
        {
            if (!float.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    string.Create(CultureInfo.InvariantCulture, $"TimeScale must be 0 or more; got {value}."));
            }

            field = value;
        }
    } = 1f;

    /// <summary>
    /// Number of animations running now.
    /// </summary>
    public int ActiveCount { get; private set; }

    /// <summary>
    /// Whether owners must apply values at once instead of starting animations: inside
    /// <see cref="BeginInstant"/> or after the service was disposed.
    /// </summary>
    internal bool IsInstant => _instantDepth > 0 || IsShutDown;

    private bool IsShutDown { get; set; }

    /// <summary>
    /// Starts a stretch of code where owners apply values without animation; end it with <see cref="EndInstant"/>.
    /// </summary>
    /// <remarks>ADR: motion/motion-core — Without animation.</remarks>
    internal void BeginInstant() => _instantDepth++;

    /// <summary>
    /// Ends the stretch started by <see cref="BeginInstant"/>.
    /// </summary>
    internal void EndInstant()
    {
        Debug.Assert(_instantDepth > 0, "EndInstant without BeginInstant.");
        _instantDepth--;
    }

    /// <summary>
    /// Starts <paramref name="progress"/> over from 0 with <paramref name="preset"/>; it moves from the next update.
    /// </summary>
    /// <param name="progress">Progress owned by the caller.</param>
    /// <param name="preset">A spring or a tween; an owner applies an instant preset itself.</param>
    /// <remarks>After the service is disposed the progress jumps to its end and completes right here.</remarks>
    internal void Start(MotionProgress progress, MotionPreset preset)
    {
        progress.Restart(preset);
        if (IsShutDown)
        {
            progress.Finish();
            progress.Target.ApplyProgress(progress.Progress);
            progress.Target.CompleteMotion();
            return;
        }

        if (!progress.IsRunning)
        {
            progress.IsRunning = true;
            ActiveCount++;
        }

        if (!progress.IsListed)
        {
            progress.IsListed = true;
            _active.Add(progress);
        }
    }

    /// <summary>
    /// Stops <paramref name="progress"/> where it is; its owner gets no end notification.
    /// </summary>
    /// <param name="progress">Progress owned by the caller.</param>
    internal void Stop(MotionProgress progress)
    {
        progress.Invalidate();
        if (progress.IsRunning)
        {
            progress.IsRunning = false;
            ActiveCount--;
        }
    }

    /// <summary>
    /// Counts an animation that its owner moves itself, such as scroll inertia, in <see cref="ActiveCount"/>.
    /// </summary>
    /// <remarks>End it with <see cref="EndOwnMotion"/>. ADR: motion/motion-core — Scrolling.</remarks>
    internal void BeginOwnMotion() => ActiveCount++;

    /// <summary>
    /// Stops counting an animation started with <see cref="BeginOwnMotion"/>.
    /// </summary>
    internal void EndOwnMotion()
    {
        Debug.Assert(ActiveCount > 0, "EndOwnMotion without BeginOwnMotion.");
        ActiveCount--;
    }

    /// <summary>
    /// Moves every running animation by <paramref name="deltaSeconds"/> scaled by <see cref="TimeScale"/>.
    /// </summary>
    /// <param name="deltaSeconds">Frame time, already limited by the service.</param>
    internal void Update(float deltaSeconds)
    {
        if (_active.Count == 0)
        {
            return;
        }

        float scaledSeconds = deltaSeconds * TimeScale;
        int count = _active.Count;
        for (int i = 0; i < count; i++)
        {
            MotionProgress progress = _active[i];
            if (!progress.IsRunning)
            {
                continue;
            }

            int generation = progress.Generation;
            bool isDone = progress.Target.ShouldFinishNow ? progress.Finish() : progress.Advance(scaledSeconds);
            progress.Target.ApplyProgress(progress.Progress);
            if (isDone && progress.IsRunning && progress.Generation == generation)
            {
                progress.IsRunning = false;
                ActiveCount--;
                _finished.Add((progress, generation));
            }
        }

        RemoveStopped();
        NotifyFinished();
    }

    /// <summary>
    /// Finishes every running animation at once and makes every later change apply at once: after the service is
    /// disposed nothing moves animations any more.
    /// </summary>
    /// <remarks>
    /// An animation left halfway would leave its element in a mixed look for good: a color between two states, a
    /// panel half faded with its render target still on. So each one jumps to its end and its owner completes it as
    /// usual: a hide hides or removes, a show-hide task completes, a drag copy is taken away.
    /// ADR: foundation/architecture — Disposal.
    /// </remarks>
    internal void Shutdown()
    {
        IsShutDown = true;
        int count = _active.Count;
        for (int i = 0; i < count; i++)
        {
            MotionProgress progress = _active[i];
            if (!progress.IsRunning)
            {
                continue;
            }

            int generation = progress.Generation;
            progress.Finish();
            progress.Target.ApplyProgress(progress.Progress);
            if (progress.IsRunning && progress.Generation == generation)
            {
                progress.IsRunning = false;
                ActiveCount--;
                _finished.Add((progress, generation));
            }
        }

        RemoveStopped();
        NotifyFinished();
    }

    private void RemoveStopped()
    {
        int kept = 0;
        for (int i = 0; i < _active.Count; i++)
        {
            MotionProgress progress = _active[i];
            if (progress.IsRunning)
            {
                _active[kept++] = progress;
            }
            else
            {
                progress.IsListed = false;
            }
        }

        _active.RemoveRange(kept, _active.Count - kept);
    }

    /// <remarks>
    /// An owner notified earlier in the loop may restart or stop an animation that is still waiting here; the
    /// generation check drops its stale notification.
    /// </remarks>
    private void NotifyFinished()
    {
        try
        {
            for (int i = 0; i < _finished.Count; i++)
            {
                (MotionProgress progress, int generation) = _finished[i];
                if (!progress.IsRunning && progress.Generation == generation)
                {
                    progress.Target.CompleteMotion();
                }
            }
        }
        finally
        {
            _finished.Clear();
        }
    }
}
