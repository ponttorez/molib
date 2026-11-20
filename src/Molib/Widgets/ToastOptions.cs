namespace Molib.Widgets;

/// <summary>
/// How a toast is shown.
/// </summary>
public sealed class ToastOptions
{
    /// <summary>
    /// Seconds the toast stays fully shown, in real time; 2.5 by default. The time runs only while the toast is
    /// fully shown and not under the mouse or a finger.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public float Duration
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            field = value;
        }
    } = 2.5f;

    /// <summary>
    /// Where the toast stacks; <see cref="ToastPlacement.Bottom"/> by default.
    /// </summary>
    public ToastPlacement Placement { get; init; } = ToastPlacement.Bottom;

    /// <summary>
    /// Whether a click or a tap on the toast closes it; <see langword="true"/> by default.
    /// </summary>
    public bool DismissOnClick { get; init; } = true;
}
