using System.Globalization;

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
        init
        {
            if (!float.IsFinite(value) || value <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    string.Create(CultureInfo.InvariantCulture, $"Duration must be a positive number; got {value}."));
            }

            field = value;
        }
    } = 0.5f;
}
