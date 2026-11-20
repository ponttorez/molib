# Show and hide

## Decision

In Gum `Visible = false` and removal from the parent happen instantly, so a hide animation must play before hiding, and
the hiding or removal must happen after it ends. molib gives `ShowAsync` and `HideAsync` on any element for this: the
motion core ([motion-core.md](motion-core.md)) drives transparency and the slide, and a repeated call turns the
animation around from the current values. molib does not wrap animations from the Gum editor: Gum plays and awaits them
itself (`PlayAnimationAsync`), and they cannot turn around from the current values.

## API

```csharp
await element.ShowAsync(molib, ShowHide.FadeSlideUp);
bool finished = await element.HideAsync(molib, ShowHide.FadeSlideUp, then: HideAction.Remove);
await toast.ShowAsync(molib, ShowHide.FadeSlideUp.WithMotion(MotionPreset.Bouncy));
```

- Extension methods on `GraphicalUiElement` in `Molib.Motion`. For a Forms control — `control.Visual.ShowAsync(...)`.
- `ShowAsync`: makes the element visible, sets the starting values of the preset and animates to the normal ones.
- `HideAsync`: animates to the ending values of the preset, then `HideAction.Hide` (`Visible = false`, the default) or
  `HideAction.Remove` (`RemoveFromRoot`, that is `Parent = null`). After that it restores the original property values,
  so the next `ShowAsync` starts from them.
- The result is `true` if the animation reached the end and `false` if the next `ShowAsync` or `HideAsync` of the same
  element took it over: then there was no hiding or removal, and the code after `await` sees it.
- The task completes on the game thread, inside `MolibService.Update`: the continuation after `await` may change the
  interface right away.
- `CancellationToken`: cancellation finishes the animation at once (ending values, hiding or removal), and the task
  completes as cancelled. Cancellation may come from any thread: molib only marks the call and finishes it in the next
  `MolibService.Update`. A token cancelled before the call finishes the animation right in the call.
- After the service is disposed — `ObjectDisposedException`.

## Presets

| Preset | Show | Hide |
| --- | --- | --- |
| `Fade` | transparency 0 → 1 | 1 → 0 |
| `FadeSlideUp` | transparency + a slide of 24 units from below | reverse |
| `FadeSlideDown` | transparency + a slide of 24 units from above (toasts at the top edge) | reverse |
| `Instant` | at once | at once |

- The motion is `MotionPreset.Smooth` ([motion-core.md](motion-core.md)); `WithMotion(...)` returns the same preset with
  another motion. Transparency stays within `[0, 1]`; the slide may overshoot.
- There is no scale among the presets: an element in Gum has no render scale, and changing `Width`/`Height` relays out
  the content and rewraps text.

## Slide

- The slide is a change of the element's `Y` during the animation, in canvas units. It works only if `YUnits` is in
  pixels (`GetIsPixelBased()` from Gum); otherwise there is no slide and the diagnostic `showHide.unsupportedUnits`.
- Verified on Gum 2026.11.2.1: in a stack (`TopToBottomStack`, `LeftToRightStack`) the `Y` of an element also moves all
  the following siblings (`Y = 24` on the first one lowers the second by 24), and a stack skips a hidden element. So an
  element that appears with a slide is put not into a stack but on top: windows, toasts, panels in a plain container. A
  slide of an element in a stack gives the diagnostic `showHide.slideInStack`.

## Transparency

Verified on Gum 2026.11.2.1:

- The `Alpha` of a plain container **does not affect** its children: with `Alpha = 0` a child rectangle draws fully.
- With a render target, a container draws its children into a texture and outputs it with its own transparency: with
  `Alpha = 0` the children are invisible, with `Alpha = 128` exactly half visible. This works on any element with an
  `InvisibleRenderable`, not only `ContainerRuntime`: a `ButtonVisual` with `IsRenderTarget = true` and `Alpha = 128`
  has a background color exactly half as dark.
- Without `RenderTargetUsage.PreserveContents` on the back buffer, Gum warns that everything drawn before Gum in this
  frame will be erased when a render target container is drawn.

Decision:

