using System.Collections.Specialized;
using Gum.Converters;
using Gum.DataTypes;
using Gum.Forms.Controls;
using Gum.GueDeriving;
using Gum.Input;
using Gum.Managers;
using Gum.Wireframe;
using RenderingLibrary.Graphics;
using Keys = Gum.Forms.Input.Keys;

namespace Molib.Widgets;

/// <summary>
/// A scrolling list of rows of one height that keeps visuals only for the rows in view and one screen of rows above
/// and below, and gives them to new rows as it scrolls.
/// </summary>
/// <typeparam name="T">Type of the items.</typeparam>
/// <remarks>
/// <para>
/// The list is a Gum <see cref="ScrollViewer"/>: the scroll bar, the mouse wheel, touch scrolling, the right stick,
/// molib inertia and drag and drop work as on any scroll viewer. The inner panel gets the height of all rows, and
/// each row is placed at its own position, so nothing is measured. A frame without scrolling does no work: the
/// rows are recomputed on <see cref="ScrollViewer.ScrollChanged"/> and when the visible area changes size.
/// </para>
/// <para>
/// A row is a Gum <see cref="ListBoxItem"/> around the visual from <see cref="CreateVisual"/>, so hover, press and
/// selection look as in a Gum list box, and any visual made for a list box item works as a row. The selection is
/// kept by data index and drawn with the <c>Selected</c> state; the rows never take the focus, because a focused
/// Gum element scrolls its scroll viewers to itself and the list moves its rows itself.
/// </para>
/// <para>ADR: widgets/virtual-list.</para>
/// </remarks>
public sealed class VirtualList<T> : ScrollViewer, IVirtualList
{
    private const float DefaultItemHeight = 40;

    private readonly Dictionary<int, Row> _bound = [];
    private readonly Stack<Row> _pool = new();
    private readonly List<int> _leaving = [];
    private INotifyCollectionChanged? _observed;
    private int _selectedIndex = -1;
    private int _boundStart;
    private int _boundEnd;
    private bool _isBinding;
    private bool _isRebuilding;
    private EventHandler? _selectedByUser;

    /// <summary>
    /// Creates an empty list with the default visual of a Gum <see cref="ScrollViewer"/>.
    /// </summary>
    public VirtualList()
    {
        InnerPanel.ChildrenLayout = ChildrenLayout.Regular;
        InnerPanel.HeightUnits = DimensionUnitType.Absolute;
        InnerPanel.Height = 0;
        ScrollChanged += HandleScrollChanged;
        ClipContainer.SizeChanged += HandleViewportSizeChanged;
    }

