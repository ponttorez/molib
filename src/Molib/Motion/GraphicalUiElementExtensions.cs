using Gum.Wireframe;

namespace Molib.Motion;

/// <summary>
/// Animated showing and hiding of any Gum element.
/// </summary>
/// <remarks>
/// In Gum <c>Visible = false</c> and removal from the parent take effect at once, so the hiding animation has to
/// play before them. These methods play it and hide or remove the element after it. ADR: motion/show-hide — Decision.
/// </remarks>
public static class GraphicalUiElementExtensions
{
    /// <summary>
    /// Makes <paramref name="element"/> visible and animates it from the hidden pose of <paramref name="preset"/>.
    /// </summary>
    /// <param name="element">Element to show; for a Forms control, its <c>Visual</c>.</param>
    /// <param name="molib">The molib service of the game.</param>
    /// <param name="preset">How the element appears, such as <see cref="ShowHide.FadeSlideUp"/>.</param>
    /// <param name="cancellationToken">Finishes the animation at once and cancels the task.</param>
    /// <returns>
    /// A task that completes on the game thread inside <c>MolibService.Update</c>: <see langword="true"/> when the
    /// animation ran to its end or the element was already shown, <see langword="false"/> when a later
    /// <c>ShowAsync</c> or <c>HideAsync</c> of the same element took over.
    /// </returns>
    /// <remarks>
    /// A call during a running animation of the element turns it around from the current values.
    /// ADR: motion/show-hide — API, Repeated calls.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public static Task<bool> ShowAsync(
        this GraphicalUiElement element,
        MolibService molib,
        ShowHide preset,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(molib);
        ArgumentNullException.ThrowIfNull(preset);
        return molib.ShowHideAnimator.Run(element, show: true, preset, HideAction.Hide, cancellationToken);
    }

    /// <summary>
    /// Animates <paramref name="element"/> to the hidden pose of <paramref name="preset"/>, then hides or removes
    /// it and puts back the values it had, so the next <see cref="ShowAsync"/> starts from them.
    /// </summary>
    /// <param name="element">Element to hide; for a Forms control, its <c>Visual</c>.</param>
    /// <param name="molib">The molib service of the game.</param>
    /// <param name="preset">How the element disappears, such as <see cref="ShowHide.FadeSlideUp"/>.</param>
    /// <param name="then">Whether to hide the element (default) or remove it from its parent.</param>
    /// <param name="cancellationToken">Finishes the animation at once and cancels the task.</param>
    /// <returns>
    /// A task that completes on the game thread inside <c>MolibService.Update</c>: <see langword="true"/> when the
    /// element was hidden or removed, <see langword="false"/> when a later <c>ShowAsync</c> or <c>HideAsync</c> of
    /// the same element took over and nothing was hidden.
    /// </returns>
    /// <remarks>
    /// While the animation runs, the element does not take mouse and touch input: molib turns off its
    /// <c>HasEvents</c> and <c>ExposeChildrenEvents</c>. Keyboard and gamepad focus stay as they are.
    /// ADR: motion/show-hide — Input while hiding.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public static Task<bool> HideAsync(
        this GraphicalUiElement element,
        MolibService molib,
        ShowHide preset,
        HideAction then = HideAction.Hide,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(molib);
        ArgumentNullException.ThrowIfNull(preset);
        return molib.ShowHideAnimator.Run(element, show: false, preset, then, cancellationToken);
    }
}