- **An element with an `InvisibleRenderable`** (containers, Forms visuals, components from the editor): during the
  animation molib turns on `IsRenderTarget` of its `RenderableComponent` and changes its `Alpha`; afterwards it restores
  the previous values. According to the Gum sources, a render target not drawn in the last frame is deleted
  (`RenderTargetService.ClearUnusedRenderTargetsLastFrame`), so the texture lives only while the animation runs. The
  texture is the size of the element: children outside its bounds (a button's focus stripe or a panel's soft shadow,
  for example) are cut off during the animation and jump in when it ends (verified against the Gum sources:
  `CameraRenderTargetExtensions.GetRenderTargetBounds` takes the bounds of the element itself). So whatever appears and
  disappears as a whole keeps everything visible inside its bounds: a toast or a window with a shadow, for example, sits
  in a container with room for the shadow. Blending is right: Gum bakes the children with correct alpha and outputs the
  texture as premultiplied, so translucent pixels match in the last frame of the animation and after it.
- **An element with its own color**: its own transparency, without a render target — `Alpha` of `SpriteRuntime`,
  `NineSliceRuntime`, `TextRuntime`, `PolygonRuntime`, `FillAlpha` and `StrokeAlpha` of `RectangleRuntime` and
  `CircleRuntime`. If such an element has children, they do not fade: the diagnostic `showHide.childrenNotFaded`, and the
  element should be put into a container.
- **Otherwise** there is no transparency, the diagnostic `showHide.noAlpha`; the slide works.
- The game sets up the back buffer with `RenderTargetUsage.PreserveContents`
  (`GraphicsDeviceManager.PreparingDeviceSettings`). molib checks this in the first `MolibService.Update` and without it
  writes the diagnostic `motion.backBufferNotPreserved`: the game's background under a fading window would vanish
  ([foundation/architecture.md](../foundation/architecture.md), section «Entry point»).

### iOS and `RetainedBacking`

From the MonoGame 3.8.6.1 sources, not checked on a device:

- On iOS the EAGL layer is created with `RetainedBacking = false` (`Platform/iOS/iOSGameView.cs`, `CreateContext`), and
  `PresentationParameters.RenderTargetUsage` does not affect it. `RetainedBacking` only says whether the color buffer
  keeps its contents after a frame is presented — between frames.
- Within a frame Gum switches to a render target and back. MonoGame binds the texture's framebuffer and then the
  window's framebuffer again (`PlatformApplyDefaultRenderTarget` binds `glFramebuffer`), and does not touch the window's
  renderbuffer: `InvalidateFramebuffer` is called only in `Present`, and only for depth and stencil.
- Only the XNA emulation in `GraphicsDevice.ApplyRenderTargets` clears the back buffer on return — when the back buffer
  has `DiscardContents`. With `PreserveContents` there is no clearing.
- Conclusion: `RetainedBacking = false` does not get in the way of fading, only `PreserveContents` is needed; the game
  draws every frame fully anyway (`Clear` at the start of `Draw`). The cost on tile-based mobile GPUs is that every
  switch unloads and reloads the back buffer tiles, so the render target is on only while the animation runs.

## Input while hiding

While `HideAsync` runs, the element takes no mouse or touch input: molib sets `HasEvents = false` and
`ExposeChildrenEvents = false` and restores the previous values at the end. Verified on Gum 2026.11.2.1: `HasEvents =
false` on a container alone does not disable a button inside it (the click gets through), while `ExposeChildrenEvents =
false` does. The click passes to whatever lies under the element. `HideAsync` does not touch keyboard and gamepad focus:
the layer moves it away and back ([interaction/layers.md](../interaction/layers.md)).

## Repeated calls

- A call during an animation of the same element (`ShowAsync` during `HideAsync` and vice versa, or the same method
  again) turns it around from the current values with the new preset; the interrupted call completes at once with
  `false`.
- `ShowAsync` of a visible element without an animation does nothing and returns `true`. `HideAsync` of an invisible
  one does the same, except that with `HideAction.Remove` it removes the element from its parent.
- During an animation molib controls the element's `Y`, transparency and input flags; the game does not set its own
  value meanwhile, or molib will restore the original one at the end.

## Animations from the Gum editor

molib has no `ShowHide.GumAnimation` wrapper, because Gum already does this, and a wrapper would not give what
`ShowAsync` and `HideAsync` exist for:

- a Gum animation runs through key frames from the start and does not turn around from the current values;
- verified on Gum 2026.11.2.1: `PlayAnimationAsync` completes its task with `RunContinuationsAsynchronously`, and
  without `GumService.UseSingleThreadedAsync()` the continuation after `await` runs not on the game thread, where
  hiding an element is not allowed;
- interrupting with another Gum animation cancels the task (`TaskCanceledException`).

The same scenario with Gum:

```csharp
GumUI.UseSingleThreadedAsync();               // once after Initialize
element.Visible = true;
await element.PlayAnimationAsync(showAnimation);
await element.PlayAnimationAsync(hideAnimation);
element.Visible = false;
```
