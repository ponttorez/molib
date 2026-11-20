# Code style

## Decision

molib code is C# 14 on .NET 10 with nullable references on. Everything is written in English. The style is the
standard .NET one (4 spaces, braces on a new line, file-scoped namespaces), and it is not argued about by hand:
`.editorconfig`, the built-in .NET analyzers, Roslynator and the banned API list check it, and every warning is a build
error. molib extends Gum only through its public API and does not repeat what Gum already does. A frame without changes
allocates nothing; time comes only from `GameTime`.

## Language

- **Everything is in English:** code, names, XML docs, error and diagnostic messages, ADRs, every README,
  `THIRD-PARTY-NOTICES.md`, comments in configuration files, commits, pull request and issue descriptions.
- **The exception is text the player sees:** translations in localization files (CSV, RESX) are in their own languages.
  The library code has no player-facing text ([integration/localization.md](../integration/localization.md)).
- C# 14 is the language of the .NET 10 SDK with the `net10.0` target framework (why this one — in
  [project-structure.md](project-structure.md)). The language version is pinned by number, not `latest`: every compiler
  reads `latest` its own way. A build from SDK 10.0.404 reads it as C# 14, while Roslyn 5.13 in the editor's language
  server reads it as C# 15, and the editor flagged IDE0028 with a C# 15 fix (collection expression arguments,
  `[with(comparer)]`) that the build does not compile. The number goes up together with the target framework.
- `Nullable` is on everywhere. `!` (the null-forgiving operator) is not used: instead an explicit check — `?? throw` for
  a programmer error or `Debug.Assert` for an internal invariant.
- `ImplicitUsings` are on; other `using` directives are outside the namespace, `System.*` first.

## Formatting

- 4-space indent, lines up to 120 characters, LF, UTF-8, a newline at the end of the file, no trailing spaces.
- Braces on a new line (Allman) and always, even for a one-line `if`.
- File-scoped namespace (`namespace Molib.Motion;`) that matches the folder path from the project root. The folder sets
  the casing; the `PascalCase` rule does not apply to namespaces: platform names are written as usual (`iOS`, not
  `Ios`).
- One top-level type per file, file name = type name. Nested types only private and small.
- `var` when the type is obvious from the right-hand side (`new`, a cast); an explicit type for built-in types (`int`,
  `string`, `float`).
- Member order: constants → static fields → fields → constructors → properties → events → public methods → protected →
  private.
- `dotnet format` checks formatting; there is no need to fix by hand what it fixes itself.

## Naming

| What | Style | Example |
| --- | --- | --- |
| Types, methods, properties, events | `PascalCase` | `LayerStack`, `ShowAsync`, `IsOpen` |
| Interfaces | `I` + `PascalCase` | `ISoundHook` |
| Type parameters | `T` + `PascalCase` | `TValue` |
| Constants | `PascalCase` | `DefaultStiffness` |
| Public and internal fields | `PascalCase` (prefer a property) | — |
| Private fields, static ones included | `_camelCase` | `_velocity` |
| Parameters and locals | `camelCase` | `deltaSeconds` |
| Async methods | `Async` suffix | `HideAsync` |
| Extension methods | class `<Type>Extensions` | `GraphicalUiElementExtensions` |

No Hungarian notation and no abbreviations except common ones (`Ui`, `Id`). Not `dt` — write `deltaSeconds`.

## Public API

- Everything is `internal` by default; `public` only for what the game uses.
- Classes are `sealed` by default; inheritance is opened on purpose.
- Every public member has XML docs (`CS1591` is an error in the library). The docs say what the member does and what
  its conditions are, rather than repeating its name.
- Gum types (`GraphicalUiElement`, `FrameworkElement`, `InteractiveGue`) are used in the molib API directly, without
  wrappers.
- Settings are `init` properties or an options object, not long parameter lists and sets of `bool` flags.

## Working with Gum

- **Gum first.** Before writing something, check whether Gum has it: the docs and the sources of `vchelaru/Gum`. If it
  does, use it; if it almost does, extend it.
- **Public API only.** No reflection into Gum internals, no copying its sources into molib, no forks. If there is no way
  without changing Gum, that is a reason for an issue or a PR to Gum, not for a workaround.
- **Extend from the outside:** extension methods, subclasses of Forms controls, event subscriptions (`RollOn`, `Click`,
  `ScrollChanged`), overrides of virtual methods (`UpdateState`).
- A Gum version update follows the dependency rules ([project-structure.md](project-structure.md), section
  «Dependencies»).

## State

- No mutable static state in molib: everything that changes at run time lives in objects the game creates. Constants,
  pure functions and extension methods may be static.
- The one exception is what Gum imposes (`GumService.Default`, static roots): molib uses it but adds no global state of
  its own.

## Errors

- **A programmer error** (a wrong argument, a call in the wrong state) is an exception: `ArgumentNullException.ThrowIfNull`,
  `ArgumentOutOfRangeException.ThrowIf*`, `InvalidOperationException` with a clear message.
