using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// What is dragged, from where and onto what; passed to the events of <see cref="DragDrop"/>.
/// </summary>
public sealed class DragEventArgs : EventArgs
{
    internal DragEventArgs(object data, GraphicalUiElement source, GraphicalUiElement? target)
    {
        Data = data;
        Source = source;
        Target = target;
    }

    /// <summary>
    /// The object the source gave when the drag started.
    /// </summary>
    public object Data { get; }

    /// <summary>
    /// The registered source the drag started from.
    /// </summary>
    public GraphicalUiElement Source { get; }

    /// <summary>
    /// The target the data was dropped on; <see langword="null"/> for <see cref="DragDrop.Started"/> and
    /// <see cref="DragDrop.Cancelled"/>.
    /// </summary>
    public GraphicalUiElement? Target { get; }
}
