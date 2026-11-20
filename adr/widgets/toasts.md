# Toasts

## Decision

A toast is a short message over the interface that goes away by itself. Gum has none. molib shows toasts in a container
on top of `PopupRoot`: they do not block input outside themselves, take no focus and are not part of the cancel stack.
The content of a toast is any Gum element or text; molib handles only the queue, placement, show time and animation.

## What Gum does

Verified against the sources and by running Gum 2026.11.2.1:

- Gum draws the children of a root in order, the last one on top; they get the cursor in the same order.
- While `ModalRoot` has visible children, Gum draws it over `PopupRoot` and gives the cursor only to the top modal
  window ([interaction/layers.md](../interaction/layers.md)).
- An empty `ContainerRuntime` takes events (`HasEvents` is on by default). A click goes to the deepest element with
  events, and `ClickBubbling` goes from it up through the ancestors until a handler marks it handled.
- A content-sized `Label` with `MaxWidth` wraps its text at that width.

## API

```csharp
molib.Toasts.Show("Saved");
ToastHandle toast = molib.Toasts.Show(customToastElement, new ToastOptions { Duration = 4f });
toast.Dismiss();
```

- A string is wrapped into the standard toast visual: a Gum `Label` on the `Bordered` 9-patch of the Gum theme in its
  dark gray color, with padding; the text grows up to 560 canvas units and wraps beyond that. A custom visual for
  strings is set by `molib.Toasts.DefaultVisualFactory`.
- The text goes through Gum localization like any `Text`: a string key may be passed
  ([integration/localization.md](../integration/localization.md)).
- A custom element sizes itself (absolute or by content): the toast takes its size and follows its `SizeChanged`. While
  shown, the element is taken out of its parent, and after it disappears, out of the toast. An element that is already
  in an open toast throws `InvalidOperationException`.
- `ToastOptions`: `Duration` (seconds, 2.5 by default; non-positive throws `ArgumentOutOfRangeException`), `Placement`,
  `DismissOnClick`.
- `Show` returns a `ToastHandle`: `Content`, `Options`, `IsOpen` (waiting in the queue or shown and not closed),
  `Dismiss()` — a queued toast will not appear, a shown one disappears.
- `molib.Toasts.VisibleCount` — toasts on screen, including the appearing and disappearing ones.

## Placement and queue

- The place is the `ToastPlacement` of each toast: `Bottom` (default), `Top`, `BottomEnd`, `TopEnd`; the margin from
  the edge is 24 canvas units inside the safe area ([platform/safe-area.md](../platform/safe-area.md)). Every place has
  its own stack and its own queue.
- Up to 3 toasts are visible in one place; the new one is closest to the edge, and the others move away from the edge
  with an animation. The fourth waits in the queue.
- A disappearing toast keeps its place until it has gone: its neighbours do not move into it. Then the others move
  towards the edge, and the next one from the queue appears.
- The show time is `Duration` in real time (`TimeScale` slows the animation, not the reading time). It counts only
  while the toast is fully visible. The mouse over a toast pauses the timer; a finger only while it touches the toast:
  after a touch Gum's cursor stays where the finger was.
- A click or a touch on a toast closes it if `DismissOnClick = true` (default). The click is caught by the bubbling
  `ClickBubbling` on the toast: usually the click goes to the content itself or an element inside it. A button inside a
  toast that must not close it marks `ClickBubbling` handled.
- The toast container and its safe area container take no events: only the toasts get the cursor, and buttons outside
  them are pressed as usual. While toasts are on screen, molib keeps their container the last child of `PopupRoot`, so
  a popup layer opened later does not cover it.
- Toasts take no focus, Tab and the D-pad do not see them, cancel does not close them. So a button in a toast (for
  example, «Undo» after «Thrown away») is available only to the mouse and touch: it may undo or repeat an action, but
  must never be the only way to do something. Verified by running.
- While a modal window is open, a toast is visible under its scrim and gets no cursor: this is how Gum draws the roots
  and hands them input (section «What Gum does»).
- A toast gets the current orientation state when shown ([platform/scaling.md](../platform/scaling.md), section
  «Orientation»).

## Animation

Show and hide — [motion/show-hide.md](../motion/show-hide.md), the preset `FadeSlideUp` (`FadeSlideDown` for `Top` and
`TopEnd`), appearing with the `Bouncy` spring (`WithMotion(MotionPreset.Bouncy)`). A toast is a container in the stack
the size of its content, and it is the content inside it that appears and disappears, so the shift on appearing does
not change the size of the stack. The other toasts shift with the `Smooth` spring when a new one appears. All three
movements go through the motion core: they obey `TimeScale` and count in `Motion.ActiveCount`.

The content fades through a render target the size of the content, so anything outside its bounds (a shadow, for
example) is cut off while the animation runs. A toast with a shadow keeps it inside its bounds: it puts its panel into a
container with room for the shadow ([motion/show-hide.md](../motion/show-hide.md), section «Transparency»). Verified by
screenshots: with such a container the toast's shadow is the same in the middle of appearing and after it.

## Zero work

molib keeps the list of toasts on screen; an empty list returns without work. While there are toasts, a frame checks
the timers, the toast under the cursor (walking up the ancestors from `VisualOver`) and the order of the container in
`PopupRoot`.

## Sound

Showing a toast is the sound event `ToastShown` ([integration/sound.md](../integration/sound.md)).
