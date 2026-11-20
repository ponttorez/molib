using System.Diagnostics.CodeAnalysis;
using Gum.Converters;
using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.Wireframe;
using RenderingLibrary.Graphics;

namespace Molib.Widgets;

/// <summary>
/// The rows of a <see cref="VirtualList{T}"/> that exist now: the visuals bound to item indexes in the range around
/// the view, and the pool of visuals waiting for a new row.
/// </summary>
/// <remarks>
/// <para>
/// The range is the rows in view plus one screen of rows above and below, so a scroll by less than a screen reuses
/// visuals that are already filled. A visual that leaves the range is hidden and goes to the pool; a row that enters
/// the range takes a pooled visual, and only an empty pool creates a new one.
/// </para>
/// <para>
/// Writing the selected state of a row makes Gum raise the row's <c>Selected</c> event; while this class writes it,
/// <see cref="IsBinding"/> tells the list to ignore that event. ADR: widgets/virtual-list — Structure.
/// </para>
/// </remarks>
internal sealed class VirtualRows
{
    private readonly GraphicalUiElement _panel;
    private readonly Func<InteractiveGue> _createVisual;
    private readonly Action<VirtualRow, int> _bind;
    private readonly Action<VirtualRow> _selected;
    private readonly Action<VirtualRow> _clicked;
    private readonly Dictionary<int, VirtualRow> _bound = [];
    private readonly Stack<VirtualRow> _pool = new();
    private readonly List<int> _leaving = [];
    private int _start;
    private int _end;

    /// <summary>
    /// Creates the rows with an empty range and pool.
    /// </summary>
    /// <param name="panel">The inner panel of the list, which holds the row visuals.</param>
    /// <param name="createVisual">Creates an empty row visual when the pool runs out.</param>
    /// <param name="bind">Places and fills a row visual for an item index.</param>
    /// <param name="selected">Called when Gum selects a row, unless this class is writing the state.</param>
    /// <param name="clicked">Called when a row is clicked.</param>
    public VirtualRows(
        GraphicalUiElement panel,
        Func<InteractiveGue> createVisual,
        Action<VirtualRow, int> bind,
        Action<VirtualRow> selected,
        Action<VirtualRow> clicked)
    {
        _panel = panel;
        _createVisual = createVisual;
        _bind = bind;
        _selected = selected;
        _clicked = clicked;
    }

    /// <summary>
    /// Whether this class is writing the selected state of a row right now.
    /// </summary>
    public bool IsBinding { get; private set; }

    /// <summary>
    /// The range of item indexes, first included and last excluded, that rows exist for in a list of this shape.
    /// </summary>
    /// <param name="count">Number of items.</param>
    /// <param name="columns">Cells per row.</param>
    /// <param name="itemHeight">Height of a row.</param>
    /// <param name="viewport">Height of the visible area.</param>
    /// <param name="scroll">Vertical scroll value.</param>
    /// <returns>The first index and the index after the last one; both 0 when nothing is visible.</returns>
    public static (int Start, int End) RangeFor(int count, int columns, float itemHeight, float viewport, float scroll)
    {
        if (count <= 0 || viewport <= 0)
        {
            return (0, 0);
        }

        int rowCount = (count + columns - 1) / columns;
        int screenRows = (int)MathF.Ceiling(viewport / itemHeight);
        int firstRow = (int)(scroll / itemHeight);
        int start = Math.Max(0, firstRow - screenRows) * columns;
        int end = Math.Min(count, Math.Min(rowCount, firstRow + (2 * screenRows) + 1) * columns);
        return (start, end);
    }

    /// <summary>
    /// The row bound to <paramref name="index"/>, if it exists now.
    /// </summary>
    /// <param name="index">Item index.</param>
    /// <param name="row">The row.</param>
    /// <returns>Whether the index is in the range.</returns>
    public bool TryGet(int index, [NotNullWhen(true)] out VirtualRow? row) => _bound.TryGetValue(index, out row);

    /// <summary>
    /// Makes rows exist for exactly the indexes from <paramref name="start"/> to <paramref name="end"/>: rows that
    /// left the range go to the pool, rows that entered it are bound.
    /// </summary>
    /// <param name="start">First index.</param>
    /// <param name="end">Index after the last one.</param>
    public void Show(int start, int end)
    {
        if (start == _start && end == _end)
        {
            return;
        }

        _leaving.Clear();
        foreach (KeyValuePair<int, VirtualRow> pair in _bound)
        {
            if (pair.Key < start || pair.Key >= end)
            {
                _leaving.Add(pair.Key);
            }
        }

        for (int i = 0; i < _leaving.Count; i++)
        {
            int index = _leaving[i];
            Release(_bound[index]);
            _bound.Remove(index);
        }

        for (int i = start; i < end; i++)
        {
            if (!_bound.ContainsKey(i))
            {
                VirtualRow row = Acquire();
                row.Index = i;
                _bind(row, i);
                _bound.Add(i, row);
            }
        }

        _start = start;
        _end = end;
    }

    /// <summary>
    /// Sends every bound row to the pool; the next <see cref="Show"/> binds the range again.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (KeyValuePair<int, VirtualRow> pair in _bound)
        {
            Release(pair.Value);
        }

        _bound.Clear();
        _start = 0;
        _end = 0;
    }

    /// <summary>
    /// Releases every row and takes the pooled visuals out of the panel, so the next rows are created anew.
    /// </summary>
    public void Discard()
    {
        ReleaseAll();
        while (_pool.TryPop(out VirtualRow? row))
        {
            row.Item.Visual.RemoveFromRoot();
        }
    }

    /// <summary>
    /// Writes the selected state of a row without the list taking it for a selection by the player.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <param name="isSelected">Whether the row shows as selected.</param>
    public void SetSelected(VirtualRow row, bool isSelected)
    {
        IsBinding = true;
        try
        {
            row.Item.IsSelected = isSelected;
        }
        finally
        {
            IsBinding = false;
        }
    }

    private VirtualRow Acquire()
    {
        if (_pool.TryPop(out VirtualRow? pooled))
        {
            pooled.Item.Visual.Visible = true;
            return pooled;
        }

        InteractiveGue visual = _createVisual();
        ListBoxItem item = visual.FormsControlAsObject as ListBoxItem ?? new ListBoxItem(visual);
        var row = new VirtualRow(item);
        item.Selected += (_, _) => _selected(row);
        item.Clicked += (_, _) => _clicked(row);
        visual.XUnits = GeneralUnitType.Percentage;
        visual.XOrigin = HorizontalAlignment.Left;
        visual.YUnits = GeneralUnitType.PixelsFromSmall;
        visual.YOrigin = VerticalAlignment.Top;
        visual.WidthUnits = DimensionUnitType.PercentageOfParent;
        visual.HeightUnits = DimensionUnitType.Absolute;
        _panel.AddChild(visual);
        return row;
    }

    private void Release(VirtualRow row)
    {
        SetSelected(row, false);
        row.Item.IsHighlighted = false;
        row.Item.Visual.Visible = false;
        row.Index = -1;
        _pool.Push(row);
    }
}
