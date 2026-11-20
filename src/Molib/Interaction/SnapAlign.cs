namespace Molib.Interaction;

/// <summary>
/// Which point of a child meets the same point of the visible area when a scroll viewer snaps to items.
/// </summary>
/// <remarks>ADR: interaction/scrolling — Snapping.</remarks>
public enum SnapAlign
{
    /// <summary>The leading edges: top, or left for horizontal scrolling.</summary>
    Start,

    /// <summary>The centers.</summary>
    Center,

    /// <summary>The trailing edges: bottom, or right for horizontal scrolling.</summary>
    End,
}
