namespace Molib.Interaction;

/// <summary>
/// Kind of device the player used last.
/// </summary>
/// <remarks>ADR: interaction/focus-and-gamepad — Last device.</remarks>
public enum InputDeviceKind
{
    /// <summary>
    /// Mouse or touch: the focus is hidden.
    /// </summary>
    Cursor,

    /// <summary>
    /// Gamepad or keyboard: the focus is shown.
    /// </summary>
    Navigation,
}
