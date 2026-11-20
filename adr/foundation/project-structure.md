# Project structure

## Decision

One repository, one solution `Molib.slnx`. The library is one project, `src/Molib`, with no platform code: MonoGame is
referenced only for compilation, and the game brings the actual platform. All package versions live in one file,
shared build settings in `Directory.Build.props`. The target framework is `net10.0`. The library reaches a game as the
NuGet package `Molib`; the library guarantees AOT compatibility and the game turns AOT on itself ([aot.md](aot.md)).

## Tree

```text
molib/
├── README.md                     what molib adds and how to use it; the package page on nuget.org
├── CLAUDE.md                     notes for coding agents
├── LICENSE                       molib license (MIT)
├── THIRD-PARTY-NOTICES.md        licenses of dependencies
├── Molib.slnx                    solution
├── global.json                   .NET SDK version
├── NuGet.config                  package source: nuget.org only
├── Directory.Build.props         shared build and analysis settings
├── Directory.Packages.props      versions of every package, analyzers
├── BannedSymbols.txt             banned APIs
├── .editorconfig                 code style and rule levels
├── .gitignore, .gitattributes
├── .github/workflows/ci.yml      CI, see ci.md
├── adr/                          specification, see adr/README.md
└── src/
    ├── Directory.Build.props     library settings: XML docs, AOT
    └── Molib/                    the library
```

## Names

- **The name in text, the repository and its folder are `molib`**, in lower case: `github.com/<owner>/molib`,
  `~/Projects/molib`.
- **.NET identifiers are `Molib`**: namespace, project and assembly names, the NuGet package. .NET conventions require
  it (`PascalCase`).
- **Top-level service folders are in lower case:** `src/`, `adr/` and the ADR categories. This is common in .NET
  repositories.
- **Project folders match the project name:** `src/Molib/`. Folders inside a project match the namespace (`Motion/`).

## Projects

| Project | Kind | References | Purpose |
| --- | --- | --- | --- |
| `src/Molib` | library, NuGet package `Molib` | Gum.MonoGame; MonoGame for compilation only | Everything molib adds to Gum |

### The `src/Molib` library

- No platform code and no `#if` per platform. What depends on the OS (safe area, system theme) comes through
  interfaces the game implements.
- MonoGame is referenced with `PrivateAssets="All"`: the library compiles against DesktopGL but does not pull it into
  the game. The game references `MonoGame.Framework.DesktopGL`, `.Android` or `.iOS` itself. Gum does the same.
- The library does not need KernSmith: fonts are the game's choice.
- Folders inside follow the ADR areas, one folder = one namespace: `Platform/`, `Motion/`, `Interaction/`, `Widgets/`,
  `Integration/`, plus `Visuals/` — subclasses of the standard Gum visuals with state transitions
  ([motion/state-transitions.md](../motion/state-transitions.md)). A folder appears with the first code in it.

## Target framework

`net10.0` in every project. .NET 10 is the current LTS; support for .NET 8 and .NET 9 ended in November 2026. The Gum
and MonoGame packages are built for `net8.0` and run on .NET 10. The SDK version is pinned in `global.json` (10.0.100
or newer within 10.0.x).

## Dependencies

Versions live only in [`Directory.Packages.props`](../../Directory.Packages.props) (central package management);
`.csproj` files reference packages without a version. Transitive pinning is on: a version in this file also applies to
indirect dependencies.

| Package | Where | Why |
| --- | --- | --- |
| `Gum.MonoGame` | library | The UI foundation: layout, rendering, states, Forms, focus, modal windows |
| `MonoGame.Framework.DesktopGL` | library, compilation only | The engine; the game references its own MonoGame package for its platform |
| `MinVer` | library, build only | Package version from the git tag, see [ci.md](ci.md) |
| `Roslynator.Analyzers` | every project, build only | Analyzer, see [code-style.md](code-style.md) |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` | every project, build only | Bans the APIs in `BannedSymbols.txt` |

Rules:

- A new dependency or a version update is a separate commit: an edit of `Directory.Packages.props`, a build check and
  an edit of [`THIRD-PARTY-NOTICES.md`](../../THIRD-PARTY-NOTICES.md).
- Packages come only from nuget.org (`NuGet.config` clears other sources).
- No third-party sources, binaries or assets are copied into the repository.

## Using it in a game

- **The main way is the NuGet package `Molib`**: it is built from `src/Molib` with `dotnet pack`, includes the README,
  XML docs and symbols (`.snupkg`) and pulls in `Gum.MonoGame`; the game references MonoGame itself. The version comes
  from the git tag, publishing from CI ([ci.md](ci.md)).
- **While the library and a game are developed together** — a local package source: `dotnet pack src/Molib -o
  <folder>` and that folder in the game's `NuGet.config`. The game then checks exactly what will later go to nuget.org.
- To edit molib right from a game — a git submodule and a `ProjectReference` to `src/Molib/Molib.csproj`. This is
  temporary: a released game uses the package.

## AOT

The library is compatible with trimming and Native AOT (`IsAotCompatible` in `src/Directory.Build.props`); the game
turns AOT on. Rules for the library and the game — [aot.md](aot.md).

## Commands

```sh
dotnet restore Molib.slnx                                   # download dependencies
dotnet build Molib.slnx                                     # build, every warning is an error
dotnet format Molib.slnx --verify-no-changes                # style check
dotnet pack src/Molib -c Release -o artifacts               # NuGet package
```
