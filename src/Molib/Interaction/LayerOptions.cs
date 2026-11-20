using Gum.Forms.Controls;
using Microsoft.Xna.Framework;
using Molib.Motion;

namespace Molib.Interaction;

/// <summary>
/// How a layer opens, takes the focus and closes.
/// </summary>
/// <remarks>ADR: interaction/layers — Options.</remarks>
public sealed class LayerOptions
{
    /// <summary>
    /// The scrim color used when <see cref="ScrimColor"/> is not set: black at 160 of 255.
    /// </summary>
    public static readonly Color DefaultScrimColor = new(0, 0, 0, 160);

    /// <summary>
    /// The element that gets the focus when the layer opens. When it is not set or cannot take the focus, the
    /// first focusable element of the layer gets it.
    /// </summary>
    public FrameworkElement? InitialFocus { get; init; }

    /// <summary>
    /// Whether a cancel press closes the layer; <see langword="true"/> by default. When <see langword="false"/>,
    /// the press is absorbed: the player has to choose.
    /// </summary>
    public bool CloseOnCancel { get; init; } = true;

    /// <summary>
    /// Whether a click or touch on the scrim closes a modal layer; <see langword="true"/> by default.
    /// </summary>
    public bool DismissOnScrimClick { get; init; } = true;

    /// <summary>
    /// How the content appears and disappears; <see cref="ShowHide.FadeSlideUp"/> by default. The scrim fades with
    /// the same motion.
    /// </summary>
    public ShowHide Animation { get; init; } = ShowHide.FadeSlideUp;

    /// <summary>
    /// Color of the scrim of a modal layer; <see cref="DefaultScrimColor"/> by default.
    /// </summary>
    public Color ScrimColor { get; init; } = DefaultScrimColor;

    /// <summary>
    /// Whether molib puts the content into the center of the safe area; <see langword="true"/> by default. When
    /// <see langword="false"/>, the content keeps the position the game gave it.
    /// </summary>
    public bool CenterContent { get; init; } = true;
}
