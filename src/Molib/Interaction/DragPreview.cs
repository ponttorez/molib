using System.Collections.ObjectModel;
using Gum;
using Gum.DataTypes;
using Gum.GueDeriving;
using Gum.Wireframe;
using Molib.Motion;
using RenderingLibrary.Graphics;

namespace Molib.Interaction;

/// <summary>
/// What the player sees during a drag: the copy that follows the pointer on top of everything, the dimmed source, and
/// the flight of the copy into the target or back to the source.
/// </summary>
/// <remarks>
/// <para>
/// The copy goes into a full-screen container on top of <c>ModalRoot</c>: Gum draws the top of <c>ModalRoot</c>
/// above everything and gives the cursor only to it. The container takes no events, so for the rest of the drag
/// nothing in Gum gets the cursor: no touch scrolling under the finger, no hover, no click on the release.
/// </para>
/// <para>
/// The source is dimmed through its render target and alpha and gets both back afterwards; its place in the layout
/// stays. ADR: interaction/drag-drop — Pointer: mouse and touch, Keyboard and gamepad.
/// </para>
/// </remarks>
internal sealed class DragPreview
{
    private const int CopyAlpha = 200;
    private const int DimmedAlpha = 100;

    private readonly MotionCore _motion;
    private readonly GumService _gum;
    private readonly DragFlight _flight;
    private ContainerRuntime? _host;
    private ContainerRuntime? _copy;
    private float _grabX;
    private float _grabY;
    private InvisibleRenderable? _dimmed;
    private bool _dimmedWasRenderTarget;
    private int _dimmedAlpha;

    /// <summary>
    /// Creates the preview with nothing shown.
    /// </summary>
    /// <param name="motion">The motion core, which runs the flight.</param>
    /// <param name="gum">The Gum service, for <c>ModalRoot</c>.</param>
    public DragPreview(MotionCore motion, GumService gum)
    {
        _motion = motion;
        _gum = gum;
        _flight = new DragFlight(Hide);
    }

    /// <summary>
    /// Whether a copy is in the tree: following the pointer or flying into place.
    /// </summary>
    public bool IsShown => _host is not null;

    /// <summary>
    /// Puts a copy of the source on top of everything, grabbed at the press point.
    /// </summary>
    /// <param name="source">The source of the drag.</param>
    /// <param name="data">The data of the drag, for a custom preview.</param>
    /// <param name="pressX">Canvas X of the press.</param>
    /// <param name="pressY">Canvas Y of the press.</param>
    public void Show(DragRegistration source, object data, float pressX, float pressY)
    {
        GraphicalUiElement visual = source.Visual;
        _grabX = pressX - visual.AbsoluteLeft;
        _grabY = pressY - visual.AbsoluteTop;
        _host = new ContainerRuntime { HasEvents = false, ExposeChildrenEvents = false };
        _host.Dock(Dock.Fill);
        _copy = CreateCopy(source, data);
        _host.AddChild(_copy);
        _gum.ModalRoot.AddChild(_host);
    }

    /// <summary>
    /// Moves the copy so that the grab point is under the pointer.
    /// </summary>
    /// <param name="x">Canvas X of the pointer.</param>
    /// <param name="y">Canvas Y of the pointer.</param>
    public void MoveTo(float x, float y)
    {
        if (_copy is not null)
        {
            _copy.X = x - _grabX;
            _copy.Y = y - _grabY;
        }
    }

    /// <summary>
    /// Sends the copy, if there is one, into <paramref name="destination"/> and fades it on the way.
    /// </summary>
    /// <param name="destination">The target, or the source when the drag was cancelled.</param>
    /// <param name="centered">Whether the copy lands in the center of the destination (a target) or on its top left
    /// corner (back to the source).</param>
    public void FlyTo(GraphicalUiElement destination, bool centered)
    {
        if (_copy is not { } copy)
        {
            return;
        }

        float toX = destination.AbsoluteLeft;
        float toY = destination.AbsoluteTop;
        if (centered)
        {
            toX += (destination.AbsoluteWidth - copy.Width) / 2;
            toY += (destination.AbsoluteHeight - copy.Height) / 2;
        }

        _flight.Begin(copy, toX, toY);
        if (_motion.IsInstant)
        {
            Hide();
        }
        else
        {
            _motion.Start(_flight.Progress, MotionPreset.Smooth);
        }
    }

