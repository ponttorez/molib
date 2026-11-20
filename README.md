# molib

molib is a thin C# library on top of [Gum](https://github.com/vchelaru/Gum) for MonoGame games on desktop and
mobile. It adds only what Gum lacks:

- **Scaling and orientation** — Godot-style canvas scaling from a base resolution, separate portrait base, orientation
  states.
- **Safe area** — content stays clear of notches, rounded corners and system bars; the background still reaches the
  edges.
- **Motion** — springs and tweens, animated transitions between Gum states, show and hide animations.
- **Layers** — modal and popup layers with a dimming scrim, focus return and one cancel stack for Esc, B and Android
  back.
- **Gamepad and focus** — the focus frame hides from the mouse and comes back on the first key press, arrow keys,
  LB / RB paging, a choice of which gamepads drive the menus.
- **Scrolling** — touch inertia for any Gum `ScrollViewer` or `ListBox`, snapping to items and pages.
- **Gestures and drag and drop** — long press; drag and drop with the mouse, a finger, a gamepad or the keyboard.
- **Widgets** — toasts and a virtual list for thousands of rows.
- **UI sound events** — molib tells the game what the player did; the game plays its own sounds.

Every decision is written down in the [ADRs](https://github.com/ponttorez/molib/blob/master/adr/README.md); each
section below links to the one that describes the module in detail.

## Installation

```sh
dotnet add package Molib
```

molib targets `net10.0` and depends on `Gum.MonoGame` 2026.11.2.1. It compiles against MonoGame 3.8.6 but does not
bring it: the game references its own `MonoGame.Framework.DesktopGL`, `.Android` or `.iOS`. molib has no platform
code and is compatible with trimming and Native AOT
([AOT](https://github.com/ponttorez/molib/blob/master/adr/foundation/aot.md)).

## Setup

Create one `MolibService` after Gum and update it right after Gum every frame. The back buffer must keep its contents
between render target switches: molib fades whole elements through render targets.

```csharp
public sealed class MyGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private MolibService? _molib;

    public MyGame()
    {
        _graphics = new GraphicsDeviceManager(this);
        _graphics.PreparingDeviceSettings += (_, e) =>
            e.GraphicsDeviceInformation.PresentationParameters.RenderTargetUsage = RenderTargetUsage.PreserveContents;
    }

    protected override void Initialize()
    {
        GumService.Default.Initialize(this);
        GumService.Default.UseKeyboardDefaults();
        GumService.Default.UseGamepadDefaults();
        _molib = new MolibService(this, GumService.Default);
        _molib.Diagnostics.Warning += (code, message) => Debug.WriteLine($"{code}: {message}");
        _molib.Scaling.Enable(1280, 720);
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        GumService.Default.Update(gameTime);
        _molib!.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _molib?.Dispose();
        }

        base.Dispose(disposing);
    }
}
```

`Diagnostics.Warning` reports misuse that does not throw, such as a missing `PreserveContents` or a drop target
without its states ([architecture](https://github.com/ponttorez/molib/blob/master/adr/foundation/architecture.md)).

### Lifetime and threads

- `Dispose` finishes whatever is running instead of freezing it: animations jump to their end, open layers and toasts
  close, a drag ends, Gum's settings that molib changed come back, and molib's handlers leave your elements. After it
  you may create a new `MolibService`.
- When a screen goes away but its elements live on, take molib off them: `LongPress.Detach()`,
  `ScrollInertia.Detach()`, `molib.Sound.Detach(screen)`, `molib.DragDrop.Remove(element)`.
- Use molib from the game thread only. Events and task continuations run there, inside `MolibService.Update`.
- Details: [architecture](https://github.com/ponttorez/molib/blob/master/adr/foundation/architecture.md), sections
  «Disposal» and «Threads».

### Text beyond Latin

Gum's built-in font has Latin letters only. For Cyrillic or other scripts, rasterize a TTF with KernSmith
(`KernSmith.MonoGameGum` and `KernSmith.Rasterizers.StbTrueType`; the managed rasterizer needs KernSmith 0.25 or newer,
so pin the `KernSmith*` packages to one version such as 0.28.0), register the font before any text exists, add the
character ranges and make it the font of every Gum text style:

```csharp
CustomSetPropertyOnRenderable.InMemoryFontCreator =
    new KernSmithFontCreator(GraphicsDevice, RasterizerBackend.StbTrueType);
KernSmithFontCreator.RegisterFont("Inter", File.ReadAllBytes("Inter-Regular.ttf"));
KernSmithFontCreator.RegisterFont("Inter", File.ReadAllBytes("Inter-SemiBold.ttf"), "Bold");
BmfcSave.AddFontRange("1024-1119,8192-8303");   // Cyrillic, punctuation
var style = Gum.Forms.DefaultVisuals.V3.Styling.ActiveStyle;
style.Text.Normal.SetValue("Font", "Inter", "string");
style.Text.Strong.SetValue("Font", "Inter", "string");
style.Text.Emphasis.SetValue("Font", "Inter", "string");
TextRuntime.UseFontOversampling = true;          // sharp text when the zoom is not 1
```

Details and pitfalls: [localization](https://github.com/ponttorez/molib/blob/master/adr/integration/localization.md).

## Modules

### Scaling and orientation

```csharp
molib.Scaling.Enable(1280, 720);              // one base; the canvas grows to fill the window, nothing is cut off
molib.Scaling.Enable(1920, 1080, 1080, 1920); // or a separate base for portrait
molib.Scaling.UserScale = 1.25f;              // the player's interface scale
Vector2 canvas = molib.Scaling.ScreenToCanvas(worldPointInPixels);
```

On every orientation change molib applies the state `Portrait` or `Landscape` of the category `Orientation` to the Gum
trees, so a screen made in the Gum editor rearranges itself without code. Elements the game adds to `Root` itself get
the current state from `molib.Scaling.ApplyOrientation(element)`.
[Scaling](https://github.com/ponttorez/molib/blob/master/adr/platform/scaling.md)

### Safe area

```csharp
molib.SafeArea.Source = new MyAndroidSafeAreaSource(activity); // the game implements ISafeAreaSource per platform
molib.SafeArea.Attach(screenContainer);                        // the container keeps clear of the insets
```

molib does not ask the OS itself: the game's platform head reports the insets in pixels through `ISafeAreaSource`. The
ADR describes how to write the source on Android (`WindowInsets`) and iOS (`SafeAreaInsets`).
[Safe area](https://github.com/ponttorez/molib/blob/master/adr/platform/safe-area.md)

### State transitions

A visual animates between its Gum states when its `ApplyState` goes through molib:

```csharp
public sealed class MyButtonVisual : InteractiveGue
{
    private readonly StateTransitions? _transitions;

    public MyButtonVisual(MolibService molib)
    {
        _transitions = molib.Transitions.Create(this)
            .For("ButtonCategory", MotionPreset.Smooth)
            .For("ButtonCategory", to: "Pushed", MotionPreset.Snappy);
    }

    public override void ApplyState(StateSave state)
    {
        if (_transitions is null)
        {
            base.ApplyState(state);   // Gum may apply a state from the base constructor
            return;
        }

        _transitions.Apply(state, base.ApplyState);
    }
}
```

Without visuals of its own, a game can use molib's subclasses of the standard Gum visuals (`Molib.Visuals.ButtonVisual`,
`CheckBoxVisual`, `ListBoxItemVisual`). `molib.Motion.TimeScale` slows every molib animation down for debugging.
[State transitions](https://github.com/ponttorez/molib/blob/master/adr/motion/state-transitions.md),
[motion core](https://github.com/ponttorez/molib/blob/master/adr/motion/motion-core.md)

### Show and hide

```csharp
await panel.ShowAsync(molib, ShowHide.FadeSlideUp);
bool finished = await panel.HideAsync(molib, ShowHide.Fade, HideAction.Remove);
```

A container fades as a whole through a render target. The render target is exactly the size of the element, so
anything drawn outside it (a soft shadow) must lie inside the element's bounds.
[Show and hide](https://github.com/ponttorez/molib/blob/master/adr/motion/show-hide.md)

### Layers and cancel

```csharp
LayerHandle layer = molib.Layers.ShowModal(dialog, new LayerOptions { InitialFocus = closeButton });
LayerCloseReason reason = await layer.Closed;    // Cancel, ScrimClick or Code
molib.Layers.CancelUnhandled += (_, _) => GoBack();
```

Esc, gamepad B and Android back close the top layer; with no layers open the press arrives as `CancelUnhandled`.
[Layers](https://github.com/ponttorez/molib/blob/master/adr/interaction/layers.md)

### Focus and gamepad

```csharp
molib.Input.FocusScreen(mainMenu, initialFocus: playButton);
molib.Input.SetPaging(tabs, step => SelectTab(currentTab + step));   // LB / RB, Ctrl+PageUp / Ctrl+PageDown
molib.Input.UiGamepads = UiGamepads.Only(0);                         // only the first gamepad drives the menus
molib.Input.LastDeviceChanged += (_, _) => ShowButtonHints(molib.Input.LastDevice);
```

[Focus and gamepad](https://github.com/ponttorez/molib/blob/master/adr/interaction/focus-and-gamepad.md)

### Inertial scrolling and snapping

```csharp
scrollViewer.UseInertia(molib);
carousel.UseInertia(molib, new InertiaOptions { Snap = ScrollSnap.Items, SnapAlign = SnapAlign.Center })
    .SnapChanged += (_, e) => ShowDot(e.Index);
```

[Scrolling](https://github.com/ponttorez/molib/blob/master/adr/interaction/scrolling.md)

### Long press

```csharp
LongPress longPress = button.Visual.UseLongPress(molib);
longPress.Triggered += (_, _) => OpenContextMenu();
button.Click += (_, _) =>
{
    if (!longPress.ConsumedClick)
    {
        Activate();
    }
};
```

[Gestures](https://github.com/ponttorez/molib/blob/master/adr/interaction/gestures.md)

### Drag and drop

```csharp
molib.DragDrop.AddSource(slotVisual, () => slot.Item);   // null: nothing to drag
molib.DragDrop.AddTarget(
    weaponSlotVisual,
    canDrop: data => data is Weapon,
    drop: data => Equip((Weapon)data));
```

The mouse and a finger drag a copy of the source; a gamepad or the keyboard picks up and puts down with X. A target
shows whether it accepts the item through the states of the category `DropTarget`.
[Drag and drop](https://github.com/ponttorez/molib/blob/master/adr/interaction/drag-drop.md)

### Toasts

```csharp
molib.Toasts.Show("Game saved");
molib.Toasts.Show(myToastVisual, new ToastOptions { Duration = 4, Placement = ToastPlacement.TopEnd });
```

Toasts queue per placement, take no focus and pause while the pointer is over them.
[Toasts](https://github.com/ponttorez/molib/blob/master/adr/widgets/toasts.md)

### Virtual list

```csharp
var list = new VirtualList<Player>
{
    ItemHeight = 56,
    CreateVisual = () => new PlayerRowVisual(),
    BindVisual = (visual, player, index) => ((PlayerRowVisual)visual).Show(player, index + 1),
    Items = players,                                     // an ObservableCollection updates the list by itself
};
list.ItemActivated += player => OpenProfile(player);
list.UseInertia(molib);
list.ScrollToIndex(4_320, ScrollAlign.Center);
```

Rows exist only for what is on screen; a gamepad selects by data index.
[Virtual list](https://github.com/ponttorez/molib/blob/master/adr/widgets/virtual-list.md)

### UI sound events

```csharp
molib.Sound.Attach(screen);                                   // controls under it report their actions
molib.Sound.Triggered += (kind, element) => PlayUiSound(kind); // Hover, Click, Toggle, Select, LayerOpened, ...
molib.Sound.Mute(silentButton);
```

Values set by code stay silent; only the player's actions are reported.
[Sound](https://github.com/ponttorez/molib/blob/master/adr/integration/sound.md)

### Localization

molib has no text of its own; Gum's localization translates every string key, including toasts.
[Localization](https://github.com/ponttorez/molib/blob/master/adr/integration/localization.md)

## Development

Requires the .NET SDK 10 ([`global.json`](https://github.com/ponttorez/molib/blob/master/global.json)).

```sh
dotnet build Molib.slnx                                 # every warning is an error
dotnet format Molib.slnx --verify-no-changes            # code style check
dotnet pack src/Molib -c Release -o artifacts           # NuGet package
```

CI runs the same checks on Linux, Windows and macOS and publishes the package on a `v*` tag
([CI](https://github.com/ponttorez/molib/blob/master/adr/foundation/ci.md)). Notes for coding agents are in
[`CLAUDE.md`](https://github.com/ponttorez/molib/blob/master/CLAUDE.md); code style is in
[code-style](https://github.com/ponttorez/molib/blob/master/adr/foundation/code-style.md); licenses of dependencies
are in [`THIRD-PARTY-NOTICES.md`](https://github.com/ponttorez/molib/blob/master/THIRD-PARTY-NOTICES.md).

## License

[MIT](https://github.com/ponttorez/molib/blob/master/LICENSE)
