# AOT

## Decision

molib guarantees compatibility with trimming and Native AOT, and the game turns AOT on. AOT matters either way: on
iOS all .NET code is compiled ahead of time, and on desktop Native AOT is the game's choice (fast start, one
executable). molib code has no trimming or AOT warnings: the analyzers make them build errors of the library.

## What the library does

- `IsAotCompatible` in `src/Directory.Build.props` turns on the trimming and AOT analyzers: reflection, run-time code
  generation and dynamic type loading in molib are build errors (`IL2xxx`, `IL3xxx`).
- molib uses no reflection and no Gum string bindings: only the lambda overloads of `SetBinding`, no
  `DisplayMemberPath` and no `RegisterRuntimeProperty(object, string)`.
- If types ever need to be kept from trimming, molib puts the description (`ILLink.Descriptors.xml`) into the package
  itself, so the game does not have to.

## What the game does

This cannot go into the library: publishing settings belong to the executable project.

- `PublishAot`, `SelfContained`, `RuntimeIdentifier` in the project of the platform head;
- the Gum project in JSON format (`.gumj`, converted with `gumcli convert-to-json`): an XML `.gumx` project loads
  through `XmlSerializer` and fails under AOT;
- `.fnt` fonts, if the game uses them, only in text format;
- no generic Gum components and no string bindings in its own code.

## Warnings

- molib code must have no trimming or AOT warnings.
- Third-party assemblies (MonoGame, Gum, CsvHelper, BrotliSharpLib) report summary `IL2104` warnings when a game
  publishes with AOT: they are built without full trimming annotations. These are their warnings, not ours. A new
  warning that points at molib code is an error.
- So a game whose warnings are errors publishes with AOT using `-p:TreatWarningsAsErrors=false` (on iOS,
  `ILLinkTreatWarningsAsErrors=false`): trimming warnings of third-party assemblies stay warnings, while compiler and
  analyzer warnings are still errors.

## Tools

Native AOT needs `clang` and `lld` on Linux, Visual Studio with the C++ workload on Windows, and Xcode on macOS.
