# Focus and gamepad

## Decision

Gum Forms does focus navigation: a gamepad and the keyboard move the focus in element order or spatially (by direction
on screen), A and Enter press, the D-pad and the left stick move, the right stick scrolls, a modal window keeps the
focus inside, and a gamepad connected on the fly drives the interface at once. molib does not replace this, but adds
what a game with a gamepad is awkward without: connection events and a choice of one gamepad for the menus; the last
device (pointer or navigation); the focus hides while the mouse and touch are used and comes back to its previous place
on the first gamepad or keyboard press; every screen has an initial focus; the keyboard arrows move like the D-pad; the
shoulder buttons page through tabs and pages. Every molib module with input works with a gamepad — this is a
requirement for each of them.

## What Gum gives

Verified against the sources and the package of Gum 2026.11.2.1:

- `GumService.UseGamepadDefaults()` / `UseKeyboardDefaults()` — turn control on; the gamepads that drive the UI are
  listed in `FrameworkElement.GamePadsForUiControl`, the keyboards in `KeyboardsForUiControl`.
- **Hot plugging.** Every frame Gum reads all four MonoGame gamepad slots by number (`GumService.Gamepads`), connected or
  not, and `UseGamepadDefaults` adds all four to `GamePadsForUiControl`. A gamepad connected during the game drives the
  interface at once, a disconnected one simply falls silent; a Gum gamepad has `IsConnected` and
  `WasDisconnectedThisFrame`.
- Navigation in element order (the default) and spatial navigation (`GamepadNavigationMode.Spatial` on a container, for
  grids, skill trees, scattered HUDs), explicit direction overrides (`SpatialNavigationUp/Down/Left/Right`).
- `A` presses the focused element; `ListBox` — entering the rows with `A` or Enter, moving with the D-pad and arrows;
  `Slider` — the value left and right; `ScrollViewer` — the right stick.
- The focus does not leave the top modal window ([layers.md](layers.md)).
- `InteractiveGue.CurrentInputReceiver` is the focused element; `Cursor.LastInputDevice` is only the mouse or touch, it
  has no gamepad and keyboard.
- `FrameworkElement.HandleTab(TabDirection.Down, null, container, false, false)` puts the focus on the first focusable
  element of the container (verified by running).
- An element that became invisible loses the focus by itself. An element removed from the tree keeps the focus
  (verified by running): `CurrentInputReceiver` points at it, and navigation has nothing to start from.
- Getting the focus from the keyboard or a gamepad, an element scrolls every `ScrollViewer` above it to itself; with the
  focus from the mouse or touch it does not.
- A `ScrollViewer` is itself a focus stop (it takes input): Tab and the D-pad stop on it, and only `A` or Enter let in,
  and only into a direct child that takes input itself. To let the focus pass into the scroll content in element order,
  the `ScrollViewer` gets `GamepadTabbingFocusBehavior = SkipOnTab`: Gum skips such an element and enters its
  descendants.

## What molib adds

While the game window is inactive (`Game.IsActive`), Gum does not read input and the buttons stay in their last state;
`molib.Input` and the layer cancel do nothing then either, otherwise one press would fire every frame.

### Gamepads

- The events `molib.Input.GamepadConnected` and `GamepadDisconnected` with the slot number (0–3) — for any of the four
  Gum gamepads. molib compares `IsConnected` with the last frame; gamepads connected when the service is created give
  no events. Pausing the game on a disconnect is the game's decision.
- `molib.Input.UiGamepads = UiGamepads.Only(index)` — only this gamepad drives the menus (several players, the menu
  belongs to one); `UiGamepads.All` — all four again. molib rebuilds only the Gum gamepads in `GamePadsForUiControl`
  and leaves foreign `IGamePad` objects there. Until the property is set, molib does not touch the list:
  `UseGamepadDefaults` fills it. Disposing the service puts all four Gum gamepads back if the property was set
  ([foundation/architecture.md](../foundation/architecture.md), section «Disposal»).

### Last device

- `molib.Input.LastDevice`: `Cursor` (mouse, touch — in Gum this is the cursor) or `Navigation` (gamepad, keyboard);
  the event `LastDeviceChanged`. Layers (initial focus), sound (`Focus`) and the game use it — for button hints («A —
  select» or nothing).
- `molib.Input.LastGamepadIndex` — the slot number of the gamepad pressed last, or `-1`; the game chooses the hint style
  (Xbox, PlayStation, Nintendo). A change of the number also raises `LastDeviceChanged`.
- **Pointer:** mouse movement, a press of any mouse button, the wheel, a touch.
- **Navigation:** any button or stick of a gamepad from `GamePadsForUiControl` except `Back` and `BigButton` (on Android
  `Back` is the system back button, not a gamepad: see [layers.md](layers.md), «Cancel»); the keys Tab, the arrows,
  Enter, Space, PageUp, PageDown and the drag «pick» key (`DragDrop.PickKey`, [drag-drop.md](drag-drop.md)). While a
  text field is focused, all of them except Tab are text editing and do not change the device; Tab does.
- The initial value: `Navigation` if at least one gamepad is connected when the service is created, otherwise `Cursor`.

### Focus visibility

