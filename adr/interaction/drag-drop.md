# Drag and drop

## Decision

molib adds moving elements between containers — an item from the inventory into an equipment slot, an ability onto the
quick bar. Gum gives only the raw material: the cursor knows what was pressed (`Cursor.VisualPushed`) and what is under
it now (`VisualOver`), and an element gets `Dragging`. molib builds the full gesture from it: sources and targets are
registered, a copy of the element follows the pointer above the interface, a suitable target lights up, and a release
outside returns the copy to the source. For the keyboard and a gamepad the same move is done with one button: «pick up»
and «put down». The drag data is the game's object; molib does not know what is moved, and the game decides whether it
may be dropped somewhere. Gum itself reorders rows inside one `ListBox`.

## What Gum does

Verified against the sources and by running Gum 2026.11.2.1:

- `GraphicalUiElement.Clone()` copies one element without its children: a copy of a button visual has no children. The
  copy is visual only: Gum removes its Forms control and event handlers.
- `ListBox.DragDropReorderMode = Immediate` reorders the rows of a list while they are dragged with the mouse or a
  finger (from the sources).
- Only the last visible child of `ModalRoot` gets cursor input, and Gum draws it on top of everything, including
  `PopupRoot`. A container without events in that place (`HasEvents = false`, `ExposeChildrenEvents = false`) cuts the
  cursor off from everything else: a touch does not scroll the area under the finger, and `VisualOver` is empty.
- Gum presses the focused button (`Click`) at the moment `A`, Enter or Space is pressed — before molib sees the frame.
- An element has no render scale ([motion/show-hide.md](../motion/show-hide.md), section «Presets»).

## API

```csharp
var drag = molib.DragDrop;

drag.AddSource(slotVisual, () => slot.Item);                     // null: nothing to drag
drag.AddTarget(equipSlotVisual,
    canDrop: data => data is Weapon,
    drop: data => Equip((Weapon)data));
drag.Dropped += (_, e) => PlayDropSound();
```

- `AddSource(visual, getData, options)` — the element can be dragged: a press on it or on something inside starts the
  drag. `getData` is called when the drag starts; `null` cancels the start.
- `AddTarget(visual, canDrop, drop)` — the element takes drops; `canDrop` is called when the drag comes onto the
  target, `drop` on release over a target for which `canDrop` returned `true`.
- An element may be both a source and a target (inventory slots). A repeated `AddSource` or `AddTarget` replaces the
  previous parameters.
- `DragOptions` of a source: `Preview` — a factory of the copy visual from the data, by default a copy of the source
  visual with all its children; `Threshold` — 8 canvas units; `HideSourceWhileDragging`.
- `molib.DragDrop`:
  - the events `Started`, `Dropped`, `Cancelled` with `DragEventArgs`: `Data`, `Source`, `Target`;
  - the `IsDragging` property;
  - `Pick(visual)` — pick up a source from game code, like the «pick up» button;
  - `Cancel()`;
  - `PickButton` and `PickKey` — the «pick up / put down» button and key.
- Registrations live in a weak table: an element the game discards goes away with its registration, and one removed
  from the tree never ends up under the pointer, so there is no need to remove it on deletion. `Remove(visual)` removes
  it explicitly; a drag from this source is cancelled.

## Pointer: mouse and touch

1. **Start.** A press on a source and a movement beyond `Threshold`. If the source is inside a scroll area, a touch
   along its axis (by the dominant axis of the movement) goes to the scroll, across it to the drag; along the axis the
   drag starts if the finger holds still for 0.5 s. Gum does not drag content with the mouse, so the mouse starts a
   drag in any direction.
2. **The copy.** A full-screen container without events goes on top of everything in `ModalRoot`, with the copy in
   it: `Preview(data)` if set, otherwise a copy of the source visual with all its children (molib copies the tree
   element by element, because `Clone()` does not copy children). The copy, the size of the source, follows the
   pointer with the offset of the grab point and is translucent (a render target, alpha 200 of 255). The source stays in
   place dimmed (100 of 255) or transparent (`HideSourceWhileDragging`); it keeps its place in the layout. The copy is
   not enlarged: Gum has no render scale, and changing sizes relays out the content.
