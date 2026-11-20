# Motion core

## Decision

Every molib animation — state transitions, show and hide, toasts, drag and drop — is driven by one core: a progress
value from 0 to 1, led either by a spring or by a tween along a curve. The core does not know what it animates: it
advances the progress by `deltaSeconds` and calls whoever applies the value. The tween curves are the same as in Gum
(`InterpolationType` and `Easing`), so an animation from code and one from the Gum editor look the same.

## API

```csharp
molib.Motion.TimeScale = 0.25f;                 // slow every molib animation down
int running = molib.Motion.ActiveCount;         // how many animations are running now
var soft = MotionPreset.Spring(stiffness: 300, dampingRatio: 0.8f);
var quick = MotionPreset.Tween(0.2f, InterpolationType.Quadratic, Easing.Out);
```

- The module is the `molib.Motion` property of type `MotionCore`. The type is not called `Motion`: that is the name of
  the `Molib.Motion` namespace, and inside `Molib` the name would resolve to the namespace.
- The presets, `TimeScale` and `ActiveCount` are public (together with scroll inertia and snapping, section
  «Scrolling»). The animations themselves are internal molib objects: the game gets them through state transitions
  ([state-transitions.md](state-transitions.md)) and `ShowAsync` / `HideAsync` ([show-hide.md](show-hide.md)).
- `MotionPreset` is immutable: a preset is created once and reused. A wrong parameter (`Stiffness` and `DampingRatio`
  not above 0, `Duration` not above 0, not a finite number, or a Gum `InterpolationType` or `Easing` outside its named
  values) is a programmer error, `ArgumentOutOfRangeException`.

## Progress

- An animation is an object with a progress `p` and a velocity `v`; the target is `p = 1`.
- The core keeps a list of active animations; a finished one is removed from the list, and its owner is notified.
- Notifications come after the pass over the list: in a notification the owner may start a new animation, and it runs
  from the next frame. The `ShowAsync` / `HideAsync` tasks complete there too — on the game thread inside
  `MolibService.Update`.
- An empty list — `Update` does nothing.
- When the service is disposed, the core finishes every running animation at once — the progress jumps to 1 and the
  owners complete as usual — and from then on a started animation completes in the call
  ([foundation/architecture.md](../foundation/architecture.md), section «Disposal»).
- Animations are reusable objects of their owner: nothing is created in a frame.

## Without animation

The core can forbid animations for a while — an internal molib scope, not a game API: owners in it apply values at
once. This is how `Scaling` applies the orientation to elements that were not on screen with the old orientation
([state-transitions.md](state-transitions.md), section «How a transition runs»).

## Spring

A damped oscillator solved analytically for any `deltaSeconds`, so the result does not depend on the frame rate:

```text
x = 1 - p         (distance from the target)
x'' = -k·x - c·x'
```

- The parameters are `Stiffness` (k) and `DampingRatio` (ζ = c / 2√k); the mass is 1.
- `ζ = 1` is critical damping: fastest without overshoot. `ζ < 1` overshoots. `ζ > 1` is slower, without overshoot.
  Each of the three cases has its own exact formula.
- It ends when `|x| < 0.001` and `|v| < 0.01`; then `p = 1` exactly.

## Tween

- `Duration` in seconds and a curve `InterpolationType` + `Easing` from Gum.
- `p = ease(min(t / Duration, 1))`; at `t ≥ Duration`, `p = 1` exactly.
- molib takes the curve function from Gum (`Tweener.GetInterpolationFunction`) once, when the preset is created.
  Verified on Gum 2026.11.2.1: every call returns a new delegate, so it must not be called in a frame.
- The `Back` and `Elastic` curves leave `[0, 1]` (`Back` + `Out` reaches 1.1 at 0.6), like spring overshoot.

## Presets

| Name | Kind | Parameters | Noticeable motion | Until full stop | For |
| --- | --- | --- | --- | --- | --- |
| `Snappy` | spring | k = 600, ζ = 1 | ≈ 0.15 s | ≈ 0.4 s | Hover, press, focus |
| `Smooth` | spring | k = 250, ζ = 1 | ≈ 0.25 s | ≈ 0.6 s | Showing and hiding layers |
| `Bouncy` | spring | k = 400, ζ = 0.6 | ≈ 0.12 s | ≈ 0.55 s | Accents: a toast, a badge; ≈ 10 % overshoot |
| `Instant` | — | — | — | — | No animation: the value is applied at once |

«Noticeable motion» is the way to 90 % of the target, «until full stop» — to the end threshold (computed with the core
formulas; at 60 frames per second that is 25, 37 and 34 frames). An animation occupies the core and the `await` waiting
for it until the full stop. The numbers are starting values; they are tuned in practice and corrected here.

## Overshoot

A spring with `ζ < 1` and the `Back` / `Elastic` curves give `p > 1`. Verified on Gum 2026.11.2.1: `float` properties
continue past the target correctly, while integer color components overflow outside `[0, 1]` (0 → 200 at `t = 1.3`
gives 4). So the progress for color and transparency is always clamped to `[0, 1]`, and only position, size and
rotation overshoot ([state-transitions.md](state-transitions.md)), plus the slide of show and hide
([show-hide.md](show-hide.md)).

## Retargeting

If an animation gets a new target before it ends, the owner takes the current values as the new start and restarts the
progress from `p = 0`. The progress velocity is not carried over: for UI transitions of 0.1–0.3 s this is not
noticeable. If it becomes noticeable — move to springs per property.

## Scrolling

Scroll inertia and snapping ([interaction/scrolling.md](../interaction/scrolling.md)) use the same spring
(`MotionPreset.Smooth`, the same exact formula), but directly on the scroll bar value rather than on a progress, and
with the initial velocity of the throw: velocity continuity matters there. The scrolling module drives these motions,
not the core list; it multiplies time by `TimeScale`, and each moving area counts as one animation in `ActiveCount`.

## Time

- Only `deltaSeconds` from `GameTime`, capped at 0.1 s ([foundation/architecture.md](../foundation/architecture.md),
  section «Update order»).
- `molib.Motion.TimeScale` (1 by default) multiplies `deltaSeconds` after the cap — slowing every molib animation down
  for debugging. 0 pauses. A negative value or not a number is a programmer error (`ArgumentOutOfRangeException`): it
  is a setting from code, not data.
