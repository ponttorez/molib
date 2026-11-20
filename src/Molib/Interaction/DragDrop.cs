using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Gum;
using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Input;
using Gum.Wireframe;
using Microsoft.Xna.Framework;
using Molib.Integration;
using Molib.Motion;
using RenderingLibrary.Graphics;
using Keys = Gum.Forms.Input.Keys;

namespace Molib.Interaction;

/// <summary>
/// Drag and drop between containers: an item from the inventory into an equipment slot, an ability onto the quick
/// bar. With the mouse and touch the player drags a copy; with a gamepad or the keyboard the player picks the item
/// and puts it with one button.
/// </summary>
/// <remarks>
/// <para>
/// Gum gives the raw parts only: the cursor knows what was pressed and what is under it, and an element gets
/// <c>Dragging</c>. molib builds the whole gesture from them. What is dragged is an object of the game: molib does
/// not look into it, and the game decides whether a target takes it.
/// </para>
/// <para>
/// Registrations are kept in a weak table: an element the game drops goes away together with its registration, and
/// an element out of the tree is never under the pointer. ADR: interaction/drag-drop — Decision, API.
/// </para>
/// </remarks>
public sealed class DragDrop
{
    /// <summary>
    /// Name of the state category a target shows the drag with.
    /// </summary>
    public const string DropTargetCategory = "DropTarget";

    /// <summary>
    /// State of <see cref="DropTargetCategory"/> when no drag is over the target.
    /// </summary>
    public const string NoneState = "None";

    /// <summary>
    /// State of <see cref="DropTargetCategory"/> when the dragged data is over the target and the target takes it.
    /// </summary>
    public const string AcceptState = "Accept";

    /// <summary>
    /// State of <see cref="DropTargetCategory"/> when the dragged data is over the target and the target refuses it.
    /// </summary>
    public const string RejectState = "Reject";

    private const float HoldToDragSeconds = 0.5f;
    private const int CopyAlpha = 200;
    private const int DimmedAlpha = 100;
    private const float EdgeBand = 32;
    private const float EdgeSpeed = 600;

    private readonly MolibService _molib;
    private readonly Game _game;
    private readonly GumService _gum;
    private readonly ConditionalWeakTable<GraphicalUiElement, DragRegistration> _registrations = [];
    private readonly List<DragRegistration> _targets = [];
    private readonly List<ScrollViewer> _scrollAreas = [];
    private readonly DragFlight _flight;
    private Phase _phase;
    private DragRegistration? _source;
    private object? _data;
    private DragRegistration? _target;
    private bool _targetAccepts;
    private float _pressX;
    private float _pressY;
    private float _grabX;
    private float _grabY;
    private float _holdSeconds;
    private bool _isTouch;
    private ScrollViewer? _sourceScroll;
    private GraphicalUiElement? _modal;
    private ContainerRuntime? _host;
    private ContainerRuntime? _copy;
    private InvisibleRenderable? _dimmed;
    private bool _dimmedWasRenderTarget;
    private int _dimmedAlpha;

    internal DragDrop(MolibService molib, Game game, GumService gum)
    {
        _molib = molib;
        _game = game;
        _gum = gum;
        _flight = new DragFlight(EndFlight);
    }

    /// <summary>
    /// Where the drag is.
    /// </summary>
    private enum Phase
    {
        /// <summary>No drag.</summary>
        Idle,

        /// <summary>The pointer is down on a source and has not moved far enough yet.</summary>
        Pending,

        /// <summary>The pointer drags a copy.</summary>
        Pointer,

        /// <summary>A gamepad or the keyboard picked the source and moves the focus.</summary>
        Navigation,
    }

    /// <summary>
    /// Whether a drag is going on, from its start to the drop or the cancel.
    /// </summary>
    public bool IsDragging => _phase is Phase.Pointer or Phase.Navigation;

    /// <summary>
    /// The gamepad button that picks the focused source and puts it on the focused target; <c>X</c> by default.
    /// </summary>
    /// <remarks>
    /// Not <c>A</c>: Gum clicks the focused button the moment <c>A</c> goes down, before molib sees the press.
    /// ADR: interaction/drag-drop — Keyboard and gamepad.
    /// </remarks>
    public GamepadButton PickButton { get; set; } = GamepadButton.X;

