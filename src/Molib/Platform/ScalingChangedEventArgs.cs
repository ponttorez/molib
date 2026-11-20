namespace Molib.Platform;

/// <summary>
/// New scaling values, passed to <see cref="Scaling.Changed"/>.
/// </summary>
public sealed class ScalingChangedEventArgs : EventArgs
{
    internal ScalingChangedEventArgs(float zoom, float canvasWidth, float canvasHeight, bool isPortrait)
    {
        Zoom = zoom;
        CanvasWidth = canvasWidth;
        CanvasHeight = canvasHeight;
        IsPortrait = isPortrait;
    }

    /// <summary>
    /// Back buffer pixels per canvas unit.
    /// </summary>
    public float Zoom { get; }

    /// <summary>
    /// Canvas width in canvas units.
    /// </summary>
    public float CanvasWidth { get; }

    /// <summary>
    /// Canvas height in canvas units.
    /// </summary>
    public float CanvasHeight { get; }

    /// <summary>
    /// Whether the back buffer is taller than it is wide.
    /// </summary>
    public bool IsPortrait { get; }
}
