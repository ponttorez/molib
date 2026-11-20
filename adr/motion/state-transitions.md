# Transitions between states

## Decision

Gum applies states instantly: Forms controls set `"<Category>State"` on the visual (`Enabled`, `Highlighted`, `Pushed`,
`Focused`, `Disabled`…), and the values switch in one frame. molib intercepts applying a state in the visual and
compares the values before and after: everything that changed from the animatable set (position, size, rotation, color,
transparency) molib puts back to the old values and leads to the new ones with a spring or a tween. The rest switches at
once. A «before and after» diff was chosen because Gum states come in two kinds, and `InterpolateBetween` works with
only one of them. It works for any category, not only Forms: orientation states and the game's own categories animate
the same way.

## Verified Gum behavior

Verified with a program on Gum 2026.11.2.1:

- **The intercept works on both paths.** Both Forms controls (`IsEnabled`, `UpdateState()`) and
  `SetProperty("<Category>State", name)` call the virtual `GraphicalUiElement.ApplyState(StateSave)`.
- **States come in two kinds.**
  - From the Gum editor (`.gumj`) and created with variables — `StateSave.Variables` is filled.
  - The standard V3 visuals from code (`ButtonVisual` and others) — `Variables` is empty, and the `StateSave.Apply`
    delegate sets the values (on `Disabled` a button changes the background and text colors of its children).
- **`InterpolateBetween` does not fit as the main mechanism:** for V3 visuals it has nothing to interpolate, and outside
  `[0, 1]` integer color components overflow (`Red`: 0 → 200 at `t = 1.3` gives 4, at `t = -0.2` gives 217). `float`
  values continue correctly (`X` at `t = 1.3` is 130).
- **A Gum animation applies a new state every frame** (`AnimationRuntime.ApplyAtTimeTo` builds a fresh `StateSave` and
  calls `ApplyState`; from the sources). No category of the visual has such a state.
- **Forms controls apply a state on events** (hover, press, focus, `IsEnabled`, a color change on a V3 visual), not
  every frame; often the same state again (from the `ButtonBase` sources).

## The intercept point

A visual connects transitions like this:

```csharp
private readonly StateTransitions? _transitions;

public MyVisual(MolibService molib)
{
    _transitions = molib.Transitions.Create(this);
}

public override void ApplyState(StateSave state)
{
    if (_transitions is null)
    {
        base.ApplyState(state);
        return;
    }

    _transitions.Apply(state, base.ApplyState);
}
```

- `StateTransitions` is bound to the visual at creation, so the visual is not passed to `Apply`.
- The `null` check is needed: the base constructor may apply a state before the subclass constructor runs. The visual is
  not on screen yet at that time, and such an application would be instant anyway.
- The method group `base.ApplyState` creates a delegate on every call. States change on events, not every frame, so
  this is acceptable for one's own visual; molib's visuals keep the delegate in a field.
- For visuals from the Gum editor (generated `partial` classes) this code goes into one's own `partial` part.

### molib visuals

For the standard V3 visuals molib ships subclasses with this intercept in the `Molib.Visuals` namespace:
`ButtonVisual`, `CheckBoxVisual`, `ListBoxItemVisual`. The set grows as games need visuals.

- The constructor repeats Gum's and adds the service: `new ButtonVisual(molib, fullInstantiation: true,
  tryCreateFormsObject: true)`.
- The base constructor is called with `tryCreateFormsObject: false`, and the subclass creates the Forms control itself —
  after the transitions are ready. Gum's own `ThumbVisual` is built the same way. If the base constructor created the
  control, its first `UpdateState` would bypass the transitions.
- The `Transitions` property — the visual's transitions for configuration (`visual.Transitions.For(...)`).
- To give every control of the game molib visuals, the game registers them in Gum, like its own visuals:

```csharp
FrameworkElement.DefaultFormsTemplates[typeof(Button)] =
    new VisualTemplate((_, createForms) => new Molib.Visuals.ButtonVisual(molib, tryCreateFormsObject: createForms));
