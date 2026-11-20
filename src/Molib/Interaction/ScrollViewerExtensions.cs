using Gum.Forms.Controls;

namespace Molib.Interaction;

/// <summary>
/// Touch inertia and snapping for Gum scroll viewers and everything built on them: <c>ListBox</c>,
/// <c>ItemsControl</c>, <c>ComboBox</c>.
/// </summary>
/// <remarks>ADR: interaction/scrolling — Attaching.</remarks>
public static class ScrollViewerExtensions
{
    /// <summary>
    /// Makes <paramref name="viewer"/> coast after a fling with a finger and, with
    /// <see cref="InertiaOptions.Snap"/>, come to rest on an item or a page.
    /// </summary>
    /// <param name="viewer">The scroll viewer.</param>
    /// <param name="molib">The molib service of the game.</param>
    /// <param name="options">How it coasts and snaps; no snapping and <c>Friction</c> 4 when omitted.</param>
    /// <returns>The inertia of the viewer: the snapped item and its change event.</returns>
    /// <remarks>
    /// With <see cref="ScrollSnap.Pages"/> the viewer is also a paging area: LB / RB and Ctrl+PageUp /
    /// Ctrl+PageDown turn it while the focus is inside. ADR: interaction/scrolling — Snapping.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The viewer already uses inertia.</exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public static ScrollInertia UseInertia(this ScrollViewer viewer, MolibService molib, InertiaOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(molib);
        return molib.Scrolling.Attach(viewer, options ?? new InertiaOptions());
    }
}
