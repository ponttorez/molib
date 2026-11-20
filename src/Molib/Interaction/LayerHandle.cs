using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// An open or closing layer: closes it and tells when and why it closed.
/// </summary>
/// <remarks>ADR: interaction/layers — Showing a layer.</remarks>
public sealed class LayerHandle
{
    private readonly Layers _owner;
    private readonly TaskCompletionSource<LayerCloseReason> _closed = new();

    internal LayerHandle(
        Layers owner,
        GraphicalUiElement content,
        LayerOptions options,
        ContainerRuntime root,
        ContainerRuntime safeContainer,
        RectangleRuntime? scrimFill,
        FrameworkElement? focusToReturn)
    {
        _owner = owner;
        Content = content;
        Options = options;
        Root = root;
        SafeContainer = safeContainer;
        ScrimFill = scrimFill;
        FocusToReturn = focusToReturn;
    }

    /// <summary>
    /// The element shown in the layer.
    /// </summary>
    public GraphicalUiElement Content { get; }

    /// <summary>
    /// Whether the layer is open: <see langword="false"/> from the moment it starts closing.
    /// </summary>
    public bool IsOpen { get; internal set; } = true;

    /// <summary>
    /// Completes on the game thread, inside <c>MolibService.Update</c>, when the layer has been removed from the
    /// tree and the focus returned.
    /// </summary>
    public Task<LayerCloseReason> Closed => _closed.Task;

    /// <summary>
    /// How the layer opens and closes.
    /// </summary>
    internal LayerOptions Options { get; }

    /// <summary>
    /// The container that holds the scrim and the safe area container, a child of <c>ModalRoot</c> or
    /// <c>PopupRoot</c>.
    /// </summary>
    internal ContainerRuntime Root { get; }

    /// <summary>
    /// The container attached to the safe area that holds <see cref="Content"/>.
    /// </summary>
    internal ContainerRuntime SafeContainer { get; }

    /// <summary>
    /// The colored rectangle of the scrim, or <see langword="null"/> for a popup layer.
    /// </summary>
    internal RectangleRuntime? ScrimFill { get; }

    /// <summary>
    /// The element that had the focus, or was to get it, when the layer opened.
    /// </summary>
    internal FrameworkElement? FocusToReturn { get; }

    /// <summary>
    /// Starts closing the layer with the reason <see cref="LayerCloseReason.Code"/>.
    /// </summary>
    /// <returns><see cref="Closed"/>. A layer that is already closing keeps its first reason.</returns>
    public Task<LayerCloseReason> CloseAsync() => _owner.Close(this, LayerCloseReason.Code);

    /// <summary>
    /// Completes <see cref="Closed"/>.
    /// </summary>
    /// <param name="reason">Why the layer closed.</param>
    internal void Complete(LayerCloseReason reason) => _closed.TrySetResult(reason);
}
