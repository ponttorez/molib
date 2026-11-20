using Gum;
using Gum.Converters;
using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.Forms.DefaultVisuals.V3;
using Gum.GueDeriving;
using Gum.Wireframe;
using Molib.Integration;
using Molib.Motion;
using RenderingLibrary.Graphics;

namespace Molib.Widgets;

/// <summary>
/// Short messages over the interface that disappear by themselves: a queue per placement, up to three on screen,
/// appearance, disappearance and the move of the stack.
/// </summary>
/// <remarks>
/// <para>
/// Toasts live in a container on top of Gum's <c>PopupRoot</c>: they do not block input outside themselves, take no
/// focus and are not part of the cancel stack. The container and its safe area container take no events, so only
/// the toasts themselves take the pointer.
/// </para>
/// <para>
/// A toast is a slot that takes the pointer, with the content inside. The slot is placed in the stack and has the
/// size of the content; the content appears and disappears inside it through show and hide, so the slide does not
/// change the size of the slot or the stack. ADR: widgets/toasts.
/// </para>
/// </remarks>
public sealed class Toasts
{
    private const int MaxVisible = 3;
    private const float EdgeMargin = 24;
    private const float Spacing = 12;
    private const float PaddingX = 20;
    private const float PaddingY = 12;
    private const float MaxTextWidth = 560;
    private const int PlacementCount = 4;

    private readonly MolibService _molib;
    private readonly GumService _gum;
    private readonly List<ToastHandle>[] _visible = new List<ToastHandle>[PlacementCount];
    private readonly Queue<ToastHandle>[] _waiting = new Queue<ToastHandle>[PlacementCount];
    private readonly List<ToastHandle> _active = [];
    private ContainerRuntime? _host;
    private ContainerRuntime? _safeContainer;

    internal Toasts(MolibService molib, GumService gum)
    {
        _molib = molib;
        _gum = gum;
        for (int i = 0; i < PlacementCount; i++)
        {
            _visible[i] = [];
            _waiting[i] = new Queue<ToastHandle>();
        }
    }

    /// <summary>
    /// Builds the visual of a text toast. When not set, the text is a Gum <see cref="Label"/> on the bordered
    /// nine-slice of the Gum theme.
    /// </summary>
    /// <remarks>The visual sizes itself; the toast takes its size.</remarks>
    public Func<string, GraphicalUiElement>? DefaultVisualFactory { get; set; }

    /// <summary>
    /// Number of toasts on screen, including the ones that are appearing or disappearing.
    /// </summary>
    public int VisibleCount => _active.Count;

    /// <summary>
    /// Shows a text toast. The text goes through Gum localization like any text, so it may be a string key.
    /// </summary>
    /// <param name="text">The message.</param>
    /// <param name="options">How to show it; defaults when omitted.</param>
    /// <returns>The toast, to dismiss it early.</returns>
    public ToastHandle Show(string text, ToastOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        GraphicalUiElement content = DefaultVisualFactory?.Invoke(text) ?? CreateDefaultVisual(text);
        return Show(content, options);
    }

    /// <summary>
    /// Shows any element as a toast. The element sizes itself; it is taken out of its parent while shown and out of
    /// the toast when the toast is gone.
    /// </summary>
    /// <param name="content">What to show.</param>
    /// <param name="options">How to show it; defaults when omitted.</param>
    /// <returns>The toast, to dismiss it early.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="content"/> is already in an open toast.</exception>
    public ToastHandle Show(GraphicalUiElement content, ToastOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        for (int i = 0; i < PlacementCount; i++)
        {
            if (Contains(_visible[i], content) || Contains(_waiting[i], content))
            {
                throw new InvalidOperationException(
                    "The element is already in an open toast. Wait until that toast is gone before showing it again.");
            }
        }

        options ??= new ToastOptions();
        var slot = new ContainerRuntime { HasEvents = true };
        var toast = new ToastHandle(this, content, options, slot);
        slot.ClickBubbling += (_, _) => HandleClick(toast);
        int placement = (int)options.Placement;
        if (_visible[placement].Count < MaxVisible)
        {
            Present(toast);
        }
        else
        {
            _waiting[placement].Enqueue(toast);
        }

        return toast;
    }

