namespace Molib.Interaction;

/// <summary>
/// The speed of the scroll values while a finger is down: how they changed over the last few frames.
/// </summary>
/// <remarks>
/// A ring of the last five frames, so nothing is created per frame. ADR: interaction/scrolling — Measuring velocity.
/// </remarks>
internal sealed class ScrollVelocity
{
    private const int FrameCount = 5;
    private const float VelocityWindow = 0.1f;
    private const float StillWindow = 0.05f;
    private const double Tolerance = 0.001;

    private readonly float[] _frameSeconds = new float[FrameCount];
    private readonly double[] _frameDeltaX = new double[FrameCount];
    private readonly double[] _frameDeltaY = new double[FrameCount];
    private int _frameNext;
    private int _frameFilled;
    private double _trackedX;
    private double _trackedY;

    /// <summary>
    /// Forgets the frames recorded before and starts from the values at the press.
    /// </summary>
    /// <param name="x">The horizontal scroll value.</param>
    /// <param name="y">The vertical scroll value.</param>
    public void Reset(double x, double y)
    {
        _frameNext = 0;
        _frameFilled = 0;
        _trackedX = x;
        _trackedY = y;
    }

    /// <summary>
    /// Records one frame with the finger down.
    /// </summary>
    /// <param name="seconds">Real frame time.</param>
    /// <param name="x">The horizontal scroll value now.</param>
    /// <param name="y">The vertical scroll value now.</param>
    public void Track(float seconds, double x, double y)
    {
        _frameDeltaX[_frameNext] = x - _trackedX;
        _frameDeltaY[_frameNext] = y - _trackedY;
        _frameSeconds[_frameNext] = seconds;
        _frameNext = (_frameNext + 1) % FrameCount;
        _frameFilled = Math.Min(_frameFilled + 1, FrameCount);
        _trackedX = x;
        _trackedY = y;
    }

    /// <summary>
    /// The speed of one scroll value at the release: the average over the frames of the last 0.1 s, or 0 when the
    /// finger rested for the last 0.05 s.
    /// </summary>
    /// <param name="vertical">Whether the vertical value is asked for.</param>
    /// <returns>Units per second.</returns>
    public double ReleaseVelocity(bool vertical)
    {
        double[] deltas = vertical ? _frameDeltaY : _frameDeltaX;
        float seconds = 0;
        float stillSeconds = 0;
        bool isStill = true;
        double distance = 0;
        for (int i = 0; i < _frameFilled && seconds < VelocityWindow; i++)
        {
            int index = (_frameNext - 1 - i + FrameCount) % FrameCount;
            double delta = deltas[index];
            if (isStill && Math.Abs(delta) < Tolerance)
            {
                stillSeconds += _frameSeconds[index];
            }
            else
            {
                isStill = false;
            }

            seconds += _frameSeconds[index];
            distance += delta;
        }

        return seconds <= 0 || stillSeconds >= StillWindow ? 0 : distance / seconds;
    }
}
