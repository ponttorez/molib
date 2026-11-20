# Safe area

## Decision

molib does not ask the OS about cutouts and system bars: the game provides the safe area insets through the
`ISafeAreaSource` interface, which its platform head implements. molib converts them from pixels to canvas units and
applies them to the containers the game marked as safe. Backgrounds and scrims still cover the whole screen; only the
content moves into the safe area.

## Source

```csharp
public interface ISafeAreaSource
{
    event EventHandler? Changed;
    Thickness GetInsetsInPixels();
}
```

- `Thickness` is a molib struct: the insets `Left`, `Top`, `Right`, `Bottom`. The source returns them in back buffer
  pixels, from the edges of the back buffer.
- The source raises `Changed` itself when the insets may have changed. The window size is not enough for this: a phone
  turned from one landscape side to the other keeps its size while the cutout moves to the other side; the system
  sends the insets a few frames after start; bars appear and hide without the window changing.
- `Changed` may be raised from any thread: molib only remembers a flag and calls `GetInsetsInPixels` on the game thread
  in the next `Update`.
- The platform head implements the source (section «Platform heads»); a desktop needs no source.
- `molib.SafeArea.Source = new AndroidSafeAreaSource(view, game.Window);`. Without a source the insets are zero.
  Assigning a source reads its insets at once.

## Applying

- molib rereads the source when it raised `Changed`, when the back buffer size or the Gum camera zoom changed, and when
  a source is assigned. Pixels are divided by the Gum camera zoom, not by `Scaling.Zoom`: the module does not depend on
  scaling and works with whatever the game chose. An idle frame costs a flag and three comparisons.
- A negative or non-numeric inset is a data error: the diagnostic `safeArea.badInsets` and 0 on that side.
- `molib.SafeArea.Insets` — the insets in canvas units, `InsetsInPixels` — in pixels as the source gave them;
  `Changed` — after `Insets` changed and the containers were updated.
- `molib.SafeArea.Attach(container)` — the container stretches over its parent minus the insets: `X = left`,
  `Y = top` from the top left corner, width and height `RelativeToParent` with `-(left + right)` and `-(top + bottom)`.
  When the insets change, molib updates these values itself. A second `Attach` of the same container does nothing;
  `Detach(container)` stops updating it. molib holds the containers by weak references: a screen the game throws away
  without `Detach` is not kept alive.
- Disposing the service leaves the source's event and forgets the containers; they keep their last insets, so the
  content does not jump under the cutout ([foundation/architecture.md](../foundation/architecture.md), section
  «Disposal»).
- Usually one root container of the screen is made safe. Modal windows and toasts shown by molib put their content into
  the safe area automatically, and the scrim over the whole screen.

## Platform heads

The game writes the safe area source for a phone in its platform head: the library has no platform code. Below is how
to write it on Android and iOS.

### Android

The source is a `View.IOnApplyWindowInsetsListener` on the game view that MonoGame creates.

- Android calls the listener on start, on rotation, when the cutout moves to the other side and when bars appear; the
  source remembers the insets and raises `Changed`.
- A view gets only what its parents did not consume, so the insets already relate to the view itself — both when the
  window draws edge to edge and when the system lays it out between the bars.
- Android 11 and newer: `WindowInsets.GetInsets(SystemBars | DisplayCutout)`. Android 9–10: the larger of
  `SystemWindowInset*` and `DisplayCutout.SafeInset*` on each side. Android 6–8: `SystemWindowInset*`.
- The back buffer is the part of the view given by `Window.ClientBounds`: on Android MonoGame letterboxes the picture if
  the aspect ratio of the preferred back buffer size does not match the view. The insets are counted from that part, so
  they are right even with bars. To have no bars, a game on a phone does not set `PreferredBackBufferWidth/Height` —
  then the back buffer equals the screen.
- The game's activity draws the window edge to edge: on Android 15 and newer the system does this for apps with target
  SDK 35+; on Android 11–14 the activity calls `Window.SetDecorFitsSystemWindows(false)` and sets
  `LayoutInDisplayCutoutMode.Always`. So the background goes under the bars and into the cutout while the content stays
  in the safe area. On a phone MonoGame turns on full-screen window mode (`WindowManagerFlags.Fullscreen`), so the
  status bar is usually hidden and the navigation bar visible.

### iOS

The source reads `UIView.SafeAreaInsets` of the game view.

- The view and the controller belong to MonoGame; their `SafeAreaInsetsDidChange` cannot be overridden. So the source
  puts a transparent view over the whole game view, without touches (`UserInteractionEnabled = false`): it has the same
  insets, and its `SafeAreaInsetsDidChange` raises `Changed` on every change — rotation, split screen on iPad.
- Pixels are the insets in points multiplied by `Layer.ContentsScale` of the game view: MonoGame computes the back
  buffer size with the same factor.

## Limits

- molib draws nothing in the unsafe area and does not hide system bars — those are the game's window settings.
- The insets are applied as a whole: a partial «background extended to the edge» is done in the game's layout (a
  background outside the safe container).