    /// <summary>
    /// Ends a flight at once and takes the copy away; a new drag starts or the service is being disposed.
    /// </summary>
    public void FinishNow()
    {
        if (_host is not null)
        {
            _motion.Stop(_flight.Progress);
            Hide();
        }
    }

    /// <summary>
    /// Dims the source, or hides it, while it is dragged.
    /// </summary>
    /// <param name="visual">The source.</param>
    /// <param name="hide">Whether the source turns fully transparent instead of dimmed.</param>
    public void Dim(GraphicalUiElement visual, bool hide)
    {
        if (visual.RenderableComponent is not InvisibleRenderable renderable)
        {
            return;
        }

        _dimmed = renderable;
        _dimmedWasRenderTarget = renderable.IsRenderTarget;
        _dimmedAlpha = renderable.Alpha;
        renderable.IsRenderTarget = true;
        renderable.Alpha = hide ? 0 : DimmedAlpha;
    }

    /// <summary>
    /// Gives the dimmed source its own render target and alpha back.
    /// </summary>
    public void Undim()
    {
        if (_dimmed is not null)
        {
            _dimmed.IsRenderTarget = _dimmedWasRenderTarget;
            _dimmed.Alpha = _dimmedAlpha;
            _dimmed = null;
        }
    }

    /// <summary>
    /// Copies <paramref name="element"/> with all its children.
    /// </summary>
    /// <remarks>
    /// Checked against Gum 2026.11.2.1: <c>GraphicalUiElement.Clone()</c> copies one element without its children
    /// (a cloned button visual has none), so the tree is copied element by element. The copy is visual only: Gum
    /// drops the Forms control and the event handlers of a clone.
    /// ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    private static GraphicalUiElement CopyTree(GraphicalUiElement element)
    {
        GraphicalUiElement copy = element.Clone();
        ObservableCollection<GraphicalUiElement> children = element.Children;
        for (int i = 0; i < children.Count; i++)
        {
            copy.AddChild(CopyTree(children[i]));
        }

        return copy;
    }

    private static ContainerRuntime CreateCopy(DragRegistration source, object data)
    {
        GraphicalUiElement visual = source.Visual;
        var box = new ContainerRuntime
        {
            HasEvents = false,
            WidthUnits = DimensionUnitType.Absolute,
            Width = visual.AbsoluteWidth,
            HeightUnits = DimensionUnitType.Absolute,
            Height = visual.AbsoluteHeight,
        };
        GraphicalUiElement content;
        if (source.Options.Preview is { } preview)
        {
            content = preview(data);
        }
        else
        {
            content = CopyTree(visual);
            content.XUnits = Gum.Converters.GeneralUnitType.PixelsFromSmall;
            content.YUnits = Gum.Converters.GeneralUnitType.PixelsFromSmall;
            content.XOrigin = HorizontalAlignment.Left;
            content.YOrigin = VerticalAlignment.Top;
            content.X = 0;
            content.Y = 0;
            content.WidthUnits = DimensionUnitType.Absolute;
            content.Width = visual.AbsoluteWidth;
            content.HeightUnits = DimensionUnitType.Absolute;
            content.Height = visual.AbsoluteHeight;
        }

        box.AddChild(content);
        if (box.RenderableComponent is InvisibleRenderable renderable)
        {
            renderable.IsRenderTarget = true;
            renderable.Alpha = CopyAlpha;
        }

        return box;
    }

    private void Hide()
    {
        _host?.RemoveFromRoot();
        _host = null;
        _copy = null;
    }
}
