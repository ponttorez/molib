# Scaling and orientation

## Decision

Scaling as in Godot with `stretch mode = canvas_items`, `aspect = expand`, `scale mode = fractional`: the game sets a
base resolution, the zoom is the smaller of the two window-to-base ratios, and the logical area extends to the window
along the long side. Nothing is cut off and there are no bars. Screen rotation and window resizing are handled the same
way — by recomputing at the start of the frame. Gum's ready modes (`EnableZoomToWindow`, `EnableExpandToWindow`) are
not used: the first computes the zoom along one axis and cuts off the width in portrait, the second does not scale at
all.

## Formula

```text
zoom   = min(windowWidth / baseWidth, windowHeight / baseHeight) * userScale
canvas = (windowWidth / zoom, windowHeight / zoom)
```

- `windowWidth`, `windowHeight` — the back buffer size in pixels
  (`GraphicsDevice.PresentationParameters.BackBufferWidth/Height`), not `Preferred*`: on screens with OS scaling they
  differ.
- `baseWidth`, `baseHeight` — the base of the current orientation: in portrait, the portrait base if the game set one
  (section «Orientation»).
- Result: `GumService.Renderer.Camera.Zoom = zoom`, `GumService.CanvasWidth/CanvasHeight = canvas`.
- Right there molib stretches `Root`, `ModalRoot` and `PopupRoot` over the new canvas, as Gum does at the start of its
  `Update`. molib updates after Gum, and without this one frame would be drawn with the new zoom and the old layout:
  dragging the window edge would make the layout flicker.
- `userScale` is a multiplier for game settings («interface size»), 1 by default, allowed 0.5…2
  (`Scaling.MinUserScale`, `Scaling.MaxUserScale`). Outside the range — the diagnostic `scaling.badUserScale` and
  clamping; not a number — the diagnostic and 1. This is a data error, not a programmer error: the value usually comes
  from a settings file.
- A zero-sized back buffer (a minimized window on some platforms) is skipped: recomputing happens when the size becomes
  non-zero again.

Example for a 1920×1080 base:

| Window | zoom | Logical area |
| --- | --- | --- |
| 1920×1080 | 1 | 1920×1080 |
| 2560×1080 (21:9) | 1 | 2560×1080 |
| 1024×768 (4:3) | 0.533 | 1920×1440 |
| 1080×2400 (phone, portrait) | 0.5625 | 1920×4267 |

## API

```csharp
molib.Scaling.Enable(baseWidth: 1920, baseHeight: 1080);
molib.Scaling.UserScale = 1.25f;
molib.Scaling.Changed += OnScalingChanged;
```

- `Enable` is called once after `MolibService` is created: a second call throws `InvalidOperationException`, a base side
  ≤ 0 throws `ArgumentOutOfRangeException`. Before `Enable` the module does nothing, `IsEnabled = false`.
- `Enable` applies the zoom and the orientation state at once: the first frame is already laid out. So the game builds
  its interface before `Enable`, and the whole tree gets the orientation states.
- `UserScale` takes effect in the next `MolibService.Update`: recomputing always happens at one place in the frame.
- `Zoom`, `CanvasWidth`, `CanvasHeight`, `IsPortrait` — the current values, read-only.
- `Changed` — after recomputing, when the orientation states are already applied; the argument
  (`ScalingChangedEventArgs`) holds the new values.
- `ScreenToCanvas(Vector2)` / `CanvasToScreen(Vector2)` — for world anchors and the game's own math; they go through the
  Gum camera (`Camera.ScreenToWorld` / `WorldToScreen`) and take its zoom and position into account.

## When to recompute

- Every `Update` compares the back buffer size with the last one. If it changed, recomputing happens in the same frame,
  before the other modules.
- `Window.ClientSizeChanged` and `Window.OrientationChanged` are not used as the source: on different platforms they
  arrive at different times and not always. Comparing two numbers per frame is more reliable and cheaper.

## Orientation

- `IsPortrait = windowHeight > windowWidth`.
- On an orientation change and in `Enable`, molib applies the state `Portrait` or `Landscape` of the category
  `Orientation` to the trees of `Root`, `ModalRoot` and `PopupRoot` (`ApplyStateRecursive`). Elements without that
  category are not touched. So a screen from the Gum editor rearranges itself for the orientation without code. The
  category and state names are the constants `Scaling.OrientationCategory`, `PortraitState`, `LandscapeState`.
- An element built in code gets the same category through `StateSave.Apply`: the state sets properties directly,
  without string variable names.
- An element added later gets the current orientation state when molib shows it (layers, toasts). Elements the game
  adds to `Root` itself get it by calling `molib.Scaling.ApplyOrientation(element)`.
- State transitions ([motion/state-transitions.md](../motion/state-transitions.md)) animate the orientation change if
  the visual has them set for the `Orientation` category. `Enable` and `ApplyOrientation` apply the state without
  animation: those elements were not on screen with the old orientation, so there is nothing to animate.
- The game sets the allowed orientations (`GraphicsDeviceManager.SupportedOrientations`): it is a window setting, not UI.
- A separate base resolution for portrait (optional) is the second overload `Enable(baseWidth, baseHeight,
  portraitBaseWidth, portraitBaseHeight)`, for example 1920×1080 and 1080×1920. Without it there is one base, as in
  Godot, and the interface is smaller in portrait: the zoom is computed from the short side of the landscape base.

## Crisp text

With `zoom ≠ 1` text scales together with everything else and blurs. A game with KernSmith only needs
`TextRuntime.UseFontOversampling = true`: Gum rasterizes the font for the final size. molib does not turn it on
itself — it is a global Gum flag and the game's choice (pixel art keeps it off).

## Input

Gum's cursor already takes `Camera.Zoom` into account in hit tests, so molib recomputes nothing for clicks. Movement
thresholds in gestures and scrolling are in logical units (canvas units).
