# Optional GitHub updates and launch

## Release scope

Publish DAOrganizer 0.16.0 to the existing public `BuildWithRaymond/DAOrganizer` repository after verifying the current app and the release changes. Include the existing uncommitted consolidation feature only with the owner's confirmation and passing checks.

## Design proposed for review

Use Velopack for both a self-contained Windows x64 installer and an update-capable portable ZIP. Publish its update feed and full package beside the downloads on GitHub Releases. Keep the existing deterministic archive/checksum helper for release evidence where useful.

Settings offers an optional automatic-update checkbox, disabled initially, plus manual check/download/apply controls. Automatic mode checks stable GitHub releases on startup and downloads newer packages. Applying an update waits for a normal organizer exit with no active operation or tracked live connection. Manual restart also refuses while those conditions hold. Disable Velopack's unconditional startup apply, so a previously downloaded update cannot bypass these rules or an opt-out. Demo performs no updater network requests or writes. Offline errors leave the current build usable. Updates retain the external SQLite profile and Credential Manager entries. Releases remain unsigned; HTTPS and package checksums protect transport/integrity but are not independent publisher authentication.

Bundle route-only WorldLogs data derived from the existing local route collection. Retain only map identifiers, names, sizes, portals and world-map links consumed by `WorldGraph`; exclude accounts, observations, NPC records, raw captures and game assets. Include these routes in build and publish output. Use them by default, with an optional custom folder override for advanced users. Ignore a missing legacy folder and fall back to bundled routes. Explain observed-route limits and record dataset provenance/counts.

Find and inspect the existing ImageGen DAOrganizer launch artwork. Use it in the README, matching the sibling projects' presentation. Keep actual app screenshots distinct from promotional artwork. Polish download/setup/update instructions, version badges, release notes and contributor/release docs. Remove proven redundant local archives and stop shipping internal plans, handoffs, protocol research and development screenshots in user packages; preserve useful source documentation and unrelated work.

## Alternatives

- Recommended: Velopack manages installation and update mechanics; adds one maintained runtime dependency and release tooling.
- Manual downloads: least complexity, but does not satisfy automatic updating.
- Custom portable updater: preserves the old archive layout, but adds substantial file replacement, rollback and process-lifecycle code to maintain.

## Verification and publication

Write behavioral tests before updater/settings/fallback implementation. Cover opt-out, demo isolation, stale/custom route paths, bundled route availability, background errors and refusing unsafe apply. Run Release build, focused/full unit tests, headless UI checks, normal/compact gallery, release-helper checks and package content inspection. Exercise a disposable packaged installation update without game clients or real profiles. Inspect the final diff, build a clean release candidate, then commit/push only approved release scope, tag and publish matching checked assets. Never operate Excalibur.exe.

## Approval and validation

Owner approved this design and inclusion of the existing consolidation scope on October 5, 2026. Implementation is complete. Local validation: Release solution build with zero warnings/errors; 334 unit/protocol tests; headless UI including Settings and consolidation; seven normal/compact demo renders with installed sprites; release-helper self-tests; x64 installer/portable/feed checks and corrupt-checksum rejection; native update of a disposable portable copy to a non-published test version with changed content. Live game actions were not revalidated for this release.
