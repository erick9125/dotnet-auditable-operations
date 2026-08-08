# Releasing

## One-time setup

1. Create a NuGet API key scoped to `erick9125.AuditableOperations` (or to *Push new packages* for
   the very first publish, since the package id does not exist yet).
2. Add it as a repository secret named `NUGET_API_KEY`.
3. Recommended: create a GitHub environment named `nuget` with a required reviewer and move the
   `release` job under it, so publishing needs an explicit approval.

## Cutting a release

1. Update `CHANGELOG.md` with a `## <version>` section. The workflow refuses to publish a version
   that has no entry.
2. Update `<Version>` in `src/AuditableOperations/AuditableOperations.csproj` so local builds match
   the release. The published version comes from the tag, not from the csproj.
3. Commit, then tag and push:

```bash
git tag v0.1.0
git push origin v0.1.0
```

The `release` workflow then:

- pins the SDK from `global.json`
- validates that the tag is a semantic version and that the changelog documents it
- builds, runs unit tests, and runs the PostgreSQL integration tests
- packs with `-p:Version=<tag>` plus a `.snupkg` symbol package
- verifies the package: no `frameworkReference`, XML documentation present, symbols produced
- pushes to nuget.org and creates a GitHub release with the packages attached

## Dry run

Use **Run workflow** on the `release` workflow with `dry_run` left checked. It performs every step
including packing and validation, uploads the packages as build artifacts, and stops before
publishing.

## Version source of truth

The tag is authoritative. `<Version>` in the csproj is the value used by local builds and by CI's
preview package; the release workflow overrides it with `-p:Version`. Keeping them in sync is
convention, not a requirement — the changelog check is what prevents an accidental release.

## Pre-release versions

Tags such as `v0.2.0-rc.1` are accepted and publish as pre-release packages. The changelog still
needs a matching section.

## Reproducibility

`Directory.Build.props` enables `ContinuousIntegrationBuild` on GitHub Actions, along with
`PublishRepositoryUrl` and `EmbedUntrackedSources`. Source Link ships with the .NET SDK, so consumers
can step into the library from a debugger. `ContinuousIntegrationBuild` is deliberately **not** set
for local builds, because normalized paths break stepping into a locally built assembly.
