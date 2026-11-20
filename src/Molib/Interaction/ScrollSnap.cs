namespace Molib.Interaction;

/// <summary>
/// Where a scroll viewer with inertia comes to rest.
/// </summary>
/// <remarks>ADR: interaction/scrolling — Snapping.</remarks>
public enum ScrollSnap
{
    /// <summary>Anywhere: the content stops where the inertia ends.</summary>
    None,

    /// <summary>At a child of the inner panel, aligned by <see cref="SnapAlign"/>.</summary>
    Items,

    /// <summary>At a page boundary; a page is as large as the visible area.</summary>
    Pages,
}
