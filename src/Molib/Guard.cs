using System.Globalization;

namespace Molib;

/// <summary>
/// Checks of the values the game passes to molib: options, enums and numbers.
/// </summary>
/// <remarks>
/// A wrong value from code is a programmer error and throws <see cref="ArgumentOutOfRangeException"/> where it is
/// set, not later in a frame: a <c>NaN</c> duration or an enum value outside its range would otherwise break a module
/// far from the line that set it. ADR: foundation/code-style — Errors.
/// </remarks>
internal static class Guard
{
    /// <summary>
    /// Returns <paramref name="value"/> if it is a positive finite number.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="name">Name of the parameter or property.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero, negative, infinite or not a number.</exception>
    public static float Positive(float value, string name) =>
        float.IsFinite(value) && value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                name,
                value,
                Message($"{name} must be a positive number; got {value}."));

    /// <summary>
    /// Returns <paramref name="value"/> if it is zero or a positive finite number.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="name">Name of the parameter or property.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, infinite or not a number.</exception>
    public static float NonNegative(float value, string name) =>
        float.IsFinite(value) && value >= 0
            ? value
            : throw new ArgumentOutOfRangeException(name, value, Message($"{name} must be 0 or more; got {value}."));

    /// <summary>
    /// Returns <paramref name="value"/> if it is one of the named values of its enum.
    /// </summary>
    /// <typeparam name="T">The enum.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="name">Name of the parameter or property.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The value is not defined in the enum.</exception>
    public static T Defined<T>(T value, string name)
        where T : struct, Enum =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(name, value, Message($"{typeof(T).Name} has no value {value}."));

    private static string Message(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