    /// <summary>
    /// The key that picks the focused source and puts it on the focused target; <c>X</c> by default.
    /// </summary>
    /// <remarks>
    /// Not Enter or Space: they are Gum's click keys (<c>FrameworkElement.ClickCombos</c>). Ignored while a text
    /// field has the focus. Like the arrows, the key makes the keyboard the last device, and a press that brings a
    /// hidden focus back does nothing else. ADR: interaction/drag-drop — Keyboard and gamepad.
    /// </remarks>
    public Keys PickKey
    {
        get => _molib.Input.PickKey;
        set => _molib.Input.PickKey = value;
    }

    /// <summary>
    /// Raised when a drag starts; <see cref="DragEventArgs.Target"/> is <see langword="null"/>.
    /// </summary>
    public event EventHandler<DragEventArgs>? Started;

    /// <summary>
    /// Raised after the drop callback of the target ran.
    /// </summary>
    public event EventHandler<DragEventArgs>? Dropped;

    /// <summary>
    /// Raised when a drag ends without a drop: released beside a target or over one that refuses the data,
    /// cancelled with Esc, B or back, or put back on the source.
    /// </summary>
    public event EventHandler<DragEventArgs>? Cancelled;

    /// <summary>
    /// Makes <paramref name="visual"/> a drag source.
    /// </summary>
    /// <param name="visual">The element to drag; for a Forms control, its <c>Visual</c> or a container around it.
    /// A press on anything inside it starts the drag.</param>
    /// <param name="getData">Gives the dragged object when the drag starts; <see langword="null"/> means there is
    /// nothing to drag.</param>
    /// <param name="options">Threshold, copy and source look; defaults when omitted.</param>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void AddSource(GraphicalUiElement visual, Func<object?> getData, DragOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(getData);
        _molib.Lifetime.ThrowIfDisposed();
        DragRegistration registration = Register(visual);
        registration.GetData = getData;
        registration.Options = options ?? new DragOptions();
    }

    /// <summary>
    /// Makes <paramref name="visual"/> a drop target.
    /// </summary>
    /// <param name="visual">The element that takes drops. It shows the drag with the states of the
    /// <see cref="DropTargetCategory"/> category if it has one.</param>
    /// <param name="canDrop">Whether the target takes the data; called when the drag comes over the target.</param>
    /// <param name="drop">Takes the data; called on the release over the target when <paramref name="canDrop"/>
    /// returned <see langword="true"/>.</param>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public void AddTarget(GraphicalUiElement visual, Func<object, bool> canDrop, Action<object> drop)
    {
        ArgumentNullException.ThrowIfNull(visual);
        ArgumentNullException.ThrowIfNull(canDrop);
        ArgumentNullException.ThrowIfNull(drop);
        _molib.Lifetime.ThrowIfDisposed();
        DragRegistration registration = Register(visual);
        registration.CanDrop = canDrop;
        registration.Drop = drop;
    }

    /// <summary>
    /// Removes <paramref name="visual"/> from drag and drop; a drag from it is cancelled.
    /// </summary>
    /// <param name="visual">A registered element; anything else is ignored.</param>
    public void Remove(GraphicalUiElement visual)
    {
        ArgumentNullException.ThrowIfNull(visual);
        if (!_registrations.TryGetValue(visual, out DragRegistration? registration))
        {
            return;
        }

        if (ReferenceEquals(registration, _source))
        {
            if (IsDragging)
            {
                Finish(drop: false);
            }
            else
            {
                ResetPending();
            }
        }

        if (ReferenceEquals(registration, _target))
        {
            SetTarget(null);
        }

        _targets.Remove(registration);
        _registrations.Remove(visual);
    }

    /// <summary>
    /// Picks <paramref name="source"/> as <see cref="PickButton"/> does: an action of the game, such as a button of
    /// its own, can start a gamepad drag with it.
    /// </summary>
    /// <param name="source">A registered source.</param>
    /// <returns><see langword="true"/> when the drag started; <see langword="false"/> when a drag is already going
    /// on or the source has nothing to drag.</returns>
    /// <exception cref="ArgumentException"><paramref name="source"/> is not a registered source.</exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public bool Pick(GraphicalUiElement source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _molib.Lifetime.ThrowIfDisposed();
        if (!_registrations.TryGetValue(source, out DragRegistration? registration) || registration.GetData is null)
        {
            throw new ArgumentException(
                "The element is not a drag source. Register it with AddSource.",
                nameof(source));
        }

        return _phase == Phase.Idle && StartNavigation(registration);
    }

    /// <summary>
    /// Cancels the drag that is going on, as Esc does; nothing happens without a drag.
    /// </summary>
    public void Cancel()
    {
        if (IsDragging)
        {
            Finish(drop: false);
        }
        else if (_phase == Phase.Pending)
        {
            ResetPending();
        }
    }

    /// <summary>
    /// Takes a cancel press of <c>Layers</c> while a drag is going on or its copy is still flying.
    /// </summary>
    /// <returns>Whether the press belonged to drag and drop.</returns>
    /// <remarks>ADR: interaction/layers — Cancel; interaction/drag-drop — Pointer: mouse and touch.</remarks>
    internal bool HandleCancel()
    {
        if (IsDragging)
        {
            Finish(drop: false);
            return true;
        }

        return _host is not null;
    }

    /// <summary>
    /// Starts, moves and ends drags. Runs after <c>Layers</c> in <c>MolibService.Update</c>.
    /// </summary>
    /// <param name="deltaSeconds">Frame time, already limited by the service; real time, not scaled.</param>
    internal void Update(float deltaSeconds)
    {
        if (!_game.IsActive)
        {
            return;
        }

        ICursor cursor = FrameworkElement.MainCursor;
        switch (_phase)
        {
            case Phase.Idle:
                if (cursor.PrimaryPush)
                {
                    BeginPending(cursor);
                }
                else if (IsPickPushed())
                {
                    PickFocused();
                }

                break;
            case Phase.Pending:
                UpdatePending(cursor, deltaSeconds);
                break;
            case Phase.Pointer:
                UpdatePointer(cursor, deltaSeconds);
                break;
            case Phase.Navigation:
                UpdateNavigation(cursor);
                break;
        }
    }

    /// <summary>
    /// Ends a drag without raising events and forgets every source and target; the service is being disposed.
    /// </summary>
    /// <remarks>
    /// The source gets its own transparency and render target back, the target its <c>None</c> state (applied at
    /// once: the motion core is already shut down), and the copy leaves the tree. <see cref="Cancelled"/> is not
    /// raised: the game is tearing molib down, not cancelling a move. The registrations hold the game's delegates, so
    /// they are cleared. ADR: interaction/drag-drop — Limits; foundation/architecture — Disposal.
    /// </remarks>
    internal void Shutdown()
    {
        if (IsDragging)
        {
            SetTarget(null);
            Undim();
        }

        _phase = Phase.Idle;
        _source = null;
        _sourceScroll = null;
        _data = null;
        _modal = null;
        _targets.Clear();
        _scrollAreas.Clear();
        _molib.Motion.Stop(_flight.Progress);
        EndFlight();
        _registrations.Clear();
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

    private static float EdgeSpeedFor(float fromStart, float fromEnd)
    {
        if (fromStart < EdgeBand)
        {
            return -EdgeSpeed * (1 - (fromStart / EdgeBand));
        }

        return fromEnd < EdgeBand ? EdgeSpeed * (1 - (fromEnd / EdgeBand)) : 0;
    }

    private static bool IsAlongScroll(ScrollViewer viewer, float deltaX, float deltaY) =>
        MathF.Abs(deltaY) >= MathF.Abs(deltaX)
            ? viewer.VerticalScrollBarMaximum > 0
            : viewer.HorizontalScrollBarMaximum > 0;

    private DragRegistration Register(GraphicalUiElement visual) =>
        _registrations.GetValue(visual, static element => new DragRegistration(element));

    private DragRegistration? FindRegistration(GraphicalUiElement? element, bool asSource)
    {
        for (GraphicalUiElement? current = element; current is not null; current = current.Parent)
        {
            if (_registrations.TryGetValue(current, out DragRegistration? registration)
                && (asSource ? registration.GetData is not null : registration.CanDrop is not null))
            {
                return registration;
            }
        }

        return null;
    }

    private void BeginPending(ICursor cursor)
    {
        DragRegistration? source = FindRegistration(cursor.VisualPushed, asSource: true);
        if (source is null)
        {
            return;
        }

        _source = source;
        _pressX = cursor.XRespectingGumZoomAndBounds();
        _pressY = cursor.YRespectingGumZoomAndBounds();
        _holdSeconds = 0;
        _isTouch = cursor.LastInputDevice == InputDevice.TouchScreen;
        _sourceScroll = VisualTree.FindScrollViewer(source.Visual);
        _phase = Phase.Pending;
    }

    /// <remarks>
    /// Inside a scroll viewer a touch along the scroll axis belongs to the scrolling and one across it to the drag;
    /// along the axis a finger starts the drag by holding still for 0.5 s. Gum does not scroll by dragging with the
    /// mouse, so the mouse starts the drag in any direction. ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    private void UpdatePending(ICursor cursor, float deltaSeconds)
    {
        if (!cursor.PrimaryDown || _source is not { } source)
        {
            ResetPending();
            return;
        }

        float deltaX = cursor.XRespectingGumZoomAndBounds() - _pressX;
        float deltaY = cursor.YRespectingGumZoomAndBounds() - _pressY;
        float threshold = source.Options.Threshold;
        ScrollViewer? scroll = _isTouch ? _sourceScroll : null;
        if ((deltaX * deltaX) + (deltaY * deltaY) > threshold * threshold)
        {
            if (scroll is not null && IsAlongScroll(scroll, deltaX, deltaY))
            {
                ResetPending();
            }
            else
            {
                StartPointer(source, cursor);
            }

            return;
        }

        if (scroll is not null)
        {
            _holdSeconds += deltaSeconds;
            if (_holdSeconds >= HoldToDragSeconds)
            {
                StartPointer(source, cursor);
            }
        }
    }

    private void ResetPending()
    {
        _phase = Phase.Idle;
        _source = null;
        _sourceScroll = null;
    }

    /// <remarks>
    /// The copy goes into a full-screen container on top of <c>ModalRoot</c>: Gum draws the top of <c>ModalRoot</c>
    /// above everything, and gives the cursor only to it. The container takes no events, so for the rest of the
    /// drag nothing in Gum gets the cursor: no touch scrolling under the finger, no hover, no click on the release.
    /// molib finds the target under the pointer itself. ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    private void StartPointer(DragRegistration source, ICursor cursor)
    {
        if (source.GetData?.Invoke() is not { } data)
        {
            ResetPending();
            return;
        }

        FinishFlightNow();
        _data = data;
        _modal = _molib.Input.TopModal();
        CollectTargets(source);
        _molib.Scrolling.StopTracking();

        GraphicalUiElement visual = source.Visual;
        _grabX = _pressX - visual.AbsoluteLeft;
        _grabY = _pressY - visual.AbsoluteTop;
        _host = new ContainerRuntime { HasEvents = false, ExposeChildrenEvents = false };
        _host.Dock(Dock.Fill);
        _copy = CreateCopy(source, data);
        _host.AddChild(_copy);
        _gum.ModalRoot.AddChild(_host);
        MoveCopy(cursor.XRespectingGumZoomAndBounds(), cursor.YRespectingGumZoomAndBounds());
        Dim(visual, source.Options.HideSourceWhileDragging);
        _phase = Phase.Pointer;
        _molib.Sound.Raise(UiSoundKind.DragStarted, visual);
        Started?.Invoke(this, new DragEventArgs(data, visual, target: null));
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

    private void UpdatePointer(ICursor cursor, float deltaSeconds)
    {
        float x = cursor.XRespectingGumZoomAndBounds();
        float y = cursor.YRespectingGumZoomAndBounds();
        SetTarget(HitTest(x, y));
        if (!cursor.PrimaryDown)
        {
            Finish(drop: _target is not null && _targetAccepts);
            return;
        }

        MoveCopy(x, y);
        ScrollAtEdges(x, y, deltaSeconds);
    }

    private void MoveCopy(float x, float y)
    {
        if (_copy is not null)
        {
            _copy.X = x - _grabX;
            _copy.Y = y - _grabY;
        }
    }

    /// <summary>
    /// The registered target drawn under the canvas point; the innermost one when targets are nested.
    /// </summary>
    /// <remarks>
    /// While the copy is on top of <c>ModalRoot</c> Gum reports nothing under the cursor, so molib tests the targets
    /// itself: visible, inside the bounds and inside every clipping ancestor, and inside the modal window that was on
    /// top when the drag started, as Gum's own input would be. ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    private DragRegistration? HitTest(float x, float y)
    {
        DragRegistration? best = null;
        for (int i = 0; i < _targets.Count; i++)
        {
            DragRegistration candidate = _targets[i];
            if (ReferenceEquals(candidate, _source) || !VisualTree.ShowsAt(candidate.Visual, x, y)
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

    /// <remarks>
    /// The registrations are listed once per drag; the list holds the targets reachable then and the scroll viewers
    /// around them and around the source, which scroll when the pointer rests at their edge.
    /// ADR: interaction/drag-drop — Scrolling during a drag.
    /// </remarks>
    private void CollectTargets(DragRegistration source)
    {
        _targets.Clear();
        _scrollAreas.Clear();
        foreach (KeyValuePair<GraphicalUiElement, DragRegistration> pair in _registrations)
        {
            DragRegistration registration = pair.Value;
            if (registration.CanDrop is null || (_modal is not null && !VisualTree.IsInside(pair.Key, _modal)))
            {
                continue;
            }

            _targets.Add(registration);
            AddScrollAreas(pair.Key);
        }

        AddScrollAreas(source.Visual);
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

    /// <remarks>
    /// The speed grows from 0 at the inner side of a 32-unit band to 600 units/s at the edge.
    /// ADR: interaction/drag-drop — Scrolling during a drag.
    /// </remarks>
    private void ScrollAtEdges(float x, float y, float deltaSeconds)
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

    /// <remarks>
    /// <c>canDrop</c> is asked when the drag comes over a target, not every frame.
    /// ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    private void SetTarget(DragRegistration? target)
    {
        if (ReferenceEquals(target, _target))
        {
            return;
        }

        if (_target is not null)
        {
            ShowDropState(_target, NoneState);
        }

        _target = target;
        _targetAccepts = target?.CanDrop is { } canDrop && _data is not null && canDrop(_data);
        if (target is not null)
        {
            ShowDropState(target, _targetAccepts ? AcceptState : RejectState);
        }
    }

    private void ShowDropState(DragRegistration target, string state)
    {
        GraphicalUiElement visual = target.Visual;
        if (!visual.Categories.ContainsKey(DropTargetCategory))
        {
            string name = string.IsNullOrEmpty(visual.Name) ? visual.GetType().Name : visual.Name;
            _molib.Diagnostics.WarnOnce(
                "dragDrop.noDropTargetCategory",
                name,
                $"Drop target '{name}' has no '{DropTargetCategory}' state category, so it does not show the drag. "
                    + "Add the category with the states None, Accept and Reject.");
            return;
        }

        visual.ApplyState(DropTargetCategory, state);
    }

    /// <remarks>
    /// The drop callback runs first, so the target already shows the item when the copy flies into it.
    /// ADR: interaction/drag-drop — Pointer: mouse and touch.
    /// </remarks>
    private void Finish(bool drop)
    {
        Debug.Assert(_source is not null && _data is not null, "Finish runs during a drag.");
        DragRegistration source = _source;
        object data = _data;
        DragRegistration? target = drop ? _target : null;
        SetTarget(null);
        Undim();
        _phase = Phase.Idle;
        _source = null;
        _sourceScroll = null;
        _data = null;
        _targets.Clear();
        _scrollAreas.Clear();

        if (_copy is not null)
        {
            StartFlight(_copy, target?.Visual ?? source.Visual, centered: target is not null);
        }

        if (target is not null)
        {
            target.Drop?.Invoke(data);
            _molib.Sound.Raise(UiSoundKind.DragDropped, target.Visual);
            Dropped?.Invoke(this, new DragEventArgs(data, source.Visual, target.Visual));
        }
        else
        {
            _molib.Sound.Raise(UiSoundKind.DragCancelled, source.Visual);
            Cancelled?.Invoke(this, new DragEventArgs(data, source.Visual, target: null));
        }
    }

    private void StartFlight(ContainerRuntime copy, GraphicalUiElement destination, bool centered)
    {
        float toX = destination.AbsoluteLeft;
        float toY = destination.AbsoluteTop;
        if (centered)
        {
            toX += (destination.AbsoluteWidth - copy.Width) / 2;
            toY += (destination.AbsoluteHeight - copy.Height) / 2;
        }

        _flight.Begin(copy, toX, toY);
        if (_molib.Motion.IsInstant)
        {
            EndFlight();
        }
        else
        {
            _molib.Motion.Start(_flight.Progress, MotionPreset.Smooth);
        }
    }

    private void FinishFlightNow()
    {
        if (_host is not null)
        {
            _molib.Motion.Stop(_flight.Progress);
            EndFlight();
        }
    }

    private void EndFlight()
    {
        _host?.RemoveFromRoot();
        _host = null;
        _copy = null;
    }

    private void Dim(GraphicalUiElement visual, bool hide)
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

    private void Undim()
    {
        if (_dimmed is not null)
        {
            _dimmed.IsRenderTarget = _dimmedWasRenderTarget;
            _dimmed.Alpha = _dimmedAlpha;
            _dimmed = null;
        }
    }

    private bool IsPickPushed()
    {
        List<IGamePad> pads = FrameworkElement.GamePadsForUiControl;
        for (int i = 0; i < pads.Count; i++)
        {
            if (pads[i].ButtonPushed(PickButton))
            {
                return true;
            }
        }

        if (InteractiveGue.CurrentInputReceiver is TextBoxBase)
        {
            return false;
        }

        List<IInputReceiverKeyboard> keyboards = FrameworkElement.KeyboardsForUiControl;
        for (int i = 0; i < keyboards.Count; i++)
        {
            if (keyboards[i].KeyPushed(PickKey))
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// A press that brought a hidden focus back does nothing else, as any first navigation press.
    /// ADR: interaction/focus-and-gamepad — Focus visibility.
    /// </remarks>
    private void PickFocused()
    {
        if (_molib.Input.HasFocusChangedThisFrame()
            || InteractiveGue.CurrentInputReceiver is not FrameworkElement focused)
        {
            return;
        }

        DragRegistration? source = FindRegistration(focused.Visual, asSource: true);
        if (source is not null)
        {
            StartNavigation(source);
        }
    }

    /// <remarks>
    /// There is no copy for a gamepad or the keyboard: above a modal window Gum draws only that window, so a copy
    /// would have to go into <c>ModalRoot</c> and take the focus from the window. The source is dimmed and the
    /// focused target shows <c>Accept</c> or <c>Reject</c>. ADR: interaction/drag-drop — Keyboard and gamepad.
    /// </remarks>
    private bool StartNavigation(DragRegistration source)
    {
        if (source.GetData?.Invoke() is not { } data)
        {
            return false;
        }

        FinishFlightNow();
        _source = source;
        _data = data;
        Dim(source.Visual, hide: false);
        _phase = Phase.Navigation;
        UpdateNavigationTarget();
        _molib.Sound.Raise(UiSoundKind.DragStarted, source.Visual);
        Started?.Invoke(this, new DragEventArgs(data, source.Visual, target: null));
        return true;
    }

    private void UpdateNavigation(ICursor cursor)
    {
        if (cursor.PrimaryPush)
        {
            Finish(drop: false);
            return;
        }

        UpdateNavigationTarget();
        if (!IsPickPushed() || _molib.Input.HasFocusChangedThisFrame())
        {
            return;
        }

        if (_target is not null && _targetAccepts)
        {
            Finish(drop: true);
        }
        else if (_molib.Input.FocusTarget is { } focus && _source is { } source
            && VisualTree.IsInside(focus.Visual, source.Visual))
        {
            Finish(drop: false);
        }
    }

    private void UpdateNavigationTarget()
    {
        DragRegistration? target = FindRegistration(_molib.Input.FocusTarget?.Visual, asSource: false);
        SetTarget(ReferenceEquals(target, _source) ? null : target);
    }
}
