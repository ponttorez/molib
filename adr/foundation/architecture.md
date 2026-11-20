# Architecture

## Decision

molib is a set of independent modules on top of Gum, gathered into one object, `MolibService`, which the game creates
itself and updates every frame after Gum. The modules do not know about each other, except for the shared motion core
and diagnostics. molib draws nothing of its own and does not own the element tree: everything on screen is a Gum
element. A frame in which nothing animates or scrolls does no work.

## Entry point

```csharp
GumUI.Initialize(this);
_molib = new MolibService(this, GumService.Default);

protected override void Update(GameTime gameTime)
{
    GumUI.Update(gameTime);
    _molib.Update(gameTime);
    base.Update(gameTime);
}
```

- `MolibService` is created after `GumUI.Initialize` (otherwise `InvalidOperationException`) and lives as long as the
  game.
- The game turns on the keyboard and gamepads for the interface with Gum's `UseKeyboardDefaults()` and
  `UseGamepadDefaults()`; molib reads input from the same Gum lists (`KeyboardsForUiControl`, `GamePadsForUiControl`)
  and keeps none of its own ([interaction/focus-and-gamepad.md](../interaction/focus-and-gamepad.md)).
- Creating a second instance while one is alive is a programmer error (`InvalidOperationException`): Gum itself is
  global, and two sets of layers over one `ModalRoot` would clash. molib keeps no static state of its own
  ([code-style.md](code-style.md), section «State»): the service registers itself in `Game.Services` and learns from
  that registration that it already exists. As a bonus, any code that has the `Game` finds molib through
  `Services.GetService<MolibService>()`.
- `Update` is called after `GumUI.Update`: Gum has already handled input and applied the Forms states this frame, molib
  sees the result and animates it.
- Drawing is only `GumUI.Draw()`: molib has no `Draw` of its own.
- `Dispose` ends molib completely and removes the service from `Game.Services`; a new service may be created
  afterwards (section «Disposal»).
- The game sets up the back buffer with `RenderTargetUsage.PreserveContents`: without it, fading layers erase what was
  drawn before Gum ([motion/show-hide.md](../motion/show-hide.md), section «Transparency»). molib checks this in the
  first `Update`, not in the constructor: the game can subscribe to `Diagnostics.Warning` only after the service
  exists.

## Modules

| `MolibService` property | Module | ADR |
| --- | --- | --- |
| `Scaling` | Scaling and orientation | [platform/scaling.md](../platform/scaling.md) |
| `SafeArea` | Screen safe area | [platform/safe-area.md](../platform/safe-area.md) |
| `Motion` | Motion core: springs, tweens, the list of active animations (type `MotionCore`: the name `Motion` is taken by the namespace) | [motion/motion-core.md](../motion/motion-core.md) |
| `Transitions` | Smooth transitions between states | [motion/state-transitions.md](../motion/state-transitions.md) |
| `Input` | Gamepads, last device, focus visibility and initial focus, navigation buttons | [interaction/focus-and-gamepad.md](../interaction/focus-and-gamepad.md) |
| `Layers` | Modal windows and popup layers, cancel | [interaction/layers.md](../interaction/layers.md) |
| `DragDrop` | Drag and drop between containers | [interaction/drag-drop.md](../interaction/drag-drop.md) |
| `Toasts` | Toasts | [widgets/toasts.md](../widgets/toasts.md) |
| `Sound` | UI sound events | [integration/sound.md](../integration/sound.md) |
| `Diagnostics` | molib warnings | this ADR, section «Diagnostics» |

The modules do not know about each other, except for the motion core and diagnostics. The exceptions are modules built
from ready parts:

- `Scaling` uses the core to apply the orientation without animation ([platform/scaling.md](../platform/scaling.md),
  section «Orientation»).
- `Layers` builds a layer from what already exists: focus and the last device from `Input`, the safe area container
  from `SafeArea`, the orientation from `Scaling`, show and hide from the motion core; it hands the cancel press to
  `DragDrop` first ([interaction/layers.md](../interaction/layers.md), section «Cancel»).
- Scroll inertia takes the last device, the focus and the paging area mark from `Input`
  ([interaction/scrolling.md](../interaction/scrolling.md)).
