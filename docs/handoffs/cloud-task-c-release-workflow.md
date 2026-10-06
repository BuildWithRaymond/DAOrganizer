# Codex Cloud Task C — Release workflow

Branch from `feature/cross-character-organization` at exact Phase 0 baseline commit `e1dd6765ac42f5afad1395d05b523b22bd7662a7`. Implement only Phase 9 of `docs/plans/CROSS_CHARACTER_ORGANIZATION_MASTER_PLAN.md`: a reviewable release workflow for the existing portable Windows .NET 10/Avalonia app. Do not implement an in-app updater.

## Ownership

You may create `.github/workflows/release.yml`, new release-manifest/checksum helpers and their tests under `tools/`, and release documentation under `docs/`. Do not edit `.github/workflows/build.yml`, `tools/Package.ps1`, app/Core/Game source, database migrations, profiles, or user data. Do not add secrets, raw captures, or generated packages to Git.

## Required behavior

- Trigger on SemVer stable tags (`vMAJOR.MINOR.PATCH`) and beta prerelease tags (`vMAJOR.MINOR.PATCH-beta.N`). Reject malformed tags and any version that disagrees with app/package version. Document version-bump procedure instead of silently rewriting source during release.
- On Windows, restore, Release-build, run unit tests, headless UI checks, demo/gallery checks, and invoke existing `tools/Package.ps1` for a self-contained `win-x64` portable package. Use the existing package layout and preserve `%LOCALAPPDATA%` profiles and Windows Credential Manager data.
- Produce a deterministic release ZIP, SHA-256 checksum, channel-specific machine-readable manifest, release notes/changelog reference, and documented minimum client/API/schema compatibility fields. Manifest must distinguish Stable from Beta and include version, artifact URL/name, size, and hash. Treat manifests as informational until a signature and client verification policy are approved; do not claim SHA-256 alone authenticates a release.
- Use least-privilege workflow permissions. Keep release creation behind an explicit reviewable gate; no run in this task may publish a production release. If signing credentials or policy are absent, state the gap clearly and leave signing/updater activation disabled.
- Include a manual dry-run path that generates and validates ZIP/manifest/checksum without tagging or publishing. Explain release rollback/recovery for an extracted portable app, including profile migration limits.

## Validation and done

Run the local helper tests and a Windows dry run of manifest/ZIP creation. Validate checksum against ZIP bytes, channel/tag parsing, malformed version rejection, and package contents. Inspect workflow permissions and artifact paths. Report exact commands/results, changed files, baseline SHA, manifest format, signing gap, and remaining unverified GitHub release behavior. Stop before any production tag, release publication, deployment, or updater integration. Deliver a reviewable commit or PR; local agent will integrate it.
