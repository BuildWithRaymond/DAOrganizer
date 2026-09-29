# Verification and CI

The 0.15.0 release passed a local Release build with zero warnings/errors, all 161 unit/protocol tests, the Avalonia interaction checks and the demo-isolation screenshot gallery.

The [Windows workflow](../.github/workflows/build.yml) runs on pushes to `main`, pull requests and manual dispatch. Current results are available in [GitHub Actions](https://github.com/buildwithraymond/DAOrganizer/actions/workflows/build.yml).

No repository secrets are needed. The workflow uses a Windows runner, builds and tests .NET 10, renders the demo gallery, and uploads the portable app and verification evidence. Test artifacts contain only synthetic screenshots and test reports; account databases are excluded.

See the [README](../README.md#build-from-source) for the same checks as local commands. The automated tests use synthetic data; they do not operate live game characters.