- `DragDrop` takes the focus and the top modal window from `Input`, the flight of the copy from the core, and ends the
  press record of a scroll without a throw ([interaction/drag-drop.md](../interaction/drag-drop.md)).
- `Toasts` takes the safe area container from `SafeArea`, the orientation from `Scaling`, show, hide and the stack
  shift from the motion core ([widgets/toasts.md](../widgets/toasts.md)).
- Layers, toasts, drag and drop and snapping scrolls hand their sound events to `Sound`, and layers and toasts connect
  their content to it ([integration/sound.md](../integration/sound.md)). `Sound` takes the last device and the
  per-frame navigation check from `Input`.

Show and hide ([motion/show-hide.md](../motion/show-hide.md)), scroll inertia
([interaction/scrolling.md](../interaction/scrolling.md)) and long press
([interaction/gestures.md](../interaction/gestures.md)) attach to specific elements through extension methods and take
`MolibService` as a parameter. The modules that drive them are internal and have no `MolibService` property. The
virtual list ([widgets/virtual-list.md](../widgets/virtual-list.md)) is a control, a subclass of Gum's `ScrollViewer`:
the game creates it itself, without `MolibService`, and `Input` recognizes it in focus so it does not hand the arrows
to the general navigation.

## Update order

`MolibService.Update(gameTime)`:

1. `Scaling` — compare the back buffer size with the last frame; on a change or a new `UserScale`, recompute the scale
   and orientation.
2. `SafeArea` — if the source reported new insets, or the back buffer size or the Gum camera zoom changed, reread the
   insets and apply them.
3. `Input` — gamepad connection and disconnection, the last device, hiding and restoring focus, LB / RB.
4. `Layers` — handle cancel (Esc, gamepad B, Android back); then `Sound` — a cursor press on a disabled control and the
   clock of slider steps.
5. Long presses, then drag and drop, then scroll inertia and snapping — only the held, running and moving ones. After
   the layers: cancel ends a drag first. Scrolling comes last: it sees the value changes made this frame by Gum focus,
   paging and edge scrolling during a drag.
6. `Motion` — advance every active animation by `deltaSeconds`: state transitions, show and hide, the flight of the
   drag copy, toasts.
7. `Toasts` — show timers, the toast under the cursor, removal of the disappeared ones and showing the next ones from
   the queue. After the motion core: it has already finished show and hide this frame.
8. `Input` — remember the focused element and its mode and turn arrow navigation on or off for Gum's next frame. This
   is the last step: continuations of tasks the motion core completed may also move the focus
   ([interaction/layers.md](../interaction/layers.md), section «Cancel»;
   [interaction/focus-and-gamepad.md](../interaction/focus-and-gamepad.md), section «Keyboard arrows»).

`deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds`, capped at 0.1 s: after a pause or a hitch, animations do
not jump.

## Zero work in an idle frame

- Every module keeps a list of only the active objects (running animations, moving scrolls, held presses). An empty
  list returns without work.
- The window size check is a comparison of two numbers; the safe area adds a flag from the source and the camera zoom.
- `Input`, `Layers`, `DragDrop` and `Sound` read Gum's ready input state every frame: gamepad buttons from
  `GamePadsForUiControl`, a few keys, the cursor and the focused element — without allocations. Heavy work (finding
  the focus, closing a layer, the list of drop targets) happens only on a press.
- Scroll inertia learns about presses and value changes from the area's events, and about focus from one reference
  comparison per frame.
- Subscriptions to Gum and window events are made when attaching, not in the frame.

## Diagnostics

- `MolibService.Diagnostics.Warning` — an `Action<string code, string message>` event. By default it writes nowhere: the
  game subscribes and prints where it likes.
- A repeat of the same code with the same key stays silent (`WarnOnce(code, key, message)`).
- Codes are strings in the `area.problem` form: `scaling.badUserScale`, `safeArea.badInsets`, `layers.notInTree`,
  `transitions.unknownCategory`, `showHide.slideInStack`.
- Programmer errors are exceptions, not diagnostics ([code-style.md](code-style.md), section «Errors»).

## Disposal

`MolibService.Dispose()` leaves Gum as if molib had never run, except for what the game sees on screen: running work
is finished rather than frozen halfway, temporary elements leave the tree, changed Gum settings come back, and molib's
handlers leave the game's elements. Order:

