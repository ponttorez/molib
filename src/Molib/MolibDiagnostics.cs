namespace Molib;

/// <summary>
/// Warnings molib reports about data errors: bad settings, missing elements, unsupported values.
/// </summary>
/// <remarks>
/// molib never writes to the console itself: the game subscribes to <see cref="Warning"/> and decides where the
/// message goes. Programmer errors are exceptions, not warnings. ADR: foundation/architecture — Diagnostics.
/// </remarks>
public sealed class MolibDiagnostics
{
    private readonly HashSet<(string Code, string Key)> _reported = [];

    internal MolibDiagnostics()
    {
    }

    /// <summary>
    /// Raised once per warning code and key. The first argument is the code in the <c>area.problem</c> form
    /// (<c>scaling.badUserScale</c>), the second is a message for the developer.
    /// </summary>
    public event Action<string, string>? Warning;

    /// <summary>
    /// Reports a warning unless the same <paramref name="code"/> with the same <paramref name="key"/> was already
    /// reported.
    /// </summary>
    /// <param name="code">Warning code in the <c>area.problem</c> form.</param>
    /// <param name="key">
    /// What the warning is about, such as an element name; empty when there is only one subject.
    /// </param>
    /// <param name="message">Message for the developer.</param>
    internal void WarnOnce(string code, string key, string message)
    {
        if (_reported.Add((code, key)))
        {
            Warning?.Invoke(code, message);
        }
    }
}
