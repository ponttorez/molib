using System.Collections.ObjectModel;
using Gum.Forms.Controls;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// Where a scroll viewer may come to rest: the snap points of its items or pages, computed from the layout of the
/// inner panel at the moment they are asked for.
/// </summary>
/// <remarks>
/// Points are found when a target is chosen, not every frame. ADR: interaction/scrolling — Snapping.
/// </remarks>
/// <param name="viewer">The scroll viewer.</param>
/// <param name="options">The snap mode and alignment.</param>
internal sealed class SnapPoints(ScrollViewer viewer, InertiaOptions options)
{
    private const double Tolerance = 0.001;

    /// <summary>
    /// The axis that snaps: the vertical one when it has a range, otherwise the horizontal one.
    /// </summary>
    /// <param name="vertical">Whether the snapping axis is the vertical one.</param>
    /// <returns>Whether either axis has a range.</returns>
    public bool TryGetMainAxis(out bool vertical)
    {
        vertical = viewer.VerticalScrollBarMaximum > 0;
        return vertical || viewer.HorizontalScrollBarMaximum > 0;
    }

    /// <summary>
    /// The size of the visible area along an axis: the length of one page.
    /// </summary>
    /// <param name="vertical">Whether the vertical axis is asked for.</param>
    /// <returns>Canvas units.</returns>
    public float Viewport(bool vertical) =>
        vertical ? viewer.ClipContainer.AbsoluteHeight : viewer.ClipContainer.AbsoluteWidth;

    /// <summary>
    /// Index of the last page along an axis; the last page may be shorter than the viewport.
    /// </summary>
    /// <param name="vertical">Whether the vertical axis is asked for.</param>
    /// <returns>The index, 0 for an area without a range.</returns>
    public int LastPage(bool vertical)
    {
        float page = Viewport(vertical);
        return page <= 0 ? 0 : (int)Math.Ceiling((Maximum(vertical) / page) - Tolerance);
    }

    /// <summary>
    /// The scroll value of the start of a page, limited by the range of the bar.
    /// </summary>
    /// <param name="vertical">Whether the vertical axis is asked for.</param>
    /// <param name="index">Index of the page.</param>
    /// <returns>The scroll value.</returns>
    public double PagePoint(bool vertical, int index) =>
        Math.Min(index * (double)Viewport(vertical), Maximum(vertical));

    /// <summary>
    /// The snap point nearest to <paramref name="predicted"/>.
    /// </summary>
    /// <param name="vertical">Whether the vertical axis snaps.</param>
    /// <param name="predicted">The scroll value to snap.</param>
    /// <param name="point">The scroll value of the snap point.</param>
    /// <param name="nearPage">With <see cref="ScrollSnap.Pages"/>, a page the result stays at most one page away
    /// from; <c>-1</c> for no limit.</param>
    /// <returns>Index of the item or page, or <c>-1</c> when the inner panel has no visible children.</returns>
    /// <remarks>
    /// With <paramref name="nearPage"/> a fling turns at most one page from the page under the finger at the press.
    /// ADR: interaction/scrolling — Snapping.
    /// </remarks>
    public int Nearest(bool vertical, double predicted, out double point, int nearPage = -1)
    {
        double maximum = Maximum(vertical);
        if (options.Snap == ScrollSnap.Pages)
        {
            float page = Viewport(vertical);
            int last = LastPage(vertical);
            int pageIndex = page <= 0 ? 0 : Math.Clamp((int)Math.Round(predicted / page), 0, last);
            if (nearPage >= 0)
            {
                pageIndex = Math.Clamp(pageIndex, Math.Max(0, nearPage - 1), Math.Min(last, nearPage + 1));
            }

            point = PagePoint(vertical, pageIndex);
            return pageIndex;
        }

        int best = -1;
        int visibleIndex = 0;
        double bestDistance = double.MaxValue;
        point = Math.Clamp(predicted, 0, maximum);
        ObservableCollection<GraphicalUiElement> children = viewer.InnerPanel.Children;
        for (int i = 0; i < children.Count; i++)
        {
            GraphicalUiElement child = children[i];
            if (!child.Visible)
            {
                continue;
            }

            double candidate = ItemPoint(child, vertical, maximum);
            double distance = Math.Abs(candidate - predicted);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = visibleIndex;
                point = candidate;
            }

            visibleIndex++;
        }

        return best;
    }

    /// <summary>
    /// The snap point of the item or page that holds <paramref name="element"/>.
    /// </summary>
    /// <param name="element">An element inside the inner panel.</param>
    /// <param name="vertical">Whether the vertical axis snaps.</param>
    /// <param name="point">The scroll value of the snap point.</param>
    /// <returns>Index of the item or page, or <c>-1</c> when the element is not in the inner panel.</returns>
    public int Of(GraphicalUiElement element, bool vertical, out double point)
    {
        point = 0;
        GraphicalUiElement panel = viewer.InnerPanel;
        GraphicalUiElement? item = VisualTree.ChildHolding(element, panel);
        if (item is null)
        {
            return -1;
        }

        if (options.Snap == ScrollSnap.Pages)
        {
            float page = Viewport(vertical);
            float center = vertical
                ? element.AbsoluteTop - panel.AbsoluteTop + (element.AbsoluteHeight / 2)
                : element.AbsoluteLeft - panel.AbsoluteLeft + (element.AbsoluteWidth / 2);
            int index = page <= 0 ? 0 : Math.Clamp((int)(center / page), 0, LastPage(vertical));
            point = PagePoint(vertical, index);
            return index;
        }

        int visibleIndex = 0;
        ObservableCollection<GraphicalUiElement> children = panel.Children;
        for (int i = 0; i < children.Count; i++)
        {
            if (ReferenceEquals(children[i], item))
            {
                point = ItemPoint(item, vertical, Maximum(vertical));
                return visibleIndex;
            }

            if (children[i].Visible)
            {
                visibleIndex++;
            }
        }

        return -1;
    }

    private double Maximum(bool vertical) =>
        vertical ? viewer.VerticalScrollBarMaximum : viewer.HorizontalScrollBarMaximum;

    private double ItemPoint(GraphicalUiElement item, bool vertical, double maximum)
    {
        GraphicalUiElement panel = viewer.InnerPanel;
        float start = vertical ? item.AbsoluteTop - panel.AbsoluteTop : item.AbsoluteLeft - panel.AbsoluteLeft;
        float size = vertical ? item.AbsoluteHeight : item.AbsoluteWidth;
        float viewport = Viewport(vertical);
        float point = options.SnapAlign switch
        {
            SnapAlign.Center => start + (size / 2) - (viewport / 2),
            SnapAlign.End => start + size - viewport,
            _ => start,
        };
        return Math.Clamp(point, 0, maximum);
    }
}
