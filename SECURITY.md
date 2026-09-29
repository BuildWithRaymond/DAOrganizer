# Security policy

## Supported versions

Security fixes target the latest release and the `main` branch. Older builds may not receive backports.

## Report privately

Use [GitHub's private vulnerability reporting form](https://github.com/buildwithraymond/DAOrganizer/security/advisories/new). Include the affected version, a minimal reproduction, expected impact and any proposed fix.

Do not put credentials, account databases, login packets or exploitable vulnerability details in a public issue. If the private form is unavailable, open an issue asking for a private reporting channel without including those details.

The project is maintained on a best-effort basis; no response deadline is guaranteed. Please coordinate public disclosure with the maintainer while a report is being investigated.

## Relevant boundaries

DA Organizer runs on your computer and observes game traffic through a local proxy for clients it launches. Optional saved passwords use Windows Credential Manager. Local snapshots and configuration are stored in SQLite. Demo mode uses an isolated in-memory database and blocks live operations.

Credential disclosure, unintended network exposure, unsafe client launch/input and bypasses of item-operation safeguards are useful reports. Ordinary category mistakes, unsupported game versions and cosmetic problems belong in public bug reports after removing personal data.
