# Release workflow

DA Organizer releases are portable, self-contained Windows x64 archives. The application profile remains under `%LOCALAPPDATA%\DAOrganizer` (or `DAORGANIZER_DATA_DIR`) and passwords remain in Windows Credential Manager; neither is copied into a release archive.

## Version and channels

1. Choose either a stable tag (`vMAJOR.MINOR.PATCH`) or beta tag (`vMAJOR.MINOR.PATCH-beta.N`). Leading zeroes and other prerelease labels are rejected.
2. Before tagging, update the single `<Version>` in `src/DAOrganizer.App/DAOrganizer.App.csproj` to the exact tag text without `v` (including `-beta.N`), add release notes to `CHANGELOG.md`, and merge that reviewed change normally. The workflow never rewrites source.
3. Run manual **Build release candidate** workflow with intended tag and `publish=false` (default). It builds, tests, packages, verifies, and uploads an Actions artifact without publishing.
4. After reviewing candidate, create and push matching tag. Tag push repeats candidate validation and uploads artifact; tag alone cannot publish a GitHub Release.
5. To publish, deliberately dispatch workflow with existing matching tag and `publish=true`. This run checks out tag, repeats full candidate validation, downloads its own artifact, verifies checksum/manifest/archive again, then calls `gh release create --verify-tag`. `release-production` environment may require reviewers as an extra gate; workflow's explicit input is mandatory even if environment has no protection rules. Do not dispatch publication before release decision and signing policy review.

The workflow intentionally matches all `v*` tags so unsupported or malformed release tags fail visibly instead of being silently ignored. A tag whose base version differs from the app project also fails.

## Candidate contents and manifest

The Windows job restores and Release-builds the solution, runs unit/protocol tests, headless UI checks, and the demo gallery, then calls the existing `tools/Package.ps1`. `DAOrganizer.Release` writes entries in ordinal order with a fixed ZIP timestamp, so identical package bytes produce an identical ZIP. It also verifies the expected executable, notices, changelog, and license directory.

Each channel has its own `release-stable.json` or `release-beta.json`. Manifest schema 1 contains:

- channel, semantic version, and source tag;
- artifact name, GitHub release URL, byte size, and lowercase SHA-256;
- changelog/release-notes reference;
- minimum client version, minimum API version (`none`, because no central API is required), and supported profile schema range (1–4);
- explicit `signed: false` and `informationalOnly: true` trust fields.

The adjacent `.sha256` file detects accidental corruption when it is obtained through a trusted path. **It does not authenticate the publisher or release.** There is no approved code-signing identity, manifest-signing key, key-rotation/revocation policy, or client verification policy. Therefore manifests are informational and updater activation remains disabled. Do not add an updater or claim authenticity until those decisions are reviewed.

## Local and Windows dry runs

Run helper tests on any .NET 10 host:

```powershell
dotnet run --project tools/DAOrganizer.Release -c Release -- self-test
```

On Windows, perform the complete packaging dry run without a tag or publication:

```powershell
dotnet restore DAOrganizer.slnx -m:1
dotnet build DAOrganizer.slnx -c Release --no-restore -m:1 -p:UsedAvaloniaProducts=
dotnet test tests/DAOrganizer.Tests -c Release --no-build -m:1
dotnet tests/DAOrganizer.UiChecks/bin/Release/net10.0/DAOrganizer.UiChecks.dll artifacts/ui-checks
dotnet tests/DAOrganizer.UiChecks/bin/Release/net10.0/DAOrganizer.UiChecks.dll artifacts/gallery --gallery
.\tools\Package.ps1 -Output artifacts\package
dotnet run --project tools/DAOrganizer.Release -c Release -- create --tag v0.15.0 --project src/DAOrganizer.App/DAOrganizer.App.csproj --package artifacts/package --output artifacts/release --repository OWNER/REPOSITORY --notes-url https://github.com/OWNER/REPOSITORY/blob/v0.15.0/CHANGELOG.md
Get-FileHash artifacts\release\DAOrganizer-0.15.0-win-x64.zip -Algorithm SHA256
```

Verify candidate locally with `dotnet run --project tools/DAOrganizer.Release -c Release -- verify --tag v0.15.0 --project src/DAOrganizer.App/DAOrganizer.App.csproj --output artifacts/release --repository OWNER/REPOSITORY`. Compare `Get-FileHash` with `.sha256`, inspect ZIP/manifest, and keep generated packages under ignored `artifacts/`.

## Rollback and recovery

An extracted portable app can be rolled back by closing DA Organizer, retaining the newer extracted directory, and launching a previously retained release from a separate directory. Do not overwrite a working extraction in place. Profiles and credentials are external to either directory, so replacing binaries does not intentionally delete them.

Database migrations are a separate limit: a newer app may upgrade the profile, and an older binary may not understand the newer schema. Use the migration backup created beside the profile only after closing all DA Organizer processes, and preserve both the current profile and backup before recovery. Restoring an older database loses changes made after that backup. If no compatible backup exists, return to the newer client rather than forcing a downgrade. A failed release can be marked as a GitHub prerelease or removed from recommendations, but published tags and artifacts should be retained for audit; issue a corrected version instead of silently replacing bytes.
