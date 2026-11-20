namespace Molib.Platform;

/// <summary>
/// Supplies the safe area insets of the screen: display cutouts, rounded corners, system bars.
/// </summary>
/// <remarks>
/// molib has no platform code, so the platform head of the game implements this interface. Insets change not only
/// with the window size: a phone turned from one landscape side to the other keeps its size but moves the cutout, and
/// the system may report insets a few frames after start. That is why the source raises <see cref="Changed"/> itself.
/// ADR: platform/safe-area — Source.
/// </remarks>
public interface ISafeAreaSource
{
    /// <summary>
    /// Raised when the insets may have changed. May be raised on any thread: molib only records that it happened
    /// and calls <see cref="GetInsetsInPixels"/> on the game thread in its next <c>Update</c>.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Returns the current insets in back buffer pixels, measured from the edges of the back buffer.
    /// </summary>
    /// <returns>Insets in back buffer pixels; zero on every side when the whole back buffer is safe.</returns>
    /// <remarks>Called on the game thread, only after <see cref="Changed"/> or a back buffer resize.</remarks>
    public Thickness GetInsetsInPixels();
}
