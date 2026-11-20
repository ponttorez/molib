namespace Molib.Interaction;

/// <summary>
/// The item or page passed to <see cref="ScrollInertia.SnapChanged"/>.
/// </summary>
public sealed class SnapChangedEventArgs : EventArgs
{
    internal SnapChangedEventArgs(int index)
    {
        Index = index;
    }

    /// <summary>
    /// Index of the item among the visible children of the inner panel, or of the page, from 0.
    /// </summary>
    public int Index { get; }
}
