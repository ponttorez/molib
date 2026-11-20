namespace Molib.Interaction;

/// <summary>
/// The gamepad slot passed to <see cref="Input.GamepadConnected"/> and <see cref="Input.GamepadDisconnected"/>.
/// </summary>
public sealed class GamepadEventArgs : EventArgs
{
    internal GamepadEventArgs(int index)
    {
        Index = index;
    }

    /// <summary>
    /// Gamepad slot, from 0 to 3: the index in <c>GumService.Gamepads</c>.
    /// </summary>
    public int Index { get; }
}
