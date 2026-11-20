namespace Molib.Interaction;

/// <summary>
/// Which of the four Gum gamepads control the interface.
/// </summary>
/// <remarks>
/// The default value is <see cref="All"/>. ADR: interaction/focus-and-gamepad — Gamepads.
/// </remarks>
public readonly record struct UiGamepads
{
    /// <summary>
    /// Number of gamepad slots Gum reads.
    /// </summary>
    public const int SlotCount = 4;

    private UiGamepads(int index)
    {
        Index = index;
    }

    /// <summary>
    /// Every gamepad controls the interface.
    /// </summary>
    public static UiGamepads All => default;

    /// <summary>
    /// The slot of the only gamepad that controls the interface, or <see langword="null"/> for all of them.
    /// </summary>
    public int? Index { get; }

    /// <summary>
    /// Only the gamepad in slot <paramref name="index"/> controls the interface.
    /// </summary>
    /// <param name="index">Gamepad slot, from 0 to 3.</param>
    /// <returns>The selection.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a gamepad slot.</exception>
    public static UiGamepads Only(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, SlotCount);
        return new UiGamepads(index);
    }

    /// <summary>
    /// Whether the gamepad in slot <paramref name="index"/> is selected.
    /// </summary>
    /// <param name="index">Gamepad slot.</param>
    /// <returns><see langword="true"/> when it controls the interface.</returns>
    public bool Includes(int index) => Index is null || Index == index;
}
