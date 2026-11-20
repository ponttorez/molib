# Text input

## Decision

Text input stays entirely with Gum: `TextBox` and `PasswordBox` already take text from the keyboard on desktop, show
the built-in screen keyboard on Android and the system input dialog on iOS when they get the focus. molib adds only the
agreement with cancel: Esc or back while a field is being edited takes the focus off the field instead of closing the
layer.

## What Gum gives

- Desktop: input through the MonoGame keyboard, selection, the clipboard, the caret.
- Android: on focus `TextBox` shows the built-in keyboard and hides it when the focus leaves.
- iOS: on focus it opens the system dialog (`KeyboardInput.Show`) and writes the result into the field.
- The control's `ShowNativeKeyboardOnFocus` property controls this.
- Esc in a field raises `EditCancelled` and leaves the focus on the field: the handler decides what to restore and
  where to move the focus (verified against the Gum 2026.11.2.1 sources).
- A field that got the focus from the keyboard or a gamepad scrolls every `ScrollViewer` above it to itself. When the
  focus comes from the mouse or a touch, Gum does not scroll: the field is visible at the moment of the touch.

## What molib adds

- **Cancel.** While the focus is in a `TextBox` or `PasswordBox`, the first cancel
  ([interaction/layers.md](../interaction/layers.md)) takes the focus off the field, and only the next one closes the
  layer. Gum's `EditCancelled` handler fires as usual.
- **Arrows and focus.** The arrows in a field move the caret, not the focus; with the mouse and touch the focus is not
  taken off the field ([interaction/focus-and-gamepad.md](../interaction/focus-and-gamepad.md)).
- **The field above the Android keyboard.** If a field in a scroll area got the focus by a touch, the keyboard may
  cover it, and Gum does not scroll in this case. In an area with molib inertia (`UseInertia`), after the finger is
  released molib scrolls a field whose top is below the upper third of the area so that its top stands 16 units below
  the top of the area; this is part of the scrolling module ([interaction/scrolling.md](../interaction/scrolling.md),
  section «Focus and scrolling»). If the field is not in such a scroll area, the game places it in the upper part of
  the screen itself: MonoGame does not report the keyboard size.

## Limits

IME composition (Chinese, Japanese, Korean) is not supported and not planned.
