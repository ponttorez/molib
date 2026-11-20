# molib — notes for agents

molib is a thin C# library on top of [Gum](https://github.com/vchelaru/Gum) for MonoGame games on desktop and mobile.
It adds only what Gum lacks: Godot-style scaling, safe area, animated state transitions and show/hide, layers with a
cancel stack, gamepad and focus polish, inertial scrolling with snapping, drag and drop, long press, toasts, a virtual
list and UI sound events.

## Specification

The specification is the set of ADRs in [`adr/`](adr/README.md). Read the index first, then, before touching any area:

1. [`adr/foundation/code-style.md`](adr/foundation/code-style.md) — rules every line of code follows;
2. [`adr/foundation/architecture.md`](adr/foundation/architecture.md) — `MolibService`, modules, update order;
3. the ADR of the area itself.

How to read an ADR:

- Statements marked «Verified» were checked against Gum 2026.11.2.1 by running code. Trust them, but
  re-check after a Gum update.
- API snippets (`molib.Input.ShowScreen(...)`, `UseLongPress`, `VirtualList<T>` and so on) are the intended design, not
  existing code. Before implementing, check the Gum source and docs: if Gum already does it, or the design does not fit
  Gum, change the ADR first and tell the user why.
- An ADR describes the current decision. If code and ADR disagree, the ADR is fixed in the same commit as the code.

## Rules that are easy to miss

- **Language.** Everything is in English: code, XML docs, messages, ADRs, READMEs, this file, commits. The library
  itself has no player-facing text.
- **Comments.** No `//` or `/* */` in C#. The only explanations are `///` XML docs; the reason behind a decision goes
  into `<remarks>` with an ADR link: `ADR: interaction/layers — Focus.`
- **Gum first.** Use Gum's public API; no reflection into Gum, no copied Gum sources, no forks.
- **Warnings are errors.** `dotnet build` must show 0 warnings, `dotnet format --verify-no-changes` must pass.
  Suppression only via `[SuppressMessage(..., Justification = "...")]`.
- **One change, one commit:** code and the ADR together.
- **No automated tests or benchmarks.**

## Working with the user

- Commit and push only when the user asks. Never create `v*` tags: a tag publishes the package to nuget.org.
- When you are not sure whether Gum can do something, check it: read the Gum source or write a small program against
  the package. Do not guess.

## Out of scope

Decided not to do. Do not add these, even "for completeness"; if one seems needed, ask the user.

- Own layout engine, own renderer, own widget set: Gum does this.
- Rounded corners and procedural shapes: all decoration is 9-patch.
- Haptics and vibration.
- Reduced motion setting.
- IME composition (Chinese, Japanese, Korean).
- Variable row height in `VirtualList` (Gum lists cover it).
- Long press on keyboard or gamepad.
- Clipboard helpers beyond what Gum `TextBox` has, UI inspector.
- Automated tests and benchmarks.

## Commands

```sh
dotnet build Molib.slnx                                 # 0 warnings
dotnet format Molib.slnx --verify-no-changes            # code style
dotnet pack src/Molib -c Release -o artifacts           # NuGet package
```
