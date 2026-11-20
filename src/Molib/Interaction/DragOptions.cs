using System.Globalization;
using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// How a drag source starts a drag and what follows the pointer.
/// </summary>
/// <remarks>ADR: interaction/drag-drop — API.</remarks>
public sealed class DragOptions
{
    /// <summary>
    /// Builds the visual that follows the pointer from the dragged data; <see langword="null"/> (the default) copies
    /// the visual of the source with all its children.
    /// </summary>
    public Func<object, GraphicalUiElement>? Preview { get; init; }

    /// <summary>
    /// How far the pointer has to move from the press, in canvas units, before the drag starts; 8 by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or not a number.</exception>
    public float Threshold
    {
        get;
        init
        {
            if (!float.IsFinite(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    string.Create(CultureInfo.InvariantCulture, $"Threshold must be 0 or more; got {value}."));
            }

            field = value;
        }
    } = 8f;

    /// <summary>
    /// Whether the source turns fully transparent while the pointer drags its copy; otherwise it is dimmed.
    /// <see langword="false"/> by default. The source keeps its place in the layout either way.
    /// </summary>
    public bool HideSourceWhileDragging { get; init; }
}