3. **Gum's cursor.** While the copy container is on top of `ModalRoot`, Gum gives the cursor to nobody: a touch does not
   scroll the area under the finger, hover does not light up buttons, a release does not count as a click for the
   source or a target. molib does the edge scrolling.
4. **The target under the pointer.** Gum finds nothing under the cursor meanwhile, so molib checks the targets.
   - The target list is collected once, at the start of the drag: targets inside the top modal window if one was open,
     otherwise under `Root` or `PopupRoot`.
   - A target under the pointer is visible, with the pointer inside its bounds and inside every ancestor with
     `ClipsChildren`: a row scrolled past the edge of a scroll area is not a target. Of nested targets the inner one is
     chosen; the source itself is never a target.
   - On a target change `canDrop` is called, and the target gets a state of the category `DropTarget`: `Accept` or
     `Reject`, and without a target, `None`. The target's visual draws the highlight with its states, and the
     transitions animate like any others ([motion/state-transitions.md](../motion/state-transitions.md)).
   - A target without the `DropTarget` category is not highlighted: the diagnostic `dragDrop.noDropTargetCategory`.
5. **A release on a suitable target** — `drop(data)`, the `Dropped` event; the copy flies into the center of the target
   with the `Smooth` spring and fades. `drop` is called before the flight, so the target already shows the item when
   the copy arrives.
6. **A release outside, on an unsuitable target or on the source itself** — the `Cancelled` event; the copy returns to
   the source and fades.
7. **Cancel** ([layers.md](layers.md)) during a drag cancels the drag and does not close the layer. While the copy is
   still flying into place, the cancel is swallowed: the press belongs to the drag.
8. A new drag while a copy is flying ends its flight at once.

## Keyboard and gamepad

- On a focused source the gamepad `X` button or the `X` key (`DragDrop.PickButton`, `DragDrop.PickKey`) means «pick
  up». Not `A` and not Enter: Gum presses the focused button at the moment of the press, before molib, and that click
  cannot be cancelled; holding `A` would also be a long press on a gamepad, which molib does not have.
- `PickKey` is a navigation key ([focus-and-gamepad.md](focus-and-gamepad.md), section «Last device»): a press that
  brought back the hidden focus picks up nothing. While the focus is in a text field, the key types and does not pick
  up.
- There is no copy: the source dims, and the focused target gets `Accept` or `Reject`. Over a modal window Gum draws
  only the window itself: a copy for navigation would have to lie in `ModalRoot` and take the focus from the window,
  and a copy inside a foreign window would change its layout.
- The focus moves as usual during the drag — in element order or spatially, by Gum's means: limiting it to targets
  would take a walk over every element of the tree or a navigation of our own.
- `X` on a focused target that accepts the data means «put down»; on the source itself, cancel (put it back); on
  anything else, nothing, and the drag goes on. `B` or Esc cancels. A pointer press during such a drag cancels it.
- `Pick(visual)` starts the same drag from game code, for example from a button of its context menu.

## Scrolling during a drag

While the pointer stays at the edge of a scroll area that contains targets or the source, the area scrolls at a speed
from 0 at the inner side of a 32-canvas-unit strip to 600 units/s at the very edge. The areas — the `ScrollViewer`
ancestors of the targets and the source — are collected at the start of the drag. For `VirtualList` it is the same, and
rows are created as it scrolls ([widgets/virtual-list.md](../widgets/virtual-list.md)). Scroll inertia
([scrolling.md](scrolling.md)) does not start afterwards: the drag ends the press record without a throw.

## Zero work

Without a drag, `Update` reads only the cursor press and the «pick up» button from Gum's ready input state.
Registrations are walked once per drag, when it starts.

## Sound

`DragStarted`, `DragDropped`, `DragCancelled` are sound events ([integration/sound.md](../integration/sound.md)).

## Limits

- Reordering rows inside one `ListBox` — Gum's `ListBox.DragDropReorderMode = Immediate`. For other containers
  reordering is the same drag: the elements are registered as both sources and targets, and the game's `drop` does the
  reordering. There is no animation of neighbours moving apart.
- Dragging between OS windows and dropping files into the game — not done.
