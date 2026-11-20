namespace Molib.Motion;

/// <summary>
/// How an element appears and disappears: whether it fades, how far it slides, and which motion moves it.
/// </summary>
/// <remarks>
/// A preset is immutable. There is no scale preset: Gum has no draw scale for an element, and changing its size
/// lays the content out again. ADR: motion/show-hide — Presets.
/// </remarks>
public sealed class ShowHide
{
    private const float SlideDistance = 24;

    private ShowHide(bool fades, float offsetY, MotionPreset motion)
    {
        Fades = fades;
        OffsetY = offsetY;
        Motion = motion;
    }

    /// <summary>
    /// Fades in from fully transparent and out to it.
    /// </summary>
    public static ShowHide Fade { get; } = new(true, 0, MotionPreset.Smooth);

    /// <summary>
    /// Fades in while rising 24 canvas units from below, and goes back down when hiding.
    /// </summary>
    public static ShowHide FadeSlideUp { get; } = new(true, SlideDistance, MotionPreset.Smooth);

    /// <summary>
    /// Fades in while coming 24 canvas units down from above, and goes back up when hiding; for elements at the top
    /// edge such as toasts.
    /// </summary>
    public static ShowHide FadeSlideDown { get; } = new(true, -SlideDistance, MotionPreset.Smooth);

    /// <summary>
    /// Shows and hides at once.
    /// </summary>
    public static ShowHide Instant { get; } = new(false, 0, MotionPreset.Instant);

    /// <summary>
    /// Whether the hidden element is fully transparent.
    /// </summary>
    public bool Fades { get; }

    /// <summary>
    /// Where the hidden element sits relative to its place, in canvas units along <c>Y</c>: positive is below.
    /// </summary>
    public float OffsetY { get; }

    /// <summary>
    /// Motion of the animation; <see cref="MotionPreset.Smooth"/> for the built-in presets.
    /// </summary>
    public MotionPreset Motion { get; }

    /// <summary>
    /// Returns this preset with another motion, such as <see cref="MotionPreset.Bouncy"/> for a toast.
    /// </summary>
    /// <param name="motion">The motion to use.</param>
    /// <returns>A new preset; create it once and reuse it.</returns>
    public ShowHide WithMotion(MotionPreset motion)
    {
        ArgumentNullException.ThrowIfNull(motion);
        return new ShowHide(Fades, OffsetY, motion);
    }
}
