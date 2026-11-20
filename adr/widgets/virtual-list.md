# Virtual list

## Decision

Gum's `ListBox` creates a visual for every data item: fine for dozens of items, not for hundreds and thousands (an
inventory, a leaderboard). molib adds `VirtualList<T>` — a subclass of Gum's `ScrollViewer` that keeps visuals only for
the visible rows plus a margin and reuses them while scrolling. The row height is fixed: this removes measuring and
jumps and covers game lists. A row is a Gum `ListBoxItem` around the game's visual, so hover, press and selection look
as in a Gum list. Keyboard and gamepad navigation goes by data index, not by existing visuals.

## Which list to use

| List | From | Visuals | Row height | When |
| --- | --- | --- | --- | --- |
| `ListBox`, `ItemsControl` | Gum | one per data item | any | Menus, settings, lists up to a couple of hundred items, procedural windows |
| `StackPanel` in a `ScrollViewer` | Gum | every block created by hand | any | Procedural windows of blocks of different shapes: tooltips, descriptions, cards |
| `VirtualList<T>` | molib | only the visible rows plus a margin | the same | Hundreds and thousands of uniform rows: an inventory, a leaderboard |

molib scroll inertia and drag and drop work with all three. Snapping to items (`ScrollSnap.Items`,
[interaction/scrolling.md](../interaction/scrolling.md)) works only with Gum lists: `VirtualList` rows are reused, and
the `SnapChanged` index would not be a data index. Snapping to pages works with it too.

## What Gum does

Verified against the sources and by running Gum 2026.11.2.1:

- Gum has no list virtualization: `ListBox` and `ItemsControl` create a visual per item.
- `ScrollViewer` updates the scroll bar on `SizeChanged` of the inner panel and raises `ScrollChanged` on every change
  of the bar value.
- A control without its own template takes the visual of the nearest ancestor with a template
  (`GetGraphicalUiElementForFrameworkElement` walks the base types): `new VirtualList<T>()` gets the default
  `ScrollViewer` visual, with a bar and the `Focused` state.
- A `ListBoxItem` works on its own without a `ListBox`: it highlights on mouse hover, is selected by a mouse press and a
  touch without movement (`IsSelected`, the `Selected` event), and raises `Clicked` on a pointer click.
- The Gum V3 row visual writes the placeholder «ListBox Item» through the translatable `Text`, while `UpdateToObject`
  writes the item text around the translation; a language change would bring the placeholder back into every row
  ([integration/localization.md](../integration/localization.md), section «What Gum does»). For default rows the list
  keeps the placeholder as plain text.
- An element that got the focus (`IsFocused = true`) scrolls every scroll area above it to itself and watches its own
  visibility. So rows do not get the focus: the list moves the rows itself, and such a focus on a row in the margin
  would scroll the list to it.

## API

```csharp
var list = new VirtualList<InventoryItem>
{
    ItemHeight = 72,
    Columns = 1,
    CreateVisual = () => new ItemSlotVisual(),
    BindVisual = (visual, item, index) => ((ItemSlotVisual)visual).Show(item),
};
list.Items = inventory;
list.ItemActivated += item => Use(item);
list.UseInertia(molib);
```

- The constructor takes no `MolibService`: the list needs nothing from molib modules, and inertia attaches to it like
  to any `ScrollViewer`.
- `Items` — an `IReadOnlyList<T>`. If it is an `ObservableCollection<T>` or another collection with
  `INotifyCollectionChanged`, the list updates on `CollectionChanged`; otherwise, by calling `Refresh()`. A new
  collection clears the selection.
- `CreateVisual` — creates an empty row visual (`InteractiveGue`); called only when the pool is being filled. A visual
  with a `ListBoxItem` control keeps it, others get a new one. Not set — the default `ListBoxItem` visual. The list sets
  the visual's position and size. Set at creation (`init`). The order in an object initializer does not matter: rows
  that `Items` created earlier are thrown away and created again.
