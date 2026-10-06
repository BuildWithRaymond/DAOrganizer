# Releases and optional updates

DAOrganizer ships a self-contained Windows x64 installer and portable ZIP. Both support optional updates from stable releases in `BuildWithRaymond/DAOrganizer`. The app profile remains at `%LOCALAPPDATA%\DAOrganizer` (or `DAORGANIZER_DATA_DIR`); passwords remain in Windows Credential Manager. Installation uses the separate `BuildWithRaymond.DAOrganizer` application directory.

## User controls and trust

Automatic updates are off by default. Settings can enable startup checks, background downloads and installation on a safe exit. Manual check, download and install controls work without enabling automatic mode. Installation is blocked while an organizer operation or owned game client is open, including a client awaiting login. Turning automatic mode off keeps a pending download from being applied automatically. Demo skips updater bootstrap, networking and preference writes. Source builds and old 0.15 ZIPs need one manual upgrade to an update-capable package.

Velopack 1.2.161 reads the public stable GitHub feed over HTTPS and verifies downloaded packages against feed checksums. No GitHub token is shipped or required. Releases are **unsigned**: these checks detect corruption but do not independently authenticate a publisher if its GitHub account/feed is compromised. Authenticode signing is not configured. The old helper's `release-stable.json` is informational evidence, not the updater feed; its unsigned trust fields remain accurate.

## Build a candidate

Update the app project's single `<Version>`, `CHANGELOG.md`, and `docs/releases/vVERSION.md`. Tag and app version must match. Run documented build, unit/protocol, UI and demo checks, then:

```powershell
.\tools\Build-Release.ps1
.\tools\Verify-Release.ps1
dotnet run --project tools/DAOrganizer.Release -c Release -- self-test
```

The build script restores pinned local `vpk` tooling, publishes into a fresh ignored version-specific package directory, includes license notices and the user guide, creates Velopack assets and checksums, and validates the update feed and portable contents. It refuses to reuse an old package directory; for repeat packaging of the same verified folder use `-SkipPublish` with a fresh `-Output`. No personal profile or external WorldLogs folder is needed. The sanitized route bundle is embedded in Core.

Publish these files together from the checked candidate:

| Asset | Purpose |
| --- | --- |
| `DAOrganizer-win-Setup.exe` | Recommended install with Start menu shortcut |
| `DAOrganizer-win-Portable.zip` | Update-capable portable app; start root `DAOrganizer.exe` |
| `BuildWithRaymond.DAOrganizer-VERSION-full.nupkg` | Full update package; users do not install this manually |
| `releases.win.json` and `RELEASES` (when generated) | Velopack feed files; retain original package filenames |
| `SHA256SUMS.txt` | Candidate checksums for all other release assets |

Do not replace assets in an existing published version. Ship a new version for corrections.

## GitHub publication

The **Build release candidate** workflow builds and checks all projects, exercises the app with synthetic data, creates installer/portable/update assets, and uploads a reviewable candidate. Tag pushes validate only; publication requires explicit manual dispatch with `publish=true` and an existing matching tag. The publication job checks out that tag, rebuilds the candidate, validates downloaded assets again, then publishes the release. `release-production` environment protections apply if configured.

The deterministic archive helper remains a separate release-evidence check. It records supported profile schemas 1–5 and unsigned status. Its raw ZIP and informational manifests are uploaded as Actions evidence and are not user downloads or an alternate update feed.

## Recovery

Failed checks/downloads leave the installed build usable. A downloaded package waits until an allowed exit; an app crash does not authorize installation. After an update, reopen DAOrganizer normally. Automatic startup application is disabled so it cannot bypass saved preferences or game-client guards.

Before any manual rollback, close organizer clients and the organizer, preserve the current profile, and keep the newer installation. Profile schema migrations can prevent older builds from reading newer data. Use a migration backup only after preserving both current data and backup; restoring it loses later changes. If compatibility is uncertain, return to the newer build rather than forcing a downgrade. Retain published versions and issue a corrected stable release when needed.