```

## How a transition runs

On `Apply(target, applyBase)`:

1. **Category.** molib looks for `target` among the states of the visual's categories (`Categories`, by reference). Not
   found — it is a frame of a Gum animation or a state without a category.
2. **Without animation** — if there is no category, the preset is `Instant`, or nobody saw the old values:
   - the visual is not on screen: it or an ancestor is invisible, or its top ancestor is not Gum's `Root`, `ModalRoot`
     or `PopupRoot` (the element is still being built or already removed);
   - molib applies the orientation to an element that was not on screen with the previous orientation
     (`Scaling.Enable`, `Scaling.ApplyOrientation`, [platform/scaling.md](../platform/scaling.md)).

   Then the current transition is finished (the tracks get their ending values and the animation stops), and then
   `applyBase(target)`. Without finishing, the running transition would keep writing its values over the new state.
3. **The «before» snapshot.** The animatable properties of the visual and all its descendants are recorded into a
   reused buffer. During a transition these are the current intermediate values.
4. The running transition is finished to its ending values.
5. `applyBase(target)` — Gum applies the state as usual (variables or the `Apply` delegate).
6. **The «after» snapshot** of the same elements.
7. Properties that changed are put back to their «before» values and become animation tracks «before → after».
   Unchanged ones are not touched.
8. Every frame the tracks get a value by the progress `p` from [motion-core.md](motion-core.md): `float` properties by
   `p` (with overshoot if the spring has it), color and transparency by `clamp(p, 0, 1)`, without leaving the range.
9. At `p = 1` every track gets its exact «after» value, and the tracks are cleared.

- Steps 3–7 run with the visual's layout suspended (`SuspendLayout(true)` and `ResumeLayout(true)` if layout was not
  suspended before): Gum lays the elements out once instead of per property. Frames with position and size tracks are
  also written with layout suspended.
- If during `applyBase` the same visual applies a state again (a state variable sets a state of another category), the
  nested application bypasses the transitions: its result lands in the «after» snapshot of the outer one.
- If the transitions are no longer updated (`MolibService` disposed), every application is instant.

### Why «not on screen» and not «the first application»

The first application of a category is not treated as instant. In components from the Gum editor the Forms control is
created in the generated constructor before the game's code sets up the transitions, so the first application through
the transitions is already the first hover, and it would not animate. The right condition is «nobody saw the old
values», and that is exactly «the visual is not on screen». A consequence: a screen built right in the tree animates
the states set while building. A screen is built and then added to the tree — that is how it is done in Gum.

The animatable set:

| Property | Where |
| --- | --- |
| `X`, `Y`, `Width`, `Height`, `Rotation` | any `GraphicalUiElement` |
| `Color` (with transparency) | `SpriteRuntime`, `NineSliceRuntime`, `TextRuntime`, `PolygonRuntime` |
| `FillColor`, `StrokeColor` | `RectangleRuntime`, `CircleRuntime` (their `Color`, `Red`, `Alpha` are obsolete and write only the stroke) |
| `Alpha` | an element with an `InvisibleRenderable` (a container, a Forms visual); it hides children only with a render target ([show-hide.md](show-hide.md), section «Transparency») |

- Frames are written through the typed properties of these types, without `SetProperty` and value boxing.
- If a state changes the units (`XUnits`, `WidthUnits`…) or the origin (`XOrigin`, `YOrigin`) together with the value,
  that property switches at once: numbers in different units are not interpolated.
- A size does not cross zero on overshoot if both ends have the same sign: a width of 100 → 0 with the `Bouncy` spring
  does not go negative.
- Everything else the state changed (textures, text, visibility, children layout) is already applied in step 5 and
  stays as is — that is, it switches at once.
- Snapshots are taken only on a state change, not every frame. The snapshot and track buffers live on the
  `StateTransitions` object and are reused.

## A state change during a transition

A new state before the current transition ends: the «before» snapshot takes the current intermediate values, the old
transition is finished to its ending values, and the tracks are rebuilt ([motion-core.md](motion-core.md), section
«Retargeting»). Properties the new state does not touch continue towards their previous target instead of freezing
halfway. Quick hovering back and forth does not jerk.

One visual runs one transition: a state change in any of its categories rebuilds the tracks by this rule.

## A look change outside states

A visual may change its look in code rather than through a state: for example, the theme changed its palette and the
visual repaints its layers. A running transition would overwrite the new values: its tracks lead to targets taken from
the old look, and the old color remains after the transition ends. Before such a change the visual calls
`transitions.Finish()`: the transition is finished to its ending values at once and stops, and then the code sets the
new values. The look change is instant; the next state change animates as usual.

Verified by running: a palette switched while a button under the cursor goes into hover and a radio button is being
unchecked lands on every visual.

## Configuration

```csharp
molib.Transitions.Default = MotionPreset.Snappy;
var transitions = molib.Transitions.Create(visual)
    .For("ButtonCategory", MotionPreset.Snappy)
    .For("ButtonCategory", to: "Pushed", MotionPreset.Instant)
    .For("Orientation", MotionPreset.Smooth);
```

- The preset is chosen by the most exact match: category + target state → category → `Default`. `Default` is read on
  every application, so changing it affects every visual.
- `MotionPreset.Instant` turns the transition off.
- A repeated `For` with the same category and state replaces the preset.
- A category the visual does not have at the time of `For` gives the diagnostic `transitions.unknownCategory`:
  categories are added before the transitions are configured.

## What does not animate

- Children-based layout (`RelativeToChildren`), unit changes, textures, text — discretely.
- If a transition changes sizes, Gum recomputes the layout every frame of the transition; on complex screens this shows
  in the frame time. For such transitions — the `Instant` preset, or change color and transparency instead of size.
- The snapshot walks the whole subtree of the visual. For a visual with large content (a list of hundreds of rows) a
  state change costs a walk over every element — for such visuals transitions are configured only for the needed
  categories, the rest are `Instant`.
- Two sources on one property (the visual's transition and its descendant's, or a transition and `ShowAsync`) write in
  turn, and the last one wins. molib does not sort this out.
