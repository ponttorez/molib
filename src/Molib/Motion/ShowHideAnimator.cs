using System.Diagnostics;
using Gum.Wireframe;

namespace Molib.Motion;

/// <summary>
/// Runs <c>ShowAsync</c> and <c>HideAsync</c>: one animation per element, taken from a pool and turned around by a
/// later call.
/// </summary>
/// <remarks>ADR: motion/show-hide — Repeated calls.</remarks>
internal sealed class ShowHideAnimator
{
    private readonly MotionCore _motion;
    private readonly Dictionary<GraphicalUiElement, ShowHideAnimation> _running =
        new(ReferenceEqualityComparer.Instance);
    private readonly Stack<ShowHideAnimation> _pool = new();

    /// <summary>
    /// Creates the animator.
    /// </summary>
    /// <param name="motion">The motion core that moves the animations.</param>
    /// <param name="diagnostics">Where problems with elements are reported.</param>
    public ShowHideAnimator(MotionCore motion, MolibDiagnostics diagnostics)
    {
        _motion = motion;
        Diagnostics = diagnostics;
    }

    /// <summary>
    /// Where problems with elements are reported.
    /// </summary>
    public MolibDiagnostics Diagnostics { get; }

    /// <summary>
    /// Shows or hides <paramref name="element"/>, turning around its running animation if it has one.
    /// </summary>
    /// <param name="element">Element to show or hide.</param>
    /// <param name="show">Whether to show it.</param>
    /// <param name="preset">Fade, slide and motion.</param>
    /// <param name="then">What to do with the element after hiding.</param>
    /// <param name="cancellationToken">Finishes the animation at once and cancels the task.</param>
    /// <returns>
    /// <see langword="true"/> when the animation ran to its end, <see langword="false"/> when a later call took
    /// over.
    /// </returns>
    /// <remarks>
    /// The interrupted call completes last, when the new one is fully set up: its continuation runs at once and may
    /// call again.
    /// </remarks>
    public Task<bool> Run(
        GraphicalUiElement element,
        bool show,
        ShowHide preset,
        HideAction then,
        CancellationToken cancellationToken)
    {
        if (!_running.TryGetValue(element, out ShowHideAnimation? animation))
        {
            if (element.Visible == show)
            {
                if (!show && then == HideAction.Remove)
                {
                    element.RemoveFromRoot();
                }

                return cancellationToken.IsCancellationRequested
                    ? Task.FromCanceled<bool>(cancellationToken)
                    : Task.FromResult(true);
            }

            animation = _pool.Count > 0 ? _pool.Pop() : new ShowHideAnimation(this);
            animation.Begin(element, element.Visible, preset);
            _running.Add(element, animation);
        }

        var call = new ShowHideCall(cancellationToken);
        ShowHideCall? interrupted = animation.Retarget(show, preset, then, call);
        if (preset.Motion.Kind == MotionKind.Instant || _motion.IsInstant || call.IsCancelRequested)
        {
            _motion.Stop(animation.Progress);
            Complete(animation);
        }
        else
        {
            _motion.Start(animation.Progress, preset.Motion);
        }

        interrupted?.Complete(false);
        return call.Task;
    }

    /// <summary>
    /// Ends <paramref name="animation"/>, returns it to the pool and completes the call that waited for it.
    /// </summary>
    /// <param name="animation">An animation of this animator that reached its end.</param>
    public void Complete(ShowHideAnimation animation)
    {
        Debug.Assert(animation.Element is not null, "Only a running animation completes.");
        _running.Remove(animation.Element);
        ShowHideCall? call = animation.End();
        _pool.Push(animation);
        call?.Complete(true);
    }
}
