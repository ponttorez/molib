# Gestures

## Decision

Press, release, click, double click, hover and drag come from Gum events (`Push`, `LosePush`, `Click`, `DoubleClick`,
`RollOn`, `RollOff`, `Dragging`). molib adds the one gesture that is missing — long press. It is built on the same
events and a timer in `MolibService.Update`; the click that Gum raises after a long press is skipped by the handler
using a flag.

## What Gum does

Verified against the Gum 2026.11.2.1 sources:

- A visual's `Push` (`InteractiveGue`) is raised only by the cursor — the mouse or a touch. The keyboard and a gamepad
  press the Forms control (`Button.Push`, `Button.Click`), not the visual.
- `LosePush` is raised on release (together with `Click`) and when the pointer leaves the element while pressed.
- `Click` is raised on release over the same element, however long it was held; it cannot be cancelled from outside.

## Long press

```csharp
LongPress longPress = button.Visual.UseLongPress(molib);
longPress.Triggered += (_, _) => OpenContextMenu(button);
button.Click += (_, _) =>
{
    if (longPress.ConsumedClick)
    {
        return;
    }

    Activate();
};
```

- An extension method on `InteractiveGue` in `Molib.Interaction`; for a Forms control, use its `Visual`. It returns a
  `LongPress`: the `Triggered` event, the `ConsumedClick` flag, `Element`, `Options`.
- A second call for the same element is a programmer error (`InvalidOperationException`). The registration lives in a
  weak table and does not hold the element.
- `LongPress.Detach()` takes the handlers off the element; after it the element may get a new long press. A second
  `Detach` does nothing; disposing the service detaches every long press
  ([foundation/architecture.md](../foundation/architecture.md), section «Detaching from elements»).
- It starts on the element's `Push`. The duration is 0.5 s (`LongPressOptions.Duration`, a positive finite number),
  in real time: `TimeScale` does not affect it.
- Cancelled by:
  - `LosePush` — release or the pointer leaving the element;
  - the pointer moving more than 10 canvas units from the press point;
  - the element itself moving more than 10 units — this is how any scroll of a parent cancels it: the wheel, inertia, a
    gamepad. When scrolling with a finger, the content moves with the finger, and the pointer movement fires first.
- `Triggered` is raised once per press while the pointer is held.
- `ConsumedClick` is `true` from the trigger until the next `Push`: Gum raises `Click` on release in this case too, and
  the click handler checks the flag. This is the only place where the game needs to know about the long press.
- Works for the mouse and touch. The keyboard and a gamepad have no long press: there the context action is a separate
  button.
- The molib list holds only presses being held: a frame without them does nothing.

## Dragging

Drag and drop between containers — [drag-drop.md](drag-drop.md). For dragging inside one element (the thumb of a custom
control, rotating a model), Gum's `Dragging` event and the cursor position are enough.

## Thresholds

Thresholds are in canvas units ([platform/scaling.md](../platform/scaling.md)), so on a phone and on a monitor they
feel the same relative to the interface size.