1. `Sound` stops raising events and takes its handlers off every attached control.
2. The motion core finishes every running animation at once: state transitions reach their «after» values, a show
   or hide reaches its end (a hide hides or removes the element), the render target and input flags of a fading
   element come back, and the `ShowAsync` / `HideAsync` tasks complete with `true`. Every later state change applies
   at once.
3. `DragDrop` ends a drag: the source gets its own transparency and render target back, the target gets the `None`
   state, the copy leaves `ModalRoot`; the registrations, which hold the game's delegates, are cleared.
4. `Layers` closes every open layer, top first, with `LayerCloseReason.Code`: the layer leaves the tree, its content
   is hidden and unparented, the focus returns as after any close, `Closed` completes.
5. `Toasts` takes every toast away, shown or waiting: the content leaves its slot visible and without a parent, the
   container leaves `PopupRoot`, every handle reports `IsOpen = false`.
6. Long presses and scroll inertia leave their elements' events; scroll motion stops where it is.
7. `Input` gives back Gum's direction key lists empty and all four Gum gamepads in `GamePadsForUiControl` if
   `UiGamepads` was set, and forgets the screen, the remembered focus and the paging marks.
8. `SafeArea` leaves the source's event and forgets the attached containers; the containers keep their insets.
9. `Scaling` stays as it is — the camera zoom and the canvas size of the current screen — so nothing jumps; a new
   service enables scaling again.

- Dispose raises no molib events: no sound events, no `Cancelled`, no `CancelUnhandled`. Tasks complete (layers,
  show and hide); that is how awaiting code learns that its work ended.
- After Dispose, methods that add or start something throw `ObjectDisposedException` (`Update`, `ShowModal`,
  `Toasts.Show`, `AddSource`, `Pick`, `Attach`, `FocusScreen`, `Enable`, `ShowAsync`, `UseInertia`…), while methods
  that remove, stop or undo something do nothing (`Remove`, `Detach`, `Cancel`, `Dismiss`, `CloseAsync`,
  `SetPaging(area, null)`, `Dispose` itself). Cleanup code may therefore run in any order.
- An event handler may dispose the service in the middle of `Update`; the update stops after that module.
- Verified by a program against the package: a drag, a layer, a toast, a running hide, a state transition, long
  presses, inertia and sound subscriptions, then `Dispose` in the middle; after it the source has its look back,
  `ModalRoot` and `PopupRoot` are empty, the tasks have completed, no animation runs, the handlers are gone, and a new
  service works.

### Detaching from elements

molib subscribes to events of the game's elements in three places: long press (`Push`, `LosePush`), scroll inertia
(`PushPreview`, `ScrollChanged`) and sound (`Click`, `Push`, `RollOn`, `GotFocus` and the like). The handlers hold the
molib objects and, through them, the service. A screen the game throws away together with its elements takes the
handlers along, but an element that outlives its screen would keep the old module alive and keep calling it. So each
place can be undone explicitly: `LongPress.Detach()`, `ScrollInertia.Detach()`, `Sound.Detach(element)`; `Dispose`
detaches everything. Registrations kept by molib itself are weak (drag and drop, paging marks, safe area containers)
and do not keep elements alive.

## Threads

molib, like Gum, runs on the game thread — the one that runs `Game.Update`.

- Every method, property and event of molib belongs to the game thread. Events are raised there, inside
  `MolibService.Update` or inside a Gum event.
- Tasks (`ShowAsync`, `HideAsync`, `LayerHandle.Closed`) complete there, inside `MolibService.Update`: code after
  `await` may change the interface right away.
- Two things may come from any thread, and molib only marks them and acts in the next `Update`: cancelling the token
  of `ShowAsync` or `HideAsync` ([motion/show-hide.md](../motion/show-hide.md)), and the `Changed` event of a safe area
  source ([platform/safe-area.md](../platform/safe-area.md)).
- Background code (loading, networking) hands its result to the game thread before touching molib or Gum.

## Dependence on Gum's global state

Gum keeps its state statically (`GumService.Default`, `FrameworkElement.ModalRoot`, `PopupRoot`). molib takes the
`GumService` in its constructor and reaches the roots through it, and keeps its own state only in `MolibService` and
in the objects it creates.
