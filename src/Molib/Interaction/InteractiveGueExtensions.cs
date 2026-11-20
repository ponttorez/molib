using Gum.Wireframe;

namespace Molib.Interaction;

/// <summary>
/// The long press gesture for any Gum element that takes cursor events.
/// </summary>
/// <remarks>ADR: interaction/gestures — Long press.</remarks>
public static class InteractiveGueExtensions
{
    /// <summary>
    /// Makes <paramref name="element"/> report a press held for <see cref="LongPressOptions.Duration"/> with the mouse
    /// or a finger.
    /// </summary>
    /// <param name="element">The element; for a Forms control, its <c>Visual</c>.</param>
    /// <param name="molib">The molib service of the game.</param>
    /// <param name="options">How long to hold; 0.5 s when omitted.</param>
    /// <returns>The long press: its <c>Triggered</c> event and <c>ConsumedClick</c> for the click handler.</returns>
    /// <exception cref="InvalidOperationException">The element already has a long press.</exception>
    /// <exception cref="ObjectDisposedException">The service is disposed.</exception>
    public static LongPress UseLongPress(
        this InteractiveGue element,
        MolibService molib,
        LongPressOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(molib);
        return molib.Gestures.Attach(element, options ?? new LongPressOptions());
    }
}