- While the last device is the pointer, molib remembers the focused element every frame and takes the focus off it: the
  focus frame does not hang on a button nobody uses, and focus that game code sets meanwhile is also remembered rather
  than shown.
- The exception is elements that work with the keyboard themselves: a text field (`TextBox`, `PasswordBox` — the focus
  after a click is needed for typing), a `ListBox` with the focus on its rows (including the `ComboBox` drop-down), an
  open `ComboBox` and a `Menu`. The focus is not taken off them.
- A navigation press when there is no focus or it is invalid only brings the focus back and does nothing else: Gum does
  not navigate without a focused element, so the focus does not jump to a neighbour on the very first press. Where to,
  in order:
  1. to the remembered element;
  2. to the initial element of the screen ([«Initial focus of a screen»](#initial-focus-of-a-screen)) if there are no
     modal windows;
  3. to the first focusable element of the top modal window, and without one, of the screen or `Root` (`HandleTab` from
     Gum).
- A valid element is in the tree (under `Root`, `ModalRoot` or `PopupRoot`), visible together with its ancestors,
  enabled and, if a modal window is open, inside the top one. The tree check is needed because Gum leaves the focus on
  a removed element.

### Initial focus of a screen

```csharp
molib.Input.FocusScreen(mainMenu, initialFocus: playButton);
```

- Remembers the screen and its initial element and forgets the remembered focus of the previous screen. If the last
  device is navigation and there are no modal windows, it puts the focus on the initial element at once, and without
  one, on the first focusable element of the screen; otherwise the focus appears with the first navigation press.
- In the first edition of the ADR the name was `ShowScreen`; the method shows nothing and adds nothing to the tree, so
  it is named after what it does.
- For layers the initial focus is set in `LayerOptions.InitialFocus` ([layers.md](layers.md)).

### Buttons

| Action | Gamepad | Keyboard | Who does it |
| --- | --- | --- | --- |
| Navigation | D-pad, left stick | arrows, Tab / Shift+Tab | Gum; molib turns on the arrows |
| Press | A | Enter, Space | Gum |
| Cancel, back | B, Android back | Esc | molib, [layers.md](layers.md) |
| Scroll | right stick | PageUp / PageDown in lists | Gum; molib lists — [widgets/virtual-list.md](../widgets/virtual-list.md) |
| Previous / next tab or page | LB / RB | Ctrl+PageUp / Ctrl+PageDown | molib |
| Pick up / put down in drag and drop | X | X | molib, [drag-drop.md](drag-drop.md) |

### Keyboard arrows

- Gum 2026.11.2.1 has the lists `FrameworkElement.UpKeyCombos`, `DownKeyCombos`, `LeftKeyCombos` and
  `RightKeyCombos`: a combo from them moves the focus like the D-pad — in order or spatially, as the container is set
  up, with explicit direction overrides, while a `Slider` keeps left and right for its value. The lists are empty by
  default.
- Verified by running: `TextBox`, `ListBox` with the focus on its rows, an open `ComboBox` and `Menu` handle the arrows
  themselves first and then still call the general navigation. An arrow in a text field moves the caret and moves the
  focus to a neighbour; in a list it moves the selection and moves the focus out of the list.
- So molib keeps the arrows in these lists (with Gum's keyboard auto-repeat) while the focused element does not use the
  arrows, and clears them while one of the elements above or a molib virtual list
  ([widgets/virtual-list.md](../widgets/virtual-list.md)) is focused. The decision is made at the end of
  `MolibService.Update` from the focused element and takes effect from Gum's next frame. Focus that game code changes
  after `MolibService.Update` is taken into account a frame later. The game does not change these lists: molib owns
  them.

### Paging: LB / RB

- Gum has no `TabControl`, so molib does not look for tabs itself but lets the game mark a paging area:

```csharp
molib.Input.SetPaging(tabs.Visual, step => SelectTab(currentTab + step));  // null removes the mark
```

- LB / RB and Ctrl+PageUp / Ctrl+PageDown page the marked area nearest to the focus — the first one among the
  ancestors of the focused element — with a step of `-1` or `+1`. A scroll with `Pages` snapping marks itself the same
  way ([scrolling.md](scrolling.md)).
- If no marked area is among the ancestors of the focus, molib raises the `PageUnhandled` event with the step, and the
  game handles the press.
- A press that brought the focus back does not page — like any first navigation press.
- The marks live in a weak table: an area the game throws away without `SetPaging(area, null)` is not kept alive.
- Ctrl+Tab is not used: a Gum combo with Tab fires with any modifiers held (verified by running), and Ctrl+Tab would
  first move the focus to the next element.
- A gamepad has no long press ([gestures.md](gestures.md)): the context action is a separate button the game assigns.

## Requirement for molib modules

Every module with input describes work with a gamepad and the keyboard in its ADR. Currently these are: layers
([layers.md](layers.md)), scrolling and snapping ([scrolling.md](scrolling.md)), drag and drop
([drag-drop.md](drag-drop.md)), the virtual list ([widgets/virtual-list.md](../widgets/virtual-list.md)), toasts (take
no focus, [widgets/toasts.md](../widgets/toasts.md)).
