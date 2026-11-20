using Gum.GueDeriving;
using Gum.Wireframe;

namespace Molib.Widgets;

/// <summary>
/// A toast given to <see cref="Toasts.Show(GraphicalUiElement, ToastOptions?)"/>: waiting in the queue, shown, or
/// closed.
/// </summary>
/// <remarks>ADR: widgets/toasts — API.</remarks>
public sealed class ToastHandle
{
    private readonly Toasts _owner;

    internal ToastHandle(Toasts owner, GraphicalUiElement content, ToastOptions options, ContainerRuntime slot)
    {
        _owner = owner;
        Content = content;
        Options = options;
        Slot = slot;
    }

    /// <summary>
    /// What the toast shows.
    /// </summary>
    public GraphicalUiElement Content { get; }

    /// <summary>
    /// How the toast is shown.
    /// </summary>
    public ToastOptions Options { get; }

    /// <summary>
    /// Whether the toast waits or is shown and has not been dismissed yet.
    /// </summary>
    public bool IsOpen => State is ToastState.Waiting or ToastState.Showing or ToastState.Shown;

    /// <summary>
    /// The container that holds the content in the stack and takes the pointer for it.
    /// </summary>
    internal ContainerRuntime Slot { get; }

    /// <summary>
    /// Where the toast is in its life.
    /// </summary>
    internal ToastState State { get; set; } = ToastState.Waiting;

    /// <summary>
    /// Seconds the toast has been fully shown and not paused.
    /// </summary>
    internal float ShownSeconds { get; set; }

    /// <summary>
    /// The running appearance or disappearance.
    /// </summary>
    internal Task<bool>? Animation { get; set; }

    /// <summary>
    /// The move of the toast to its place in the stack; set when the toast is shown.
    /// </summary>
    internal ToastMove? Move { get; set; }

    /// <summary>
    /// The handler of the size changes of the content while the toast is on screen.
    /// </summary>
    internal EventHandler? SizeChangedHandler { get; set; }

    /// <summary>
    /// Closes the toast: one in the queue never appears, one on screen disappears. Does nothing for a closed toast.
    /// </summary>
    public void Dismiss() => _owner.Dismiss(this);
}
