# CI and releases

## Decision

CI on GitHub Actions ([`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)) checks what one machine cannot:
the build and style on Linux, Windows and macOS and the NuGet package. CI does not check behavior: there are no
automated tests. The package version comes from the git tag; publishing to nuget.org starts on a `v*` tag and goes
through trusted publishing, with no API key in the secrets.

## When it runs

- a push to `master`, a pull request, a `v*` tag, a manual run (`workflow_dispatch`);
- a new run for the same branch cancels the unfinished previous one;
- the repository is public, so GitHub Actions minutes, macOS included, are free.

## Jobs

| Job | Machine | What it does |
| --- | --- | --- |
| Build (Linux) | `ubuntu-latest` | Builds `Molib.slnx`, `dotnet format --verify-no-changes` |
| Build (Windows) | `windows-latest` | Build |
| Build (macOS) | `macos-latest` | Build |
| Package | `ubuntu-latest` | `dotnet pack`, checks the package contents; `.nupkg` and `.snupkg` as an artifact |
| Publish | `ubuntu-latest` | Only on a `v*` tag and only if every job above is green: publishes to nuget.org |

## Package version

- [MinVer](https://github.com/adamralph/minver) computes the version from git tags with the `v` prefix
  (`MinVerTagPrefix` in `src/Molib/Molib.csproj`):
  - a commit tagged `v0.3.0` — version `0.3.0`;
  - commits after the tag — a pre-release version like `0.3.1-alpha.0.5`;
  - no tags — `0.0.0-alpha.0`.
- The `.csproj` stores no version number.
- So CI fetches the whole history (`fetch-depth: 0`).
- `EnablePackageValidation` checks the package at build time. After the first release `PackageValidationBaselineVersion`
  is set; an incompatible public API change without a new major version then becomes an error.
- CI builds are deterministic (`ContinuousIntegrationBuild`) and carry SourceLink: while debugging a game, one can step
  into the molib sources.

## Releasing

The first release happens when the user decides; until then the package is not published.

1. Make sure `master` is green.
2. Create and push the tag: `git tag v0.1.0 && git push origin v0.1.0`.
3. CI runs every job and, if they are green, publishes the package to nuget.org.

## Publishing setup (once)

Trusted publishing: nuget.org gives CI a short-lived key for GitHub's OIDC token; there is no long-lived key anywhere.

1. On nuget.org: account → **Trusted Publishing** → a new policy: repository owner `ponttorez`, repository `molib`,
   workflow file `ci.yml`, environment `nuget`.
2. In the GitHub repository: **Settings → Secrets and variables → Actions → Variables** — the variable `NUGET_USER`
   with the nuget.org user name (not the e-mail).
3. GitHub creates the `nuget` environment on the first run; **Settings → Environments** can require a manual approval
   for publishing.

Until this is done, the Publish job on a tag fails at the nuget.org login; the other jobs do not depend on it.

## Locally

The same checks without CI:

```sh
dotnet build Molib.slnx
dotnet format Molib.slnx --verify-no-changes
dotnet pack src/Molib -c Release -o artifacts
```
