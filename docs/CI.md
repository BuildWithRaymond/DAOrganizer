# Verification and CI

Each revision is checked with a Release build, unit/protocol tests, headless Avalonia interactions, demo isolation and normal/compact screenshots. The release pipeline also tests packaging, checksums and the GitHub update feed. Current test counts are reported by the run; do not use older release counts as current coverage.

The [Windows workflow](../.github/workflows/build.yml) runs on pushes to `main`, pull requests and manual dispatch. Current results are available in [GitHub Actions](https://github.com/buildwithraymond/DAOrganizer/actions/workflows/build.yml).

No repository secrets are needed. The workflow uses a Windows runner, builds and tests .NET 10, renders the demo gallery, and uploads the installer, update-capable portable ZIP, updater assets and verification evidence. Test artifacts contain only synthetic screenshots and test reports; account databases are excluded.

See the [README](../README.md#build-from-source) for the same checks as local commands. The automated tests use synthetic data; they do not operate live game characters.
