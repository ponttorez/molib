# Third-party notices

molib is distributed under the [MIT License](LICENSE). It does not vendor third-party source code or binaries:
every dependency below is fetched from NuGet and keeps its own license. This file lists them so that a game
shipping molib knows which notices to carry. It is informational, not legal advice.

Versions are the ones pinned in [Directory.Packages.props](Directory.Packages.props) and resolved by restore.
Update this file in the same commit as any dependency change.

## Shipped with a game

A game shipping molib carries these: Gum arrives with molib, and MonoGame is the framework the game already uses
(molib compiles against `MonoGame.Framework.DesktopGL` but does not ship it; the game brings its own
`MonoGame.Framework.*` package for its platform).

### MIT

| Component | Version | Source | Arrives via |
| --- | --- | --- | --- |
| Gum.MonoGame (MonoGameGum) | 2026.11.2.1 | https://github.com/vchelaru/Gum | molib |
| FlatRedBall.GumCommon | 2026.11.2.1 | https://github.com/vchelaru/Gum | Gum.MonoGame |
| FlatRedBall.InterpolationCore | 2025.4.22.1 | https://github.com/vchelaru/FlatRedBall (no license metadata in the package; FlatRedBall is MIT) | Gum.MonoGame |
| NVorbis | 0.10.4 | https://github.com/NVorbis/NVorbis | Gum.MonoGame |
| SharpCompress | 0.48.0 | https://github.com/adamhathcock/sharpcompress | Gum.MonoGame |
| TextCopy | 6.2.1 | https://github.com/CopyText/TextCopy | Gum.MonoGame |
| BrotliSharpLib | 0.3.3 | https://github.com/master131/BrotliSharpLib | Gum.MonoGame |
| Microsoft.Extensions.DependencyInjection.Abstractions | 7.0.0 | https://github.com/dotnet/runtime | Gum.MonoGame |
| System.Data.DataSetExtensions | 4.5.0 | https://github.com/dotnet/runtime | Gum.MonoGame |

### MS-PL or Apache-2.0

| Component | Version | Source | Arrives via |
| --- | --- | --- | --- |
| CsvHelper | 33.1.0 | https://github.com/JoshClose/CsvHelper | Gum.MonoGame |

### Microsoft Public License (MS-PL)

| Component | Version | Source | Arrives via |
| --- | --- | --- | --- |
| MonoGame.Framework | 3.8.6.1 | https://github.com/MonoGame/MonoGame | the game (molib compiles against DesktopGL) |

MS-PL: distributing MonoGame in compiled form is allowed under a license that complies with MS-PL; distributing
its source requires including the MS-PL text.

### zlib

| Component | Version | Source | Arrives via |
| --- | --- | --- | --- |
| SDL2 (native, `MonoGame.Library.SDL`) | 2.32.10.2 | https://github.com/libsdl-org/SDL | MonoGame.Framework.DesktopGL |

### LGPL-2.0-or-later, with BSD-3-Clause portions

| Component | Version | Source | Arrives via |
| --- | --- | --- | --- |
| OpenAL Soft (native `libopenal`, `MonoGame.Library.OpenAL`) | 1.24.3.4 | https://github.com/kcat/openal-soft | MonoGame.Framework.DesktopGL |

OpenAL Soft is linked dynamically as a separate shared library. A game must ship its license text and keep the
library replaceable by the user. The package's own LICENSE file only reproduces the BSD-3-Clause portions.

## Build time only

Not shipped with a game: analyzers run inside the compiler.

| Component | Version | License | Source |
| --- | --- | --- | --- |
| Roslynator.Analyzers | 5.0.1 | Apache-2.0 | https://github.com/dotnet/roslynator |
| Microsoft.CodeAnalysis.BannedApiAnalyzers | 5.0.0 | MIT | https://github.com/dotnet/roslyn |
| MinVer (package version from git tags) | 8.0.0 | Apache-2.0 | https://github.com/adamralph/minver |
