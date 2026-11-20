# Layers and cancel

## Decision

Gum already does modality, blocking input below a window and keeping the focus inside the top modal window through
`ModalRoot`. molib adds what Gum lacks: showing and closing a layer with an animation and a scrim, an initial focus,
returning the focus after closing, and a shared cancel stack — Esc, gamepad B and Android back close the top layer, and
with no layers the game gets the cancel.

## What Gum does

Verified against the Gum 2026.11.2.1 sources:

- Only the last visible element of `ModalRoot` gets input; everything else — `Root`, `PopupRoot` and the modal elements
  under it — gets none.
- Tab and gamepad navigation do not leave the top modal window and go around in a circle.
- `PopupRoot` is a layer over the tree without input blocking.
- Gum puts the `ComboBox` drop-down into `PopupRoot`, and if the `ComboBox` itself lies in `ModalRoot`, into
  `ModalRoot` too, as the top element. Esc and B close it and return the focus.
- `TextBox` raises `EditCancelled` on Esc and keeps the focus.
- A `ListBox` with the focus on its rows leaves them on B; a plain list does not handle Esc.
- `RectangleRuntime` takes no input (it is not an `InteractiveGue`), while `ContainerRuntime` has `HasEvents` on by
  default: an empty full-screen container intercepts clicks for everything under it.

## Showing a layer

```csharp
LayerHandle dialog = molib.Layers.ShowModal(confirmDialog, new LayerOptions
{
    InitialFocus = yesButton,
    DismissOnScrimClick = false,
    Animation = ShowHide.FadeSlideUp,
});
LayerCloseReason reason = await dialog.Closed;
```

- `ShowModal` puts the layer container into `ModalRoot`: a full-screen scrim and the content in the safe area
  ([platform/safe-area.md](../platform/safe-area.md)).
- `ShowPopup` — the same in `PopupRoot`, without a scrim and without input blocking.
- The content is any Gum element (for a Forms control, its `Visual`). molib moves it into the layer; an element already
  shown in an open layer is a programmer error (`InvalidOperationException`).
- It returns a `LayerHandle`: `CloseAsync()`, the `Closed` task (the result is a `LayerCloseReason`: `Code`, `Cancel`,
  `ScrimClick`), `IsOpen`, `Content`. The task completes on the game thread when the layer is already removed from the
  tree and the focus returned.
- `molib.Layers.OpenCount` — the number of open layers; a closing layer does not count.
- The layer gets the current orientation state ([platform/scaling.md](../platform/scaling.md)).

### Options

`LayerOptions` (all properties are `init`):

| Property | Default | What it does |
| --- | --- | --- |
| `InitialFocus` | none | The element that gets the focus on opening |
| `CloseOnCancel` | `true` | Cancel closes the layer; `false` — cancel is swallowed (a required choice) |
| `DismissOnScrimClick` | `true` | A click or touch on the scrim closes a modal layer |
| `Animation` | `ShowHide.FadeSlideUp` | Showing and hiding the content ([motion/show-hide.md](../motion/show-hide.md)) |
| `ScrimColor` | black, alpha 160 of 255 | The scrim color of a modal layer |
| `CenterContent` | `true` | Puts the content in the center of the safe area; `false` — the game places it itself |

### Layer structure

