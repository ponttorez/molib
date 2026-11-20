namespace Molib.Interaction;

/// <summary>
/// How a scroll viewer coasts after a fling and where it comes to rest.
/// </summary>
/// <remarks>ADR: interaction/scrolling — Attaching, Motion, Snapping.</remarks>
public sealed class InertiaOptions
{
    /// <summary>
    /// How fast the coasting dies out, per second: the speed falls as <c>exp(-Friction · t)</c>. 4 by default,
    /// about one second to a stop.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive number.</exception>
    public float Friction
    {
        get;
        init => field = Guard.Positive(value, nameof(Friction));
    } = 4f;

    /// <summary>
    /// Where the content comes to rest; <see cref="ScrollSnap.None"/> by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a named <see cref="ScrollSnap"/>.</exception>
    public ScrollSnap Snap
    {
        get;
        init => field = Guard.Defined(value, nameof(Snap));
    }

    /// <summary>
    /// Which point of a child meets the same point of the visible area with <see cref="ScrollSnap.Items"/>;
    /// <see cref="SnapAlign.Start"/> by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a named <see cref="SnapAlign"/>.</exception>
    public SnapAlign SnapAlign
    {
        get;
        init => field = Guard.Defined(value, nameof(SnapAlign));
    }
}