- `BindVisual` — fills a visual with the row's data; called when a row appears and when it is reused. Not set — the row
  shows the item text (`ListBoxItem.UpdateToObject`). Set after `Items`, it refills the rows already visible.
- `ItemHeight` — the row height in canvas units, 40 by default. `Columns > 1` — a grid: a row holds `Columns` cells,
  and the cell width is the list width divided by `Columns`. A height that is not a positive finite number or fewer
  than one column throws `ArgumentOutOfRangeException`.
- `SelectedIndex` (-1 — nothing), `SelectedItem`, the `SelectionChanged` event — on a selection by the player, by code
  and when the selected item is removed. A `SelectedIndex` set from code does not scroll the list.
- The `ItemActivated` event with the item — a click or touch on a row, `A`, Enter, Space; an activated item is always
  selected.
- `ScrollToIndex(index, ScrollAlign.Nearest | Start | Center | End)`; `Nearest` does not move a row that is fully
  visible. An `align` outside its named values throws `ArgumentOutOfRangeException`.

## Structure

- The `ScrollViewer`'s inner panel without children layout gets the height `ceil(count / Columns) · ItemHeight`, and
  the scroll bar works as usual. A row is put in its position: `Y = row · ItemHeight`, `X` and the width in percent of
  the panel width, so a width change recomputes nothing.
- The row range is the rows visible by the bar value and the area height plus one screen above and below; visuals are
  created only for it.
- While scrolling, visuals that left the range are hidden, returned to the pool and given to new rows with a new
  `BindVisual`. The range is recomputed on `ScrollChanged` and on a size change of the area: a frame without scrolling
  recomputes nothing.
- A collection change: if the list is scrolled, an insert or removal above the first visible row shifts the bar value
  by as many rows as that row moved, and the visible rows stay in place. At the very top, inserted rows appear on top.
  The selection follows its item; removing the selected item clears the selection.
- Inertia — [interaction/scrolling.md](../interaction/scrolling.md), attached the same way as to a `ScrollViewer`.

## Focus and gamepad

- The list takes the focus as one Gum control and shows it with the `Focused` state of its visual. The selection is
  drawn with the `Selected` state of a row, stored by index and survives visual reuse.
- The D-pad, the left stick and the arrows change `SelectedIndex`; in a grid, in every direction. A step down into the
  short last row of a grid goes to the last item. The first step without a selection selects the first fully visible
  row.
- A step outside the visible area first scrolls to the row (`Nearest`), the visual is created, and then the selection
  is drawn on it.
- A gamepad direction in which the selection cannot move (left and right in a one-column list, past the edge of a grid
  or list) moves the focus out of the list with Gum's general navigation — in order or spatially, as the container is
  set up. The keyboard arrows do not leave the list, Tab does: while the list is focused, molib keeps the arrows out of
  the general navigation, as with text fields and Gum lists
  ([interaction/focus-and-gamepad.md](../interaction/focus-and-gamepad.md), section «Keyboard arrows»).
- `PageUp` / `PageDown` — by the number of rows that fit into the visible area.
- `A`, Enter, Space — activate the selected item (without a selection, select the first visible row). The right stick
  scrolls, as in a `ScrollViewer`.
- The list does not take Esc: cancel stays with the molib layers ([interaction/layers.md](../interaction/layers.md)).

## Mouse and touch

- A row is selected and activated on release, like a button: a mouse click or a touch without movement. A mouse press
  only highlights the row; Gum's `ListBoxItem` selects itself already on the press, and the list gives the row back its
  previous look until release.
- Scrolling with a finger over the rows selects and activates nothing: Gum raises a click after a movement too if the
  finger is released over the same row, and the list skips such a click.

## Drag and drop

Rows are reused, so a drag source ([interaction/drag-drop.md](../interaction/drag-drop.md)) is registered once per
visual in `CreateVisual`, and `getData` reads the item the visual shows now: the game remembers the row index in the
visual in `BindVisual`.

## Limits

Rows of different heights are not supported and not planned: where heights differ, Gum lists are used (the table
above).