    /// <summary>
    /// Closes a toast; see <see cref="ToastHandle.Dismiss"/>.
    /// </summary>
    /// <param name="toast">A toast of this module.</param>
    internal void Dismiss(ToastHandle toast)
    {
        switch (toast.State)
        {
            case ToastState.Waiting:
                RemoveWaiting(toast);
                toast.State = ToastState.Closed;
                break;
            case ToastState.Showing:
            case ToastState.Shown:
                BeginHide(toast);
                break;
        }
    }

    /// <summary>
    /// Runs the timers of the toasts on screen and finishes the ones that are gone.
    /// </summary>
    /// <param name="deltaSeconds">Real frame time, already limited by the service.</param>
    /// <remarks>
    /// Runs after the motion core, so an appearance or a disappearance that ended in this frame is already
    /// complete. The time of a toast is real time: <c>TimeScale</c> slows the animations, not the reading time.
    /// ADR: widgets/toasts — Placement and queue; foundation/architecture — Update order.
    /// </remarks>
    internal void Update(float deltaSeconds)
    {
        if (_active.Count == 0)
        {
            return;
        }

        KeepHostOnTop();
        ToastHandle? paused = FindPaused();
        for (int i = 0; i < _active.Count; i++)
        {
            ToastHandle toast = _active[i];
            switch (toast.State)
            {
                case ToastState.Showing when toast.Animation?.IsCompleted != false:
                    toast.State = ToastState.Shown;
                    break;
                case ToastState.Shown when !ReferenceEquals(toast, paused):
                    toast.ShownSeconds += deltaSeconds;
                    if (toast.ShownSeconds >= toast.Options.Duration)
                    {
                        BeginHide(toast);
                    }

                    break;
                case ToastState.Hiding when toast.Animation?.IsCompleted != false:
                    Remove(toast);
                    i--;
                    break;
            }
        }
    }

    private static bool Contains(List<ToastHandle> toasts, GraphicalUiElement content)
    {
        for (int i = 0; i < toasts.Count; i++)
        {
            if (ReferenceEquals(toasts[i].Content, content))
            {
                return true;
            }
        }

        return false;
    }