- **A data error** (a missing file, a bad value from settings) is a warning through the molib diagnostics hook plus a
  default value; work goes on, and a repeat of the same warning stays silent.
- Internal invariants — `Debug.Assert`.
- Writing to the console and to `Debug.WriteLine` is banned (`BannedSymbols.txt`): everything goes through the
  diagnostics hook, and the game decides where to print it.
- Exceptions are not swallowed silently: a `catch` either handles a specific type or reports to diagnostics.

## Hot paths

A hot path is anything called every frame or for every element: updating motion, scrolling, layers, handling input.

- **A frame without changes allocates nothing.** A hot path has no:
  - LINQ;
  - lambdas and local functions that capture variables (non-static ones);
  - string interpolation and concatenation, `ToString()` on enums and numbers;
  - `foreach` over `IEnumerable<T>` and collection interfaces (the enumerator is boxed); `foreach` over `List<T>` and
    arrays is fine;
  - boxing: passing a `struct` as `object` or an interface, `params object[]`;
  - event subscription and unsubscription;
  - `Task` creation.
- Buffers and lists live on their owner and are reused (`Clear()`, not `new`). Temporary arrays — `stackalloc`,
  `Span<T>`, `ArrayPool<T>`.
- Small values (vectors, spring state) are `readonly struct` or `struct`, passed by `in`/`ref` when it matters.
- **Time comes only from `GameTime`:** `float deltaSeconds = (float)gameTime.ElapsedGameTime.TotalSeconds`.
  `DateTime.Now`, `Environment.TickCount` and the like are banned (`BannedSymbols.txt`). Nothing is counted «per frame»:
  every speed is multiplied by `deltaSeconds`.
- If nothing animates or scrolls, the molib update must return at once.

## Async

- Async methods are for waiting for animations and layers (`await layer.HideAsync()`). molib tasks complete on the game
  thread, inside `MolibService.Update`: the continuation after `await` may change the interface right away. Gum's
  `PlayAnimationAsync` cannot do this without `GumService.UseSingleThreadedAsync()`
  ([motion/show-hide.md](../motion/show-hide.md), section «Animations from the Gum editor»).
- Blocking waits are banned (`Task.Wait`, `.Result`, `Thread.Sleep` are in `BannedSymbols.txt`).
- `async void` only in event handlers.
- `ConfigureAwait(false)` is not used: continuations must stay on the game thread.
- Cancellation goes through a `CancellationToken`, the last parameter.

## Comments

- **C# code has no `//` and `/* */` comments.** The only form of explanation is `///` XML docs.
- `///` is written on every public member (required, checked by `CS1591`) and, when needed, on internal and private
  ones.
- `<summary>` — what the member does; `<remarks>` — why it is built this way: an invariant, a trap, non-obvious Gum
  behavior, a formula; `<param>`, `<returns>`, `<exception>` — the conditions of use.
- If the name and `///` still do not make clear what the code does, the code is rewritten: extract a method with a
  telling name, introduce a variable.
- The link to the decision goes into `<remarks>`: `ADR: interaction/layers — Focus restore.`
- No commented-out code and no `TODO` in code: tasks go into issues.
- No analyzer checks this rule: watch for it in review.

## Suppressing warnings

- By default a warning is fixed, not suppressed.
- When suppression is needed — `[SuppressMessage(..., Justification = "...")]` on the smallest scope, with the reason
  in `Justification`. `#pragma warning disable` is not used.
- Changing the level of a rule for the whole project is an edit of `.editorconfig` with a justification comment on the
  same line (it is configuration, not C# code) and, if it changes the style rules, an edit of this ADR.

## Commits

- English, imperative mood, subject up to 72 characters: `Add spring transitions for Forms states`.
- One commit, one logical change: code and ADR together.

## Tools and checks

| File | What it does |
| --- | --- |
| [`.editorconfig`](../../.editorconfig) | Formatting, language style, naming, analyzer rule levels |
| [`Directory.Build.props`](../../Directory.Build.props) | Shared settings: `net10.0`, nullable, `TreatWarningsAsErrors`, `AnalysisLevel = latest-recommended`, `EnforceCodeStyleInBuild` |
| [`src/Directory.Build.props`](../../src/Directory.Build.props) | For the library: required XML docs, AOT compatibility |
| [`Directory.Packages.props`](../../Directory.Packages.props) | Versions of every package and the analyzers applied to every project |
| [`BannedSymbols.txt`](../../BannedSymbols.txt) | Banned APIs with the reason |

Analyzers:

- **Built-in .NET analyzers** (`CA*`) at the `latest-recommended` level and the `IDE*` style rules checked at build time.
- **Roslynator** — extra quality rules; suggestions by default, with the rules that matter for per-frame code raised.
- **BannedApiAnalyzers** — bans the APIs in `BannedSymbols.txt` (`RS0030`).

Before a commit:

```sh
dotnet build Molib.slnx                         # 0 warnings: any warning is an error
dotnet format Molib.slnx --verify-no-changes    # formatting and style
```

CI repeats the same checks on Linux, Windows and macOS ([ci.md](ci.md)). There are no automated tests or benchmarks.
