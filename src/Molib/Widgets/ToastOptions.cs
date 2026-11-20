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
    /// <remarks>
    /// Infinity and <c>NaN</c> are refused too: with them the toast would never go away by itself.
    /// ADR: widgets/toasts — API.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number.</exception>
    public float Duration
    {
        get;
        init => field = Guard.Positive(value, nameof(Duration));
    } = 2.5f;

    /// <summary>
    /// Where the toast stacks; <see cref="ToastPlacement.Bottom"/> by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a named <see cref="ToastPlacement"/>.
    /// </exception>
    public ToastPlacement Placement
    {
        get;
        init => field = Guard.Defined(value, nameof(Placement));
    } = ToastPlacement.Bottom;

    /// <summary>
    /// Whether a click or a tap on the toast closes it; <see langword="true"/> by default.
    /// </summary>
    public bool DismissOnClick { get; init; } = true;
}
