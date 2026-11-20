# Sound

## Decision

molib plays no sounds and contains no sound files. It turns the user's actions in the UI into sound events and hands
them to the game through one event; the game decides which sound to play, and whether to play one at all. Events come
only from user actions: values changed by code stay silent.

## What Gum does

Verified against the sources and by running Gum 2026.11.2.1:

- `Push` and `Click` of a button control (`ButtonBase`: `Button`, `ToggleButton`, `CheckBox`, `RadioButton`) are raised
  by the pointer and by Enter, Space and `A`: from the keyboard and a gamepad, both at once at the moment of the press.
- `CheckBox` and `RadioButton` derive from `ToggleButton`. Values set in code (`IsChecked`, `Slider.Value`) raise
  neither `Click` nor `ValueChangedByUi`.
- `SelectionChanged` of `ListBox` and `ComboBox` is raised by a selection from code too.
- A disabled control gets neither `Push` nor `Click` from the cursor, but Gum puts it under the cursor (`VisualOver`).
- The `ComboBox` drop-down (`ComboBox.ListBox`) lives in `PopupRoot`, outside the visual of the `ComboBox` itself.
- A visual's `RollOn` is raised when the cursor enters the element.

## Attaching

```csharp
molib.Sound.Triggered += (kind, element) => _audio.PlayUi(kind);
molib.Sound.Attach(mainMenuScreen);
```

- The `Triggered` event (`Action<UiSoundKind, GraphicalUiElement>`) carries the kind of event and the element it
  happened on.
- `Attach(element)` walks the subtree once and subscribes to the events of the Forms controls. Attaching the same
  control again does nothing; controls added to the subtree later are attached by a new call.
- The walk does not go inside controls built from parts (buttons, sliders, `ComboBox`, text fields, `ListBox`), and in
  a `ScrollViewer` it goes only into the content: scroll bars and thumbs stay silent.
- The subscriptions live on the controls themselves. A control the game discards takes them with it, and a control
  removed from the tree gets no input and stays silent; returned to the tree, it sounds again.
- `Detach(element)` walks the subtree the same way and takes the handlers off the attached controls in it — for a
  screen whose controls outlive it ([foundation/architecture.md](../foundation/architecture.md), section «Detaching
  from elements»). molib keeps the handlers of each control in a weak table to take exactly them off; disposing the
  service detaches every control.
- molib attaches the content of layers and toasts itself when it shows them.
- Dynamic list rows sound through the events of the list itself (`SelectionChanged`), not of the rows.

## Events

| `UiSoundKind` | Source | When | Element |
| --- | --- | --- | --- |
| `Hover` | `RollOn` of the visual of a button, check box, toggle, slider, `ComboBox`, text field | The mouse entered an enabled control that responds to hover; labels and panels stay silent; not on touch | control |
| `Press` | `Push` of `ButtonBase` | Pressing a button, check box, toggle: pointer, Enter, Space, `A` | control |
| `Click` | `Click` of a button | Activating a button (mouse, touch, Enter, Space, `A`) | control |
| `Toggle` | `Click` of `ToggleButton`, `CheckBox`, `RadioButton` | Toggling by the user | control |
| `ValueStep` | `ValueChangedByUi` of `Slider` | At most once per 50 ms of real time | control |
| `Select` | `SelectionChanged` of `ListBox`, `ComboBox`; a user selection in `VirtualList` | Selection by the user | list |
| `Focus` | `GotFocus` when `molib.Input.LastDevice` is navigation ([interaction/focus-and-gamepad.md](../interaction/focus-and-gamepad.md)) | Focus navigation | control |
| `Denied` | A cursor press with a disabled control under it | Pressing something unavailable | control |
| `LayerOpened`, `LayerClosed` | [interaction/layers.md](../interaction/layers.md) | Showing a layer; the start of closing | layer content |
| `ToastShown` | [widgets/toasts.md](../widgets/toasts.md) | Showing a toast | toast content |
| `DragStarted`, `DragDropped`, `DragCancelled` | [interaction/drag-drop.md](../interaction/drag-drop.md) | Drag start, a successful drop, cancel | the source; the target for `DragDropped` |
| `SnapChanged` | [interaction/scrolling.md](../interaction/scrolling.md) | A snapping scroll settled on a new item or page | scroll area |

- molib finds `Denied` itself: Gum does not raise `Push` on a disabled control. One cursor press flag is read per frame;
  on a press, molib walks up from `VisualOver` to the nearest control. Only an attached control sounds.
- Events of layers, toasts, drag and drop and snapping come from molib modules and need no `Attach`.

## Silence for programmatic changes

- Values set from code (`slider.Value = 0.5f`, `checkBox.IsChecked = true`) produce no events: molib listens only to
  the events Gum raises from input (`Click`, `Push`, `ValueChangedByUi`).
- `SelectionChanged`, which Gum also raises for a programmatic selection, counts as a user selection only if there was
  input on the list this frame: a pointer press or click on the list or its drop-down, or the list (or its drop-down)
  in focus with a navigation button or key pressed or repeating this frame. Gum's per-frame input state is read right
  in the handler, so the check is right from inside a Gum event too.
- `VirtualList` knows itself where a selection came from ([widgets/virtual-list.md](../widgets/virtual-list.md)).

## Turning off

- `molib.Sound.Enabled = false` — globally.
- `molib.Sound.Mute(element)` — for the element and its descendants, including the events of layers, toasts, drag and
  drop and snapping on them; `Unmute(element)` brings the sound back.
