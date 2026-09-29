# Verification and CI setup

The 0.14.0 release passed a local Release build with zero warnings/errors, all 153 unit/protocol tests, the Avalonia interaction checks and the demo-isolation screenshot gallery.

The tested Windows workflow definition is prepared in [`tools/ci/build.yml`](../tools/ci/build.yml). GitHub automation is **pending activation** because the publishing credential did not have permission to write workflow files. A successful hosted CI run has not yet been verified.

To enable it with a credential that has workflow access, move the definition to `.github/workflows/build.yml` and push to `main`. No repository secrets are needed. The workflow uses a Windows runner, builds and tests .NET 10, renders the demo gallery, and uploads the portable app and verification evidence.

See the [README](../README.md#build-from-source) for the same checks as local commands. The automated tests use synthetic data; they do not operate live game characters.
