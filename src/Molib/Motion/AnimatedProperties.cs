namespace Molib.Motion;

/// <summary>
/// Which properties of an element a state transition animates.
/// </summary>
/// <remarks>ADR: motion/state-transitions — How a transition runs.</remarks>
[Flags]
internal enum AnimatedProperties
{
    /// <summary>Nothing changed.</summary>
    None = 0,

    /// <summary><c>X</c>.</summary>
    X = 1,

    /// <summary><c>Y</c>.</summary>
    Y = 1 << 1,

    /// <summary><c>Width</c>.</summary>
    Width = 1 << 2,

    /// <summary><c>Height</c>.</summary>
    Height = 1 << 3,

    /// <summary><c>Rotation</c>.</summary>
    Rotation = 1 << 4,

    /// <summary><c>Color</c>, or <c>FillColor</c> of a rectangle or a circle.</summary>
    Color = 1 << 5,

    /// <summary><c>StrokeColor</c> of a rectangle or a circle.</summary>
    StrokeColor = 1 << 6,

    /// <summary><c>Alpha</c> of an element drawn by an <c>InvisibleRenderable</c>.</summary>
    Alpha = 1 << 7,

    /// <summary>Properties that make Gum lay the element out again.</summary>
    Layout = X | Y | Width | Height,

    /// <summary>Properties interpolated without leaving the 0–1 range of the progress.</summary>
    Colors = Color | StrokeColor | Alpha,
}