    private static bool Contains(Queue<ToastHandle> toasts, GraphicalUiElement content)
    {
        foreach (ToastHandle toast in toasts)
        {
            if (ReferenceEquals(toast.Content, content))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsTop(ToastPlacement placement) =>
        placement is ToastPlacement.Top or ToastPlacement.TopEnd;

    /// <remarks>
    /// A bordered nine-slice of the Gum theme in its dark gray with a label inside; the label grows with the text up
    /// to a width and then wraps, so a longer translation does not get cut. ADR: widgets/toasts — API;
    /// integration/localization — What it means for molib.
    /// </remarks>
    private static ContainerRuntime CreateDefaultVisual(string text)
    {
        var root = new ContainerRuntime
        {
            WidthUnits = DimensionUnitType.RelativeToChildren,
            Width = 2 * PaddingX,
            HeightUnits = DimensionUnitType.RelativeToChildren,
            Height = 2 * PaddingY,
        };
        Styling style = Styling.ActiveStyle;
        var background = new NineSliceRuntime { Texture = style.SpriteSheet };
        background.ApplyState(style.NineSlice.Bordered);
        background.Color = style.Colors.DarkGray;
        background.Dock(Dock.Fill);
        root.AddChild(background);
        var label = new Label { Text = text };
        label.Visual.X = PaddingX;
        label.Visual.Y = PaddingY;
        label.Visual.MaxWidth = MaxTextWidth;
        root.AddChild(label.Visual);
        return root;
    }

    private static ShowHide PresetOf(ToastPlacement placement) =>
        IsTop(placement) ? ShowHide.FadeSlideDown : ShowHide.FadeSlideUp;

    private static void PlaceSlot(ContainerRuntime slot, ToastPlacement placement)
    {
        bool isEnd = placement is ToastPlacement.BottomEnd or ToastPlacement.TopEnd;
        slot.XUnits = isEnd ? GeneralUnitType.PixelsFromLarge : GeneralUnitType.PixelsFromMiddle;
        slot.XOrigin = isEnd ? HorizontalAlignment.Right : HorizontalAlignment.Center;
        slot.X = isEnd ? -EdgeMargin : 0;
        slot.YUnits = IsTop(placement) ? GeneralUnitType.PixelsFromSmall : GeneralUnitType.PixelsFromLarge;
        slot.YOrigin = IsTop(placement) ? VerticalAlignment.Top : VerticalAlignment.Bottom;
    }

    private static void FitSlot(ToastHandle toast)
    {
        toast.Slot.Width = toast.Content.AbsoluteWidth;
        toast.Slot.Height = toast.Content.AbsoluteHeight;
    }

    private void Present(ToastHandle toast)
    {
        ContainerRuntime safeContainer = EnsureHost();
        GraphicalUiElement content = toast.Content;
        ContainerRuntime slot = toast.Slot;
        ToastPlacement placement = toast.Options.Placement;
        content.RemoveFromRoot();
        content.XUnits = GeneralUnitType.PixelsFromSmall;
        content.YUnits = GeneralUnitType.PixelsFromSmall;
        content.XOrigin = HorizontalAlignment.Left;
        content.YOrigin = VerticalAlignment.Top;
        content.X = 0;
        content.Y = 0;
        content.Visible = false;
        slot.WidthUnits = DimensionUnitType.Absolute;
        slot.HeightUnits = DimensionUnitType.Absolute;
        PlaceSlot(slot, placement);
        slot.AddChild(content);
        safeContainer.AddChild(slot);
        _molib.Scaling.ApplyOrientation(slot);
        _molib.Sound.Attach(content);
        _molib.Sound.Raise(UiSoundKind.ToastShown, content);
        FitSlot(toast);
        toast.SizeChangedHandler = (_, _) => HandleContentSizeChanged(toast);
        content.SizeChanged += toast.SizeChangedHandler;

        bool isTop = IsTop(placement);
        toast.Move = new ToastMove(
            _molib.Motion,
            offset => slot.Y = isTop ? EdgeMargin + offset : -(EdgeMargin + offset));
        toast.Move.Jump(0);
        _visible[(int)placement].Add(toast);
        _active.Add(toast);
        Restack(placement);
        toast.State = ToastState.Showing;
        toast.Animation = _molib.ShowHideAnimator.Run(
            content,
            show: true,
            PresetOf(placement).WithMotion(MotionPreset.Bouncy),
            HideAction.Hide,
            CancellationToken.None);
        KeepHostOnTop();
    }

    private void HandleContentSizeChanged(ToastHandle toast)
    {
        FitSlot(toast);
        Restack(toast.Options.Placement);
    }

    /// <remarks>
    /// The newest toast is nearest to the edge; every older one is farther by the heights of the newer ones and the
    /// spacing. A hiding toast keeps its place until it is gone, so the others do not run into it.
    /// </remarks>
    private void Restack(ToastPlacement placement)
    {
        List<ToastHandle> toasts = _visible[(int)placement];
        float offset = 0;
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            ToastHandle toast = toasts[i];
            toast.Move?.MoveTo(offset);
            offset += toast.Slot.Height + Spacing;
        }
    }

    private void BeginHide(ToastHandle toast)
    {
        toast.State = ToastState.Hiding;
        toast.Animation = _molib.ShowHideAnimator.Run(
            toast.Content,
            show: false,
            PresetOf(toast.Options.Placement),
            HideAction.Hide,
            CancellationToken.None);
    }

    private void Remove(ToastHandle toast)
    {
        ToastPlacement placement = toast.Options.Placement;
        toast.State = ToastState.Closed;
        if (toast.Move is { } move)
        {
            _molib.Motion.Stop(move.Progress);
        }

        toast.Content.SizeChanged -= toast.SizeChangedHandler;
        toast.Slot.RemoveFromRoot();
        toast.Content.RemoveFromRoot();
        toast.Content.Visible = true;
        _visible[(int)placement].Remove(toast);
        _active.Remove(toast);
        Restack(placement);
        Queue<ToastHandle> waiting = _waiting[(int)placement];
        if (waiting.Count > 0)
        {
            Present(waiting.Dequeue());
        }
        else if (_active.Count == 0)
        {
            _host?.RemoveFromRoot();
        }
    }

    private void RemoveWaiting(ToastHandle toast)
    {
        Queue<ToastHandle> waiting = _waiting[(int)toast.Options.Placement];
        int count = waiting.Count;
        for (int i = 0; i < count; i++)
        {
            ToastHandle next = waiting.Dequeue();
            if (!ReferenceEquals(next, toast))
            {
                waiting.Enqueue(next);
            }
        }
    }

    /// <remarks>
    /// The click comes bubbling: the content usually takes events itself (an empty Gum container does), so the click
    /// lands on the content or on something inside it. A control inside the toast that should keep it open marks
    /// <c>ClickBubbling</c> handled. ADR: widgets/toasts — Placement and queue.
    /// </remarks>
    private void HandleClick(ToastHandle toast)
    {
        if (toast.Options.DismissOnClick)
        {
            Dismiss(toast);
        }
    }

    /// <remarks>
    /// The host and its safe area container take no events: an empty container would take the clicks of everything
    /// under it in Gum. ADR: interaction/layers — Layer structure.
    /// </remarks>
    private ContainerRuntime EnsureHost()
    {
        if (_host is null || _safeContainer is null)
        {
            _host = new ContainerRuntime { HasEvents = false };
            _host.Dock(Dock.Fill);
            _safeContainer = new ContainerRuntime { HasEvents = false };
            _host.AddChild(_safeContainer);
            _molib.SafeArea.Attach(_safeContainer);
        }

        if (_host.Parent is null)
        {
            _gum.PopupRoot.AddChild(_host);
        }

        return _safeContainer;
    }

    /// <remarks>
    /// Gum draws the children of a root in order, the last one on top; a popup layer opened after a toast would
    /// cover it. One reference comparison per frame while toasts are on screen.
    /// </remarks>
    private void KeepHostOnTop()
    {
        if (_host is null)
        {
            return;
        }

        var children = _gum.PopupRoot.Children;
        if (children.Count > 0 && !ReferenceEquals(children[^1], _host))
        {
            _host.RemoveFromRoot();
            _gum.PopupRoot.AddChild(_host);
        }
    }

    /// <remarks>
    /// The mouse pauses the toast it is over; a finger pauses the toast only while it touches it, because after a
    /// tap the cursor of Gum stays where the finger was. ADR: widgets/toasts — Placement and queue.
    /// </remarks>
    private ToastHandle? FindPaused()
    {
        ICursor cursor = FrameworkElement.MainCursor;
        if (cursor.LastInputDevice == InputDevice.TouchScreen && !cursor.PrimaryDown)
        {
            return null;
        }

        for (GraphicalUiElement? current = cursor.VisualOver; current is not null; current = current.Parent)
        {
            if (!ReferenceEquals(current.Parent, _safeContainer))
            {
                continue;
            }

            for (int i = 0; i < _active.Count; i++)
            {
                if (ReferenceEquals(_active[i].Slot, current))
                {
                    return _active[i];
                }
            }

            return null;
        }

        return null;
    }
}
