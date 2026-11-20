using Gum;
using Gum.Converters;
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
/// Modal and popup layers with a scrim, animation, initial focus and focus return, and the cancel stack: Esc, B on
/// a gamepad and back on Android close the top layer.
/// </summary>
/// <remarks>
/// Gum already makes a child of <c>ModalRoot</c> modal: only the last visible one gets input, and the focus stays
/// inside it. molib adds how a layer opens and closes and who gets the cancel. ADR: interaction/layers — Decision.
/// </remarks>
public sealed class Layers
{
    private readonly MolibService _molib;
    private readonly Game _game;
    private readonly GumService _gum;
    private readonly List<LayerHandle> _open = [];
    private readonly HashSet<GraphicalUiElement> _roots = new(ReferenceEqualityComparer.Instance);
    private int _openedCount;

    internal Layers(MolibService molib, Game game, GumService gum)
    {
        _molib = molib;
        _game = game;
        _gum = gum;
    }

    /// <summary>
    /// Number of open layers; a layer that is closing is not counted.
    /// </summary>
    public int OpenCount => _open.Count;

    /// <summary>
    /// Raised when a cancel press reached no molib layer: there are no layers, or a window the game put into
    /// <c>ModalRoot</c> itself is on top. The game opens its pause menu, closes its own window or quits.
    /// </summary>
    /// <remarks>ADR: interaction/layers — Cancel.</remarks>
    public event EventHandler? CancelUnhandled;

    /// <summary>
    /// Shows <paramref name="content"/> in a modal layer: a scrim over the whole screen that blocks input below,
    /// and the content inside the safe area.
    /// </summary>
    /// <param name="content">Element to show; for a Forms control, its <c>Visual</c>. molib moves it into the
    /// layer.</param>
    /// <param name="options">How the layer opens, takes the focus and closes; defaults when omitted.</param>
    /// <returns>The handle that closes the layer and tells when and why it closed.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="content"/> is already shown in an open
    /// layer.</exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public LayerHandle ShowModal(GraphicalUiElement content, LayerOptions? options = null) =>
        Show(content, options, isModal: true);

    /// <summary>
    /// Shows <paramref name="content"/> in a popup layer: above the tree, without a scrim and without blocking
    /// input below.
    /// </summary>
    /// <param name="content">Element to show; for a Forms control, its <c>Visual</c>. molib moves it into the
    /// layer.</param>
    /// <param name="options">How the layer opens, takes the focus and closes; defaults when omitted.</param>
    /// <returns>The handle that closes the layer and tells when and why it closed.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="content"/> is already shown in an open
    /// layer.</exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public LayerHandle ShowPopup(GraphicalUiElement content, LayerOptions? options = null) =>
        Show(content, options, isModal: false);

    /// <summary>
    /// Starts closing <paramref name="layer"/>: takes the focus out of it, plays the hiding animation, then
    /// removes it from the tree, returns the focus and completes <see cref="LayerHandle.Closed"/>.
    /// </summary>
    /// <param name="layer">A layer of this module.</param>
    /// <param name="reason">Why it closes.</param>
    /// <returns>The <see cref="LayerHandle.Closed"/> task of the layer.</returns>
    /// <remarks>
    /// Only a layer that was on top when it started closing returns the focus, and only if no layer opened while it
    /// was disappearing: otherwise the return would take the focus out of the new window.
    /// ADR: interaction/layers — Focus.
    /// </remarks>
    internal Task<LayerCloseReason> Close(LayerHandle layer, LayerCloseReason reason)
    {
        if (!layer.IsOpen)
        {
            return layer.Closed;
        }

        bool wasTop = _open.Count > 0 && ReferenceEquals(_open[^1], layer);
        _molib.Sound.Raise(UiSoundKind.LayerClosed, layer.Content);
        _open.Remove(layer);
        layer.IsOpen = false;
        _molib.Input.ClearFocusInside(layer.Root);

        ShowHideAnimator animator = _molib.ShowHideAnimator;
        ShowHide preset = layer.Options.Animation;
        Task<bool> content = animator.Run(layer.Content, show: false, preset, HideAction.Hide, CancellationToken.None);
        Task<bool> scrim = layer.ScrimFill is null
            ? Task.FromResult(true)
            : animator.Run(layer.ScrimFill, show: false, ScrimMotion(preset), HideAction.Hide, CancellationToken.None);
        _ = FinishAsync(layer, reason, content, scrim, wasTop ? _openedCount : -1);
        return layer.Closed;
    }

