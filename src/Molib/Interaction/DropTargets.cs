using System.Runtime.CompilerServices;
using Gum;
using Gum.Forms.Controls;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// The targets a pointer drag may end on and the scroll viewers that scroll while the pointer rests at their edge,
/// collected once when the drag starts.
/// </summary>
/// <remarks>
/// While the copy is on top of <c>ModalRoot</c> Gum reports nothing under the cursor, so molib finds the target under
/// the pointer itself. Collecting the reachable targets once per drag keeps the frames of the drag free of a walk over
/// every registration. ADR: interaction/drag-drop — Pointer: mouse and touch, Scrolling during a drag.
/// </remarks>
internal sealed class DropTargets
{
    private const float EdgeBand = 32;
    private const float EdgeSpeed = 600;

    private readonly GumService _gum;
    private readonly List<DragRegistration> _targets = [];
    private readonly List<ScrollViewer> _scrollAreas = [];
    private GraphicalUiElement? _modal;

    /// <summary>
    /// Creates the empty list.
    /// </summary>
    /// <param name="gum">The Gum service, for its roots.</param>
    public DropTargets(GumService gum)
    {
        _gum = gum;
    }

    /// <summary>
    /// Lists the targets reachable now and the scroll viewers around them and around the source.
    /// </summary>
    /// <param name="registrations">Every registration of the drag and drop module.</param>
    /// <param name="source">The source of the drag.</param>
    /// <param name="modal">The modal window on top when the drag starts, or <see langword="null"/>: only targets
    /// inside it are reachable, as Gum's own input would be.</param>
    public void Collect(
        ConditionalWeakTable<GraphicalUiElement, DragRegistration> registrations,
        DragRegistration source,
        GraphicalUiElement? modal)
    {
        Clear();
        _modal = modal;
        foreach (KeyValuePair<GraphicalUiElement, DragRegistration> pair in registrations)
        {
            DragRegistration registration = pair.Value;
            if (registration.CanDrop is null || (modal is not null && !VisualTree.IsInside(pair.Key, modal)))
            {
                continue;
            }

            _targets.Add(registration);
            AddScrollAreas(pair.Key);
        }

        AddScrollAreas(source.Visual);
    }

    /// <summary>
    /// The listed target drawn under the canvas point; the innermost one when targets are nested.
    /// </summary>
    /// <param name="x">Canvas X of the pointer.</param>
    /// <param name="y">Canvas Y of the pointer.</param>
    /// <param name="source">The source, which is never a target of its own drag.</param>
    /// <returns>The target, or <see langword="null"/>.</returns>
    /// <remarks>
    /// A target counts when it is visible, the point is inside its bounds and inside every clipping ancestor (a row
    /// scrolled out of a viewer is not a target), and it is still inside the modal window or the root it was in.
    /// ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    public DragRegistration? HitTest(float x, float y, DragRegistration source)
    {
        DragRegistration? best = null;
        for (int i = 0; i < _targets.Count; i++)
        {
            DragRegistration candidate = _targets[i];
            if (ReferenceEquals(candidate, source) || !VisualTree.ShowsAt(candidate.Visual, x, y)
                || !IsReachable(candidate.Visual))
            {
                continue;
            }

            if (best is null || VisualTree.IsInside(candidate.Visual, best.Visual))
            {
                best = candidate;
            }
        }

        return best;
    }

    /// <summary>
    /// Scrolls the listed viewers whose edge the pointer rests at.
    /// </summary>
    /// <param name="x">Canvas X of the pointer.</param>
    /// <param name="y">Canvas Y of the pointer.</param>
    /// <param name="deltaSeconds">Real frame time.</param>
    /// <remarks>
    /// The speed grows from 0 at the inner side of a 32-unit band to 600 units/s at the edge.
    /// ADR: interaction/drag-drop — Scrolling during a drag.
    /// </remarks>
    public void ScrollAtEdges(float x, float y, float deltaSeconds)
    {
        for (int i = 0; i < _scrollAreas.Count; i++)
        {
            ScrollViewer viewer = _scrollAreas[i];
            GraphicalUiElement clip = viewer.ClipContainer;
            if (!clip.AbsoluteVisible || !VisualTree.Contains(clip, x, y))
            {
                continue;
            }

            float vertical = viewer.VerticalScrollBarMaximum > 0
                ? EdgeSpeedFor(y - clip.AbsoluteTop, clip.AbsoluteBottom - y)
                : 0;
            if (vertical != 0)
            {
                viewer.VerticalScrollBarValue += vertical * deltaSeconds;
            }

            float horizontal = viewer.HorizontalScrollBarMaximum > 0
                ? EdgeSpeedFor(x - clip.AbsoluteLeft, clip.AbsoluteRight - x)
                : 0;
            if (horizontal != 0)
            {
                viewer.HorizontalScrollBarValue += horizontal * deltaSeconds;
            }
        }
    }

    /// <summary>
    /// Takes a registration off the list: its element stopped being a target during the drag.
    /// </summary>
    /// <param name="registration">The registration.</param>
    public void Remove(DragRegistration registration) => _targets.Remove(registration);

    /// <summary>
    /// Empties the lists; the drag is over.
    /// </summary>
    public void Clear()
    {
        _targets.Clear();
        _scrollAreas.Clear();
        _modal = null;
    }

    private static float EdgeSpeedFor(float fromStart, float fromEnd)
    {
        if (fromStart < EdgeBand)
        {
            return -EdgeSpeed * (1 - (fromStart / EdgeBand));
        }

        return fromEnd < EdgeBand ? EdgeSpeed * (1 - (fromEnd / EdgeBand)) : 0;
    }

    private bool IsReachable(GraphicalUiElement element)
    {
        if (_modal is not null)
        {
            return VisualTree.IsInside(element, _modal);
        }

        GraphicalUiElement top = element;
        while (top.Parent is not null)
        {
            top = top.Parent;
        }

        return ReferenceEquals(top, _gum.Root) || ReferenceEquals(top, _gum.PopupRoot);
    }

    private void AddScrollAreas(GraphicalUiElement element)
    {
        for (ScrollViewer? viewer = VisualTree.FindScrollViewer(element);
            viewer is not null;
            viewer = VisualTree.FindScrollViewer(viewer.Visual))
        {
            if (!_scrollAreas.Contains(viewer))
            {
                _scrollAreas.Add(viewer);
            }
        }
    }
}
