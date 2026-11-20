namespace Molib.Motion;

/// <summary>
/// Where an element keeps the color and transparency that molib animates.
/// </summary>
/// <remarks>ADR: motion/state-transitions — How a transition runs.</remarks>
internal enum ColorSource
{
    /// <summary>The element has no color molib animates.</summary>
    None,

    /// <summary><c>Color</c> of a sprite, nine-slice, text or polygon.</summary>
    Color,

    /// <summary>
    /// <c>FillColor</c> and <c>StrokeColor</c> of a rectangle or a circle; their <c>Color</c> is obsolete and writes
    /// the outline only.
    /// </summary>
    FillAndStroke,

    /// <summary>
    /// <c>Alpha</c> of an <c>InvisibleRenderable</c>: containers and Forms visuals. It hides the children only
    /// while the element draws through a render target.
    /// </summary>
    Alpha,
}