    /// <summary>
    /// Closes every open layer at once with <see cref="LayerCloseReason.Code"/>, top first; the service is being
    /// disposed.
    /// </summary>
    /// <remarks>
    /// The motion core is already shut down, so each close finishes in the call: the layer leaves the tree, its
    /// content is hidden and unparented, the focus returns as after any close, and <c>Closed</c> completes. Layers
    /// that were already closing finished when the motion core finished their animations.
    /// ADR: interaction/layers — Limits; foundation/architecture — Disposal.
    /// </remarks>
    internal void Shutdown()
    {
        while (_open.Count > 0)
        {
            _ = Close(_open[^1], LayerCloseReason.Code);
        }
    }

    /// <summary>
    /// Handles a cancel press. Runs after <c>Input</c> in <c>MolibService.Update</c>.
    /// </summary>
    /// <remarks>
    /// A drag takes the press first: the cancel ends the drag and does not close the layer under it. Gum handles the
    /// input of the frame first, so a press it answered (closing a drop-down, leaving the rows of a list) shows up
    /// as a changed focus, and molib leaves it alone. ADR: interaction/layers — Cancel.
    /// </remarks>
    internal void Update()
    {
        if (!_game.IsActive || !IsCancelPushed() || _molib.DragDrop.HandleCancel())
        {
            return;
        }

        Input input = _molib.Input;
        if (input.HasFocusChangedThisFrame())
        {
            return;
        }

        if (InteractiveGue.CurrentInputReceiver is TextBoxBase field)
        {
            input.UnfocusTextField(field);
            return;
        }

        GraphicalUiElement? topModal = input.TopModal();
        if (topModal is not null && !_roots.Contains(topModal))
        {
            string name = string.IsNullOrEmpty(topModal.Name) ? topModal.GetType().Name : topModal.Name;
            _molib.Diagnostics.WarnOnce(
                "layers.foreignModal",
                name,
                $"'{name}' was added to ModalRoot without molib: it takes no part in the cancel stack and the focus "
                    + "return. Show it with molib.Layers.ShowModal, or close it in CancelUnhandled.");
            CancelUnhandled?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_open.Count == 0)
        {
            CancelUnhandled?.Invoke(this, EventArgs.Empty);
            return;
        }

        LayerHandle top = _open[^1];
        if (top.Options.CloseOnCancel)
        {
            _ = Close(top, LayerCloseReason.Cancel);
        }
    }

    private static ShowHide ScrimMotion(ShowHide preset) => ShowHide.Fade.WithMotion(preset.Motion);

    private static void Center(GraphicalUiElement content)
    {
        content.XUnits = GeneralUnitType.PixelsFromMiddle;
        content.YUnits = GeneralUnitType.PixelsFromMiddle;
        content.XOrigin = HorizontalAlignment.Center;
        content.YOrigin = VerticalAlignment.Center;
        content.X = 0;
        content.Y = 0;
    }

