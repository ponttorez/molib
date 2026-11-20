using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// What an element does in drag and drop: gives data as a source, takes data as a target, or both.
/// </summary>
internal sealed class DragRegistration
{
    /// <summary>
    /// Creates an empty registration of <paramref name="visual"/>.
    /// </summary>
    /// <param name="visual">The registered element.</param>
    public DragRegistration(GraphicalUiElement visual)
    {
        Visual = visual;
    }

    /// <summary>
    /// The registered element.
    /// </summary>
    public GraphicalUiElement Visual { get; }

    /// <summary>
    /// Gives the data when a drag starts; <see langword="null"/> when the element is not a source.
    /// </summary>
    public Func<object?>? GetData { get; set; }

    /// <summary>
    /// How a drag from this source starts.
    /// </summary>
    public DragOptions Options { get; set; } = new();

    /// <summary>
    /// Whether the target takes the data; <see langword="null"/> when the element is not a target.
    /// </summary>
    public Func<object, bool>? CanDrop { get; set; }

    /// <summary>
    /// Takes the data dropped on the target.
    /// </summary>
    public Action<object>? Drop { get; set; }
}
