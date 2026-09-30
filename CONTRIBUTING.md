# Contributing

Thanks for helping improve DAOrganizer. Small, focused changes are easiest to review.

## Set up

Use Windows and the .NET 10 SDK. Follow the build commands in [README.md](README.md), then run with `--demo`. The fictional collection works without a game installation or saved accounts.

Read [Architecture](docs/ARCHITECTURE.md) for project boundaries and [the design notes](docs/design/black-and-gold.md) for UI conventions.

## Make a change

1. Open an issue for a substantial feature or protocol change so its behavior can be discussed.
2. Create a branch and keep the change focused on one problem.
3. Build the solution and run the relevant checks from the README.
4. Explain the previous behavior, the new behavior and how you verified it in your pull request.

For protocol or item-operation changes, include a synthetic regression case. Preserve server confirmation, operation cancellation and the one-attempt rule for destructive actions. Never record real login packets or credentials in fixtures.

For UI changes, run the headless interaction checks and gallery. Inspect normal and compact window sizes, keyboard focus, empty results and the ownership drawer. Use demo data for screenshots. Keep the inventory's 12-column geometry and flexible quick-slot ordering and reversible manual pins.

Category corrections should include an item name, the proposed category/family and a public reference when available. A small factual mapping is sufficient; do not copy another site's artwork or bulk content.

## Files and privacy

- Do not commit account databases, passwords, packet captures, WorldLogs, game executables, game sprites or build output.
- Do not include personal account names or file paths in screenshots or issues.
- Keep upstream licenses and attribution when changing vendored code.
- Keep documentation and release notes current when visible behavior changes.

Contributions are provided under this repository's MIT license. You must have the right to contribute the material you submit.

## Reporting problems

Use the issue templates for reproducible bugs and feature requests. Include the app version and whether the problem occurs in demo mode. Describe live-game observations separately from automated test results.

Report vulnerabilities privately as described in [SECURITY.md](SECURITY.md). Follow the [Code of conduct](CODE_OF_CONDUCT.md) in project discussions.
