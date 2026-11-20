namespace Molib.Widgets;

/// <summary>
/// What other molib modules need from a <see cref="VirtualList{T}"/> without knowing its item type.
/// </summary>
/// <remarks>
/// The input module keeps the arrow keys out of the common navigation while a list is in focus, and the sound
/// module tells a selection by the player from one made by code. ADR: widgets/virtual-list — Focus and gamepad;
/// integration/sound — Silence for programmatic changes.
/// </remarks>
internal interface IVirtualList
{
    /// <summary>
    /// Raised after the player changed the selection with the pointer, the keyboard or a gamepad.
    /// </summary>
    public event EventHandler? SelectedByUser;
}
