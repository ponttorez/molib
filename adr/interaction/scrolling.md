# Scrolling

## Decision

Gum's `ScrollViewer` does scrolling with the wheel, the bars, the right stick and a finger (`ListBox`, `ItemsControl`,
`ComboBox` and `Menu` are built on it). What is missing is inertia and snapping: after the finger is released, the
content stops at once. molib adds them from the outside: while the finger is on the area, it records how
`VerticalScrollBarValue` and `HorizontalScrollBarValue` change, and after release it continues the motion with decay or
leads the position to the nearest item or page with a spring. The area tells molib about presses and value changes
through its events, so an area that does not move costs nothing per frame. There is no rubber-band edge: Gum clamps the
value to the bar range.

## What Gum does

Verified against the sources and by running Gum 2026.11.2.1 (touch through its own cursor,
`FormsUtilities.SetCursor`):

- A touch scrolls an area by exactly the finger movement in canvas units (`YChange / zoom`), only while the finger is
  over the area and not in the frame of the touch (the `RollOverBubbling` event). A nested area gets it first and marks
  it handled: the outer one under the finger does not scroll.
- Gum does not drag content with the mouse. The wheel scrolls in jumps of `MouseWheelScrollSpeed` (30 units per notch),
  without smoothing; horizontally with Shift.
- `ScrollBar.Value` is clamped to `Minimum` and `Maximum`: assigning past the edge gives the edge.
- Focus from the keyboard or a gamepad scrolls to the element at once, without animation; focus from the mouse or touch
  does not.

## Attaching

```csharp
ScrollInertia inertia = scrollViewer.UseInertia(molib);
scrollViewer.UseInertia(molib, new InertiaOptions { Friction = 4f });
```

- An extension method on `ScrollViewer` in `Molib.Interaction`; it works for all subclasses. It returns a
  `ScrollInertia`: `Viewer`, `Options`, `SnapIndex`, the `SnapChanged` event.
- A second call for the same area is a programmer error (`InvalidOperationException`). `Friction` is a positive finite
  number, and `Snap` and `SnapAlign` are named values of their enums, otherwise `ArgumentOutOfRangeException` where the
  option is set.
- `ScrollInertia.Detach()` stops the motion where it is, takes the handlers off the area, removes the paging mark of a
  `Pages` area, and lets the area get new inertia. A second `Detach` does nothing; disposing the service detaches
  every area ([foundation/architecture.md](../foundation/architecture.md), section «Detaching from elements»).
- molib finds the area from the focused element through a weak table: the attachment does not hold an area the game
  discarded.

## Measuring velocity

- A press on the area — on the content, on a button inside, on a bar — reaches it through the `PushPreview` event
  before the element under the cursor. From this frame until release molib records the changes of both bar values and
  the frame time into a ring buffer of the last 5 frames.
- The velocity on release is the average over the frames of the last 0.1 s. If the finger stood still before release
  (the last 0.05 s without changes), the velocity is 0.
- Only a touch throws (`Cursor.LastInputDevice` is `TouchScreen`): Gum does not drag with the mouse, and the wheel and
  the bar are precise.
- A press stops the inertia and the spring of this area.
- Drag and drop ([drag-drop.md](drag-drop.md)) takes the pointer: the press record ends without a throw, and edge
  scrolling during a drag does not start a throw.

## Motion

- `v(t) = v0 · exp(-Friction · t)`; the bar value moves by the exact integral per frame,
  `v / Friction · (1 - exp(-Friction · Δt))`. `Friction` is 4 by default — about a second to stop, a path of `v0 / 4`.
- It stops when: `|v|` is below 5 units/s, Gum clamped the value at the range edge, a new press on the area, any value
  change not made by molib (the wheel, a bar, the right stick, focus, game code).
- The motion time is multiplied by `molib.Motion.TimeScale`, and running inertia or a spring counts as one animation in
  `Motion.ActiveCount` ([motion/motion-core.md](../motion/motion-core.md), section «Scrolling»). Measuring velocity and
  waiting before settling go in real time.

## Focus and scrolling

Gum itself scrolls to an element that got the focus from the keyboard or a gamepad. molib checks the focus once per
frame (one reference comparison) and, when the focus moved inside an area with inertia, stops its inertia: otherwise
the inertia would carry the selected element away.

Gum does not scroll to a text field that got the focus by a touch: it is visible at the moment of the touch. On Android
the screen keyboard then covers the lower part of the screen, and MonoGame does not report its size. So after the
finger is released molib scrolls such a field with a spring so that its top stands 16 units below the top of the area
— if the top of the field was below the upper third of the area ([platform/text-input.md](../platform/text-input.md)).

## Snapping

For carousels, level selection, a shop, pages:

```csharp
ScrollInertia carousel = list.UseInertia(molib,
    new InertiaOptions { Snap = ScrollSnap.Items, SnapAlign = SnapAlign.Center });
carousel.SnapChanged += (_, e) => dots.Select(e.Index);
pages.UseInertia(molib, new InertiaOptions { Snap = ScrollSnap.Pages });
```

- `ScrollSnap.None` (the default); `Items` — to a visible child of the inner panel (`InnerPanel`); `Pages` — to the
  edges of pages the size of the visible area (`ClipContainer`), the last page being the range edge.
- `SnapAlign` for `Items`: `Start`, `Center`, `End` — which point of the item lines up with the same point of the area.
  A point past the range edge is clamped to the edge: the first card with `Center` settles at the start.
- **Axis.** Snapping goes vertically if the vertical bar has a range, otherwise horizontally. The other axis only rolls
  with inertia.
- **Where to settle.** On a release with a throw the stopping point is predicted: `value + v0 / Friction`. The snap
  point nearest to the prediction is chosen, and the `MotionPreset.Smooth` spring leads the value from the current one,
  with the current throw velocity, to that point. So a throw does not «roll back» at the end.
- **`Pages`:** a throw pages at most one page away from the page where the press was.
- **Settling.** After a release without a throw and after any value change not made by molib (the wheel, a bar, the
  right stick, game code), the position settles to the nearest point once there were no new changes for 0.15 s.
- **Focus from the keyboard or a gamepad.** Gum scrolls to the element in a jump. molib puts the value back where it was
  before the jump and leads it with a spring to the element's snap point: a card slides smoothly into the middle, a page
  into place. For `Pages` the element's page is the one holding its center.
- **Paging.** A scroll with `Pages` marks itself as a paging area ([focus-and-gamepad.md](focus-and-gamepad.md), section
  «Paging: LB / RB»): LB / RB and Ctrl+PageUp / Ctrl+PageDown page it by one page. If the focus is inside the area, it
  moves to the first focusable element of the new page: otherwise the next D-pad step would start from the old page,
  and Gum would scroll back to it.
- `SnapChanged` is an event with the index of the item (among the visible children of the inner panel) or of the page,
  if the area stopped on a different one than before. `SnapIndex` is the last such index, `-1` before the first stop.
  The same stop is the sound event `SnapChanged` ([integration/sound.md](../integration/sound.md)).
- Snap points are computed when a target is chosen — on release, settling, focus, paging — not every frame.

## Zero work

- molib subscribes to the area visual's `PushPreview` and to its `ScrollChanged` once, when attaching. molib marks its
  own writes of the values, and they do not count as an outside change.
- The active list holds only areas that are pressed, rolling, settling or waiting to settle. An empty list returns
  without work.
- Walking up the tree from the focused element happens only when the focus changed.

## Limits

- No rubber-band edge and no overshoot past the edge: Gum clamps the value to the range.
- No smooth wheel scrolling: the wheel works as in Gum.