    /// <summary>
    /// Height of every row in canvas units; 40 by default.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public float ItemHeight
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            if (value != field)
            {
                field = value;
                Rebuild();
            }
        }
    } = DefaultItemHeight;

    /// <summary>
    /// Number of cells in a row; 1 for a plain list, more for a grid. A cell is as wide as the list divided by the
    /// number of columns.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than 1.</exception>
    public int Columns
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            if (value != field)
            {
                field = value;
                Rebuild();
            }
        }
    } = 1;

    /// <summary>
    /// Creates an empty row visual; called only when the pool of visuals runs out. When not set, a row is the
    /// default visual of a Gum <see cref="ListBoxItem"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A visual that already carries a <see cref="ListBoxItem"/> control keeps it; any other visual gets a new one.
    /// The list sets the position and size of the visual.
    /// </para>
    /// <para>
    /// The order of an object initializer does not matter: rows that <see cref="Items"/> created before this
    /// property was set are thrown away and made again. ADR: widgets/virtual-list — API.
    /// </para>
    /// </remarks>
    public Func<InteractiveGue>? CreateVisual
    {
        get;
        init
        {
            field = value;
            DiscardRows();
        }
    }

    /// <summary>
    /// Fills a row visual with an item and its index; called when a row appears and when its visual is given to
    /// another row. When not set, the row shows the text of the item through
    /// <see cref="ListBoxItem.UpdateToObject(object?)"/>.
    /// </summary>
    /// <remarks>
    /// Rows that <see cref="Items"/> filled before this property was set are filled again.
    /// ADR: widgets/virtual-list — API.
    /// </remarks>
    public Action<InteractiveGue, T, int>? BindVisual
    {
        get;
        init
        {
            field = value;
            Rebuild();
        }
    }

    /// <summary>
    /// The items. A collection that implements <see cref="INotifyCollectionChanged"/> updates the list by itself;
    /// after changing any other collection, call <see cref="Refresh"/>. Setting a new collection clears the
    /// selection.
    /// </summary>
    public IReadOnlyList<T>? Items
    {
        get;
        set
        {
            _observed?.CollectionChanged -= HandleCollectionChanged;
            field = value;
            _observed = value as INotifyCollectionChanged;
            _observed?.CollectionChanged += HandleCollectionChanged;
            SetSelected(-1, isByUser: false);
            Rebuild();
        }
    }

    /// <summary>
    /// Index of the selected item, or -1 when nothing is selected. Setting it does not scroll; use
    /// <see cref="ScrollToIndex(int, ScrollAlign)"/> for that.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is less than -1 or not less than the item count.
    /// </exception>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    "The index must be -1 or the index of an item.");
            }

            SetSelected(value, isByUser: false);
        }
    }

    /// <summary>
    /// The selected item, or the default of <typeparamref name="T"/> when nothing is selected.
    /// </summary>
    public T? SelectedItem => Items is { } items && _selectedIndex >= 0 ? items[_selectedIndex] : default;

    private int Count => Items?.Count ?? 0;

    private int RowCount => (Count + Columns - 1) / Columns;

    /// <summary>
    /// Raised when <see cref="SelectedIndex"/> points to another item: by the player, by code, or because the
    /// selected item was removed.
    /// </summary>
    public event EventHandler? SelectionChanged;

    /// <summary>
    /// Raised when the player activates an item: a click or a tap on its row, or A, Enter or Space while the list
    /// has the focus. The activated item is also the selected one.
    /// </summary>
    public event Action<T>? ItemActivated;

    /// <inheritdoc/>
    event EventHandler? IVirtualList.SelectedByUser
    {
        add => _selectedByUser += value;
        remove => _selectedByUser -= value;
    }

    /// <summary>
    /// Shows the items again after a collection that does not raise change events was changed. A selection past
    /// the new end is cleared.
    /// </summary>
    public void Refresh()
    {
        if (_selectedIndex >= Count)
        {
            SetSelected(-1, isByUser: false);
        }

        Rebuild();
    }

    /// <summary>
    /// Scrolls so that the row of an item is where <paramref name="align"/> says, as far as the scroll range allows.
    /// </summary>
    /// <param name="index">Index of the item.</param>
    /// <param name="align">Where to put the row.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not the index of an item.
    /// </exception>
    public void ScrollToIndex(int index, ScrollAlign align = ScrollAlign.Nearest)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
        float viewport = ClipContainer.AbsoluteHeight;
        float top = index / Columns * ItemHeight;
        float bottom = top + ItemHeight;
        float value = (float)VerticalScrollBarValue;
        float target = align switch
        {
            ScrollAlign.Start => top,
            ScrollAlign.Center => top - ((viewport - ItemHeight) / 2),
            ScrollAlign.End => bottom - viewport,
            _ => Nearest(value, viewport, top, bottom),
        };
        VerticalScrollBarValue = Math.Clamp(target, 0, Math.Max(0, (float)VerticalScrollBarMaximum));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The list is one focus stop: the D-pad, the left stick and the arrow keys move the selection, PageUp and
    /// PageDown move it by a screen, A, Enter and Space activate the selected item. A gamepad direction in which the
    /// selection cannot move leaves the list through Gum's common navigation; the arrow keys never leave it, Tab
    /// does. The first step with nothing selected selects the first row in view.
    /// ADR: widgets/virtual-list — Focus and gamepad.
    /// </remarks>
    public override void OnFocusUpdate()
    {
        if (IsEnabled && Count > 0)
        {
            List<IGamePad> pads = GamePadsForUiControl;
            for (int i = 0; i < pads.Count && IsFocused; i++)
            {
                HandleGamepad(pads[i]);
            }

            List<IInputReceiverKeyboard> keyboards = KeyboardsForUiControl;
            for (int i = 0; i < keyboards.Count && IsFocused; i++)
            {
                HandleKeyboard(keyboards[i]);
            }

            if (IsFocused && IsClickComboPushed())
            {
                Confirm();
            }
        }

        if (IsFocused)
        {
            HandleKeyboardFocusUpdate();
        }

        RaiseFocusUpdate();
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A Gum scroll viewer takes Escape here to leave the focus on its items; the list has no such mode, and Escape
    /// stays with the cancel of molib layers.
    /// </remarks>
    public override void OnFocusUpdatePreview(RoutedEventArgs args)
    {
    }

    private static float Nearest(float value, float viewport, float top, float bottom)
    {
        if (top < value)
        {
            return top;
        }

        return bottom > value + viewport ? bottom - viewport : value;
    }

    private static Direction? ReadDirection(IGamePad pad)
    {
        if (IsPushed(pad, GamepadButton.DPadLeft, DPadDirection.Left))
        {
            return Direction.Left;
        }

        if (IsPushed(pad, GamepadButton.DPadUp, DPadDirection.Up))
        {
            return Direction.Up;
        }

        if (IsPushed(pad, GamepadButton.DPadRight, DPadDirection.Right))
        {
            return Direction.Right;
        }

        return IsPushed(pad, GamepadButton.DPadDown, DPadDirection.Down) ? Direction.Down : null;

        static bool IsPushed(IGamePad pad, GamepadButton button, DPadDirection direction) =>
            pad.ButtonRepeatRate(button) || pad.LeftStick.AsDPadPushedRepeatRate(direction);
    }

    private static bool IsClickComboPushed()
    {
        List<KeyCombo> combos = ClickCombos;
        for (int i = 0; i < combos.Count; i++)
        {
            if (combos[i].IsComboPushed())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The index an item moves to when <paramref name="count"/> items are inserted (positive) or removed (negative)
    /// at <paramref name="start"/>.
    /// </summary>
    private static int Shifted(int index, int start, int count, int whenRemoved)
    {
        if (index < start)
        {
            return index;
        }

        if (count > 0)
        {
            return index + count;
        }

        return index >= start - count ? index + count : whenRemoved;
    }

    private void HandleGamepad(IGamePad pad)
    {
        if (ReadDirection(pad) is { } direction && !TryStep(direction))
        {
            HandleGamepadNavigation(pad);
            return;
        }

        ApplyGamepadStickScroll(pad);
        if (pad.ButtonPushed(GamepadButton.A))
        {
            Confirm();
        }
    }

    private void HandleKeyboard(IInputReceiverKeyboard keyboard)
    {
        if (keyboard.KeyTyped(Keys.Up))
        {
            TryStep(Direction.Up);
        }
        else if (keyboard.KeyTyped(Keys.Down))
        {
            TryStep(Direction.Down);
        }
        else if (keyboard.KeyTyped(Keys.Left))
        {
            TryStep(Direction.Left);
        }
        else if (keyboard.KeyTyped(Keys.Right))
        {
            TryStep(Direction.Right);
        }
        else if (keyboard.KeyTyped(Keys.PageUp))
        {
            StepPage(-1);
        }
        else if (keyboard.KeyTyped(Keys.PageDown))
        {
            StepPage(1);
        }
    }

    /// <returns><see langword="false"/> when the selection cannot move that way.</returns>
    private bool TryStep(Direction direction)
    {
        if (_selectedIndex < 0)
        {
            SelectFromInput(FirstVisibleIndex());
            return true;
        }

        int target = Neighbor(_selectedIndex, direction);
        if (target < 0)
        {
            return false;
        }

        SelectFromInput(target);
        return true;
    }

    private int Neighbor(int index, Direction direction)
    {
        int column = index % Columns;
        return direction switch
        {
            Direction.Up => index >= Columns ? index - Columns : -1,
            Direction.Down when index + Columns < Count => index + Columns,
            Direction.Down => index / Columns < RowCount - 1 ? Count - 1 : -1,
            Direction.Left => column > 0 ? index - 1 : -1,
            _ => column < Columns - 1 && index + 1 < Count ? index + 1 : -1,
        };
    }

    private void StepPage(int sign)
    {
        int rows = Math.Max(1, (int)(ClipContainer.AbsoluteHeight / ItemHeight));
        int from = _selectedIndex < 0 ? FirstVisibleIndex() : _selectedIndex;
        SelectFromInput(Math.Clamp(from + (sign * rows * Columns), 0, Count - 1));
    }

    private void Confirm()
    {
        if (_selectedIndex < 0)
        {
            SelectFromInput(FirstVisibleIndex());
            return;
        }

        ScrollToIndex(_selectedIndex);
        Activate(_selectedIndex);
    }

    private int FirstVisibleIndex()
    {
        int row = (int)MathF.Ceiling((((float)VerticalScrollBarValue) - 0.5f) / ItemHeight);
        return Math.Clamp(row * Columns, 0, Count - 1);
    }

    private void SelectFromInput(int index)
    {
        SetSelected(index, isByUser: true);
        ScrollToIndex(index);
    }

    private void SetSelected(int index, bool isByUser)
    {
        if (index == _selectedIndex)
        {
            return;
        }

        if (_bound.TryGetValue(_selectedIndex, out Row? previous))
        {
            SetRowSelected(previous, false);
        }

        _selectedIndex = index;
        if (_bound.TryGetValue(index, out Row? row))
        {
            SetRowSelected(row, true);
        }

        SelectionChanged?.Invoke(this, EventArgs.Empty);
        if (isByUser)
        {
            _selectedByUser?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SetRowSelected(Row row, bool isSelected)
    {
        _isBinding = true;
        try
        {
            row.Item.IsSelected = isSelected;
        }
        finally
        {
            _isBinding = false;
        }
    }

    private void Activate(int index)
    {
        if (Items is { } items && index >= 0 && index < items.Count)
        {
            ItemActivated?.Invoke(items[index]);
        }
    }

    private void HandleScrollChanged(object? sender, EventArgs e) => UpdateRange();

    private void HandleViewportSizeChanged(object? sender, EventArgs e) => UpdateRange();

    /// <remarks>
    /// A collection that keeps its old index for the first row in view: when the list is scrolled, an insert or a
    /// removal above that row moves the scroll value by as many rows as the row moved, so the rows in view stay in
    /// place. The selection follows its item; removing the selected item clears it. ADR: widgets/virtual-list —
    /// Structure.
    /// </remarks>
    private void HandleCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewStartingIndex >= 0 && e.NewItems is { } added:
                Shift(e.NewStartingIndex, added.Count);
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex >= 0 && e.OldItems is { } removed:
                Shift(e.OldStartingIndex, -removed.Count);
                break;
            case NotifyCollectionChangedAction.Replace:
                Rebuild();
                break;
            case NotifyCollectionChangedAction.Move when e.OldStartingIndex >= 0 && e.NewStartingIndex >= 0:
                Move(e.OldStartingIndex, e.NewStartingIndex);
                break;
            default:
                SetSelected(-1, isByUser: false);
                Rebuild();
                break;
        }
    }

    private void Shift(int start, int count)
    {
        float value = (float)VerticalScrollBarValue;
        int anchor = (int)(value / ItemHeight) * Columns;
        int movedAnchor = Shifted(anchor, start, count, whenRemoved: start);
        int selected = Shifted(_selectedIndex, start, count, whenRemoved: -1);
        _isRebuilding = true;
        try
        {
            InnerPanel.Height = RowCount * ItemHeight;
            if (value > 0)
            {
                VerticalScrollBarValue = value + (((movedAnchor / Columns) - (anchor / Columns)) * ItemHeight);
            }
        }
        finally
        {
            _isRebuilding = false;
        }

        if (selected < 0 && _selectedIndex >= 0)
        {
            SetSelected(-1, isByUser: false);
        }
        else
        {
            _selectedIndex = selected;
        }

        ReleaseAll();
        UpdateRange();
    }

    private void Move(int from, int to)
    {
        if (_selectedIndex == from)
        {
            _selectedIndex = to;
        }
        else if (_selectedIndex > from && _selectedIndex <= to)
        {
            _selectedIndex--;
        }
        else if (_selectedIndex < from && _selectedIndex >= to)
        {
            _selectedIndex++;
        }

        Rebuild();
    }

    private void Rebuild()
    {
        _isRebuilding = true;
        try
        {
            InnerPanel.Height = RowCount * ItemHeight;
        }
        finally
        {
            _isRebuilding = false;
        }

        ReleaseAll();
        UpdateRange();
    }

    /// <remarks>
    /// The range is the rows in view plus one screen of rows above and below, so a scroll by less than a screen
    /// reuses visuals that are already filled. ADR: widgets/virtual-list — Structure.
    /// </remarks>
    private void UpdateRange()
    {
        if (_isRebuilding)
        {
            return;
        }

        int count = Count;
        float viewport = ClipContainer.AbsoluteHeight;
        int start = 0;
        int end = 0;
        if (count > 0 && viewport > 0)
        {
            int screenRows = (int)MathF.Ceiling(viewport / ItemHeight);
            int firstRow = (int)((float)VerticalScrollBarValue / ItemHeight);
            start = Math.Max(0, firstRow - screenRows) * Columns;
            end = Math.Min(count, Math.Min(RowCount, firstRow + (2 * screenRows) + 1) * Columns);
        }

        if (start == _boundStart && end == _boundEnd)
        {
            return;
        }

        _leaving.Clear();
        foreach (KeyValuePair<int, Row> pair in _bound)
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
                Row row = Acquire();
                Bind(row, i);
                _bound.Add(i, row);
            }
        }

        _boundStart = start;
        _boundEnd = end;
    }

    private void DiscardRows()
    {
        ReleaseAll();
        while (_pool.TryPop(out Row? row))
        {
            row.Item.Visual.RemoveFromRoot();
        }

        Rebuild();
    }

    private void ReleaseAll()
    {
        foreach (KeyValuePair<int, Row> pair in _bound)
        {
            Release(pair.Value);
        }

        _bound.Clear();
        _boundStart = 0;
        _boundEnd = 0;
    }

    private Row Acquire()
    {
        if (_pool.TryPop(out Row? pooled))
        {
            pooled.Item.Visual.Visible = true;
            return pooled;
        }

        InteractiveGue visual = CreateVisual?.Invoke() ?? new ListBoxItem().Visual;
        ListBoxItem item = visual.FormsControlAsObject as ListBoxItem ?? new ListBoxItem(visual);
        var row = new Row(item);
        item.Selected += (_, _) => HandleRowSelected(row);
        item.Clicked += (_, _) => HandleRowClicked(row);
        if (BindVisual is null && visual.GetGraphicalUiElementByName("TextInstance") is TextRuntime text)
        {
            text.SetTextNoTranslate(text.Text);
        }

        visual.XUnits = GeneralUnitType.Percentage;
        visual.XOrigin = HorizontalAlignment.Left;
        visual.YUnits = GeneralUnitType.PixelsFromSmall;
        visual.YOrigin = VerticalAlignment.Top;
        visual.WidthUnits = DimensionUnitType.PercentageOfParent;
        visual.HeightUnits = DimensionUnitType.Absolute;
        InnerPanel.AddChild(visual);
        return row;
    }

    private void Bind(Row row, int index)
    {
        if (Items is not { } items)
        {
            return;
        }

        InteractiveGue visual = row.Item.Visual;
        float cellWidth = 100f / Columns;
        row.Index = index;
        visual.X = index % Columns * cellWidth;
        visual.Y = index / Columns * ItemHeight;
        visual.Width = cellWidth;
        visual.Height = ItemHeight;
        T item = items[index];
        if (BindVisual is { } bind)
        {
            bind(visual, item, index);
        }
        else
        {
            row.Item.UpdateToObject(item);
        }

        SetRowSelected(row, index == _selectedIndex);
    }

    private void Release(Row row)
    {
        SetRowSelected(row, false);
        row.Item.IsHighlighted = false;
        row.Item.Visual.Visible = false;
        row.Index = -1;
        _pool.Push(row);
    }

    /// <remarks>
    /// A Gum list box item selects itself when the mouse presses it; the list selects on the release, as a button
    /// acts, so a press puts the row back as it was. A finger selects on the release in Gum already.
    /// ADR: widgets/virtual-list — Mouse and touch.
    /// </remarks>
    private void HandleRowSelected(Row row)
    {
        if (_isBinding || row.Index < 0)
        {
            return;
        }

        ICursor cursor = MainCursor;
        if (cursor.PrimaryPush && cursor.LastInputDevice == InputDevice.Mouse)
        {
            SetRowSelected(row, row.Index == _selectedIndex);
            return;
        }

        SetSelected(row.Index, isByUser: true);
    }

    /// <remarks>
    /// A tap that turned into a touch scroll is not an activation: Gum still raises the click when the finger is
    /// lifted over the same row.
    /// </remarks>
    private void HandleRowClicked(Row row)
    {
        ICursor cursor = MainCursor;
        if (row.Index < 0 || (cursor.LastInputDevice == InputDevice.TouchScreen && !cursor.PrimaryClickNoSlide))
        {
            return;
        }

        SetSelected(row.Index, isByUser: true);
        Activate(row.Index);
    }

    private enum Direction
    {
        Up,
        Down,
        Left,
        Right,
    }

    /// <summary>
    /// A row visual with its <see cref="ListBoxItem"/> and the index of the item it shows, -1 while pooled.
    /// </summary>
    private sealed class Row(ListBoxItem item)
    {
        public ListBoxItem Item { get; } = item;

        public int Index { get; set; } = -1;
    }
}
