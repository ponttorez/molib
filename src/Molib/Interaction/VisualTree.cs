using Gum.Forms.Controls;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// Questions about the Gum tree that Gum has no public API for: ancestry, the scroll viewer around an element and
/// whether a point of the canvas shows an element.
/// </summary>
internal static class VisualTree
{
    /// <summary>
    /// Whether <paramref name="element"/> is <paramref name="root"/> or lies inside it.
    /// </summary>
    /// <param name="element">Element to check.</param>
    /// <param name="root">The possible ancestor.</param>
    /// <returns><see langword="true"/> when <paramref name="root"/> is on the parent chain of
    /// <paramref name="element"/>, the element itself included.</returns>
    public static bool IsInside(GraphicalUiElement? element, GraphicalUiElement root)
    {
        for (GraphicalUiElement? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The nearest scroll viewer whose content holds <paramref name="element"/>.
    /// </summary>
    /// <param name="element">Element inside the content of a scroll viewer, or anywhere else.</param>
    /// <returns>The scroll viewer, or <see langword="null"/> when no ancestor is one.</returns>
    public static ScrollViewer? FindScrollViewer(GraphicalUiElement element)
    {
        for (GraphicalUiElement? current = element.Parent; current is not null; current = current.Parent)
        {
            if (current is InteractiveGue { FormsControlAsObject: ScrollViewer viewer }
                && ReferenceEquals(viewer.Visual, current))
            {
                return viewer;
            }
        }

        return null;
    }

    /// <summary>
    /// The child of <paramref name="parent"/> that holds <paramref name="element"/>.
    /// </summary>
    /// <param name="element">Element somewhere inside <paramref name="parent"/>.</param>
    /// <param name="parent">The container.</param>
    /// <returns>The direct child of <paramref name="parent"/> on the parent chain of <paramref name="element"/>,
    /// or <see langword="null"/> when the element is not inside it.</returns>
    public static GraphicalUiElement? ChildHolding(GraphicalUiElement element, GraphicalUiElement parent)
    {
        for (GraphicalUiElement? current = element; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, parent))
            {
                return current;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the canvas point lies inside the bounds of <paramref name="element"/>.
    /// </summary>
    /// <param name="element">Element to test.</param>
    /// <param name="x">Horizontal canvas position.</param>
    /// <param name="y">Vertical canvas position.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public static bool Contains(GraphicalUiElement element, float x, float y) =>
        x >= element.AbsoluteLeft && x < element.AbsoluteRight && y >= element.AbsoluteTop
        && y < element.AbsoluteBottom;

    /// <summary>
    /// Whether the canvas point shows <paramref name="element"/>: the element and its ancestors are visible, the
    /// point is inside its bounds and inside every ancestor that clips its children.
    /// </summary>
    /// <param name="element">Element to test.</param>
    /// <param name="x">Horizontal canvas position.</param>
    /// <param name="y">Vertical canvas position.</param>
    /// <returns><see langword="true"/> when the element is drawn at the point.</returns>
    /// <remarks>
    /// The clipping check matters for the content of a scroll viewer: a row scrolled out of view keeps its bounds,
    /// but its clip container hides it. ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    public static bool ShowsAt(GraphicalUiElement element, float x, float y)
    {
        if (!element.AbsoluteVisible || !Contains(element, x, y))
        {
            return false;
        }

        for (GraphicalUiElement? current = element.Parent; current is not null; current = current.Parent)
        {
            if (current.ClipsChildren && !Contains(current, x, y))
            {
                return false;
            }
        }

        return true;
    }
}
