namespace Molib.Interaction;

/// <summary>
/// How long a press has to be held to count as a long press.
/// </summary>
/// <remarks>ADR: interaction/gestures — Long press.</remarks>
public sealed class LongPressOptions
{
    /// <summary>
    /// How long the pointer has to stay down, in seconds; 0.5 by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number.</exception>
    public float Duration
    {
        get;
        init => field = Guard.Positive(value, nameof(Duration));
    } = 0.5f;
}
