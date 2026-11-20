namespace Molib.Interaction;

/// <summary>
/// Why a layer closed: the result of <see cref="LayerHandle.Closed"/>.
/// </summary>
public enum LayerCloseReason
{
    /// <summary>
    /// The game called <see cref="LayerHandle.CloseAsync"/>.
    /// </summary>
    Code,

    /// <summary>
    /// The player pressed Esc, B on a gamepad or back on Android.
    /// </summary>
    Cancel,

    /// <summary>
    /// The player clicked or touched the scrim of a modal layer.
    /// </summary>
    ScrimClick,
}
