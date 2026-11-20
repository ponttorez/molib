using Gum.Forms.Controls;

namespace Molib.Widgets;

/// <summary>
/// A row visual of a <see cref="VirtualList{T}"/> with its <see cref="ListBoxItem"/> and the index of the item it
/// shows, -1 while it waits in the pool.
/// </summary>
/// <param name="item">The list box item around the row visual.</param>
internal sealed class VirtualRow(ListBoxItem item)
{
    /// <summary>
    /// The list box item around the row visual.
    /// </summary>
    public ListBoxItem Item { get; } = item;

    /// <summary>
    /// Index of the item the row shows, or -1.
    /// </summary>
    public int Index { get; set; } = -1;
}