- The layer root is a full-screen container with `HasEvents = false`.
- The scrim (only in a modal layer) is a full-screen container that takes the click, with a rectangle of `ScrimColor` in
  it. The rectangle fades with its own color (`ShowHide.Fade` with the preset's motion), separately from the content,
  without a full-screen render target.
- The safe area is a container attached to `molib.SafeArea`, with `HasEvents = false`, so a click outside the content
  reaches the scrim.
- The content appears and disappears through `ShowAsync` / `HideAsync` with the `Animation` preset. With `CenterContent`
  molib sets `X = Y = 0` in `PixelsFromMiddle` units with the origin at the center — the preset's slide still works.
- After closing, the layer root is removed from the tree, and the content is invisible and has no parent; it can be
  shown again with a new `ShowModal` or `ShowPopup`.

## Focus

- **Initial:** `InitialFocus` if it is valid (visible, enabled, inside the layer), otherwise the first focusable element
  of the layer in Gum order, otherwise none.
- The initial focus is set only if the last device is navigation (`molib.Input.LastDevice`,
  [focus-and-gamepad.md](focus-and-gamepad.md)); with the mouse and touch it is remembered and appears on the first
  gamepad or keyboard press.
- **Focus restore:** on opening, the focused element is remembered, or, if the focus is hidden, the one `molib.Input`
  remembered. When closing starts, molib takes the focus off if it is inside the layer: a gamepad does not drive a
  disappearing window. After the layer is removed from the tree, the focus returns to the remembered element if it is
  valid, otherwise to the first focusable element of the top remaining modal window or of the screen
  ([focus-and-gamepad.md](focus-and-gamepad.md), «Focus visibility»).
- Only a layer that was on top when it started closing returns the focus, and only if no new layer opened while it
  disappeared: otherwise the return would take the focus out of the new window.

## Cancel

Cancel is one action from three sources:

| Source | How it is read |
| --- | --- |
| Keyboard | `Esc` of any keyboard in `FrameworkElement.KeyboardsForUiControl` |
| Gamepad | `B` of any gamepad in `FrameworkElement.GamePadsForUiControl` |
| Android | back — the `Back` button of the Gum gamepad in slot 0 (`GumService.Gamepads[0]`) |

- **Android.** MonoGame 3.8.6.1 reports the system back as `Back` in `GamePad.GetState(0)` in exactly one read and only
  while slot 0 has no real gamepad (`GamePad.Android.cs`). Gum reads this state every frame, so molib takes the button
  from the Gum gamepad, not from MonoGame. With a gamepad connected in slot 0 the system back does not reach the game —
  a MonoGame limitation; the gamepad's B gives the cancel then.
- Cancel fires once per press, without auto-repeat.
- **Gum answers first.** molib updates after Gum and sees the frame where Gum has already handled the press: for
  example, closed a drop-down. So at the end of every `MolibService.Update` molib remembers the focused element and its
  mode — focus on the rows of a `ListBox` and a `ScrollViewer`, whether a `ComboBox` is open — and in a frame with a
  cancel compares against it. If any of this changed, Gum answered the press, and molib leaves it alone.

Who gets the cancel, in order:

1. A drag is running — the drag is cancelled; while the drag copy flies into place, the cancel is swallowed
   ([drag-drop.md](drag-drop.md)). The layer under the drag does not close.
2. Gum has already answered the press (see above): it closed a `ComboBox` drop-down, took the focus out of the rows of
   a `ListBox` or `ScrollViewer`, closed a `Menu` item.
3. A focused text field — takes the focus off it ([platform/text-input.md](../platform/text-input.md)); the field is
   remembered, and the first navigation press returns the focus to it.
4. A foreign element (not a molib layer) is on top of `ModalRoot` — the `CancelUnhandled` event: the game closes its
   window. molib layers under it do not close. On the first encounter — the diagnostic `layers.foreignModal`.
5. The top molib layer with `CloseOnCancel = true` — closes with the reason `Cancel`.
6. A layer with `CloseOnCancel = false` — the cancel is swallowed (a required choice).
7. No layers — the `molib.Layers.CancelUnhandled` event: the game opens the pause menu or quits. On Android without a
   handler nothing happens: closing the game is the game's decision.

- A B press that brought back the hidden focus ([focus-and-gamepad.md](focus-and-gamepad.md), «Focus visibility») does
  not cancel: the focus changed in this frame, as with a Gum answer. Esc and back do not bring the focus back and
  cancel at once.
- A layer that is already closing takes no part in the cancel stack: a second cancel during the disappearance closes
  the next layer.
- Gum does not handle Esc in a plain list with the focus on its rows, so it goes further down the list and closes the
  layer.
- Focus that game code changes after `MolibService.Update` is seen by molib a frame later: a cancel pressed in exactly
  the next frame is taken for a Gum answer and skipped.

## Sound

Showing a layer and the start of its closing are the sound events `LayerOpened` and `LayerClosed`; molib attaches the
layer content to the sound events when showing it ([integration/sound.md](../integration/sound.md)).

## Limits

- Layers the game puts into `ModalRoot` itself, bypassing molib, take no part in the cancel stack and the focus return.
  molib notices such an element only when it is on top and Gum did not answer the cancel: a `ComboBox` drop-down in
  `ModalRoot` therefore is not taken for a foreign window.
- Closing a layer removes it from `ModalRoot`; the element can be reused by showing it again.