    /// <remarks>
    /// The root and the safe area container do not take events: an empty container takes the clicks of everything
    /// under it in Gum, and a click beside the content has to reach the scrim. The scrim is a container that takes
    /// the click with a colored rectangle inside, because a rectangle takes no input in Gum; the rectangle fades by
    /// its own color, without a full-screen render target. ADR: interaction/layers — Layer structure.
    /// </remarks>
    private LayerHandle Show(GraphicalUiElement content, LayerOptions? options, bool isModal)
    {
        ArgumentNullException.ThrowIfNull(content);
        _molib.Lifetime.ThrowIfDisposed();
        ShowHideAnimator animator = _molib.ShowHideAnimator;
        options ??= new LayerOptions();
        for (int i = 0; i < _open.Count; i++)
        {
            if (ReferenceEquals(_open[i].Content, content))
            {
                throw new InvalidOperationException(
                    "The element is already shown in an open layer. Close that layer before showing it again.");
            }
        }

        var root = new ContainerRuntime { HasEvents = false };
        root.Dock(Gum.Wireframe.Dock.Fill);
        ContainerRuntime? scrim = null;
        RectangleRuntime? scrimFill = null;
        if (isModal)
        {
            scrim = new ContainerRuntime();
            scrim.Dock(Gum.Wireframe.Dock.Fill);
            scrimFill = new RectangleRuntime
            {
                IsFilled = true,
                FillColor = options.ScrimColor,
                StrokeWidth = 0,
                Visible = false,
            };
            scrimFill.Dock(Gum.Wireframe.Dock.Fill);
            scrim.AddChild(scrimFill);
            root.AddChild(scrim);
        }

        var safeContainer = new ContainerRuntime { HasEvents = false };
        root.AddChild(safeContainer);
        _molib.SafeArea.Attach(safeContainer);

        FrameworkElement? focusToReturn = _molib.Input.FocusTarget;
        content.RemoveFromRoot();
        content.Visible = false;
        if (options.CenterContent)
        {
            Center(content);
        }

        safeContainer.AddChild(content);
        var layer = new LayerHandle(this, content, options, root, safeContainer, scrimFill, focusToReturn);
        if (scrim is not null && options.DismissOnScrimClick)
        {
            scrim.Click += (_, _) => Close(layer, LayerCloseReason.ScrimClick);
        }

        (isModal ? _gum.ModalRoot : _gum.PopupRoot).AddChild(root);
        _molib.Scaling.ApplyOrientation(root);
        _molib.Sound.Attach(content);
        _molib.Sound.Raise(UiSoundKind.LayerOpened, content);
        _open.Add(layer);
        _roots.Add(root);
        _openedCount++;

        _ = animator.Run(content, show: true, options.Animation, HideAction.Hide, CancellationToken.None);
        if (scrimFill is not null)
        {
            _ = animator.Run(
                scrimFill,
                show: true,
                ScrimMotion(options.Animation),
                HideAction.Hide,
                CancellationToken.None);
        }

        _molib.Input.FocusLayer(options.InitialFocus, root);
        return layer;
    }

    /// <remarks>
    /// The tasks of the animator complete inside <c>MolibService.Update</c> without a synchronization context, so
    /// the rest of this method runs there too, on the game thread.
    /// </remarks>
    private async Task FinishAsync(
        LayerHandle layer,
        LayerCloseReason reason,
        Task<bool> content,
        Task<bool> scrim,
        int openedCount)
    {
        await content;
        await scrim;
        layer.Root.RemoveFromRoot();
        if (ReferenceEquals(layer.Content.Parent, layer.SafeContainer))
        {
            layer.Content.RemoveFromRoot();
        }

        _molib.SafeArea.Detach(layer.SafeContainer);
        _roots.Remove(layer.Root);
        if (openedCount == _openedCount)
        {
            _molib.Input.ReturnFocus(layer.FocusToReturn);
        }

        layer.Complete(reason);
    }

    private bool IsCancelPushed()
    {
        List<IInputReceiverKeyboard> keyboards = FrameworkElement.KeyboardsForUiControl;
        for (int i = 0; i < keyboards.Count; i++)
        {
            if (keyboards[i].KeyPushed(Keys.Escape))
            {
                return true;
            }
        }

        List<IGamePad> pads = FrameworkElement.GamePadsForUiControl;
        for (int i = 0; i < pads.Count; i++)
        {
            if (pads[i].ButtonPushed(GamepadButton.B))
            {
                return true;
            }
        }

        return OperatingSystem.IsAndroid() && IsAndroidBackPushed();
    }

    /// <remarks>
    /// MonoGame gives the system back key as <c>Back</c> of slot 0 in exactly one read of the gamepad state, and
    /// Gum reads that state every frame, so the key is taken from Gum's gamepad. ADR: interaction/layers — Cancel.
    /// </remarks>
    private bool IsAndroidBackPushed()
    {
        GamePad[] pads = _gum.Gamepads;
        return pads.Length > 0 && pads[0].ButtonPushed(GamepadButton.Back);
    }
}
