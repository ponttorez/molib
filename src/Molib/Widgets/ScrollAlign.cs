namespace Molib.Widgets;

/// <summary>
/// Where <see cref="VirtualList{T}.ScrollToIndex(int, ScrollAlign)"/> puts the row of an item.
/// </summary>
public enum ScrollAlign
{
    /// <summary>
    /// The smallest scroll that shows the whole row; a row already in view does not move.
    /// </summary>
    Nearest,

    /// <summary>
    /// The top of the row at the top of the list.
    /// </summary>
    Start,

    /// <summary>
    /// The middle of the row at the middle of the list.
    /// </summary>
    Center,

    /// <summary>
    /// The bottom of the row at the bottom of the list.
    /// </summary>
    End,
}
