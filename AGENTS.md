# Working in DAOrganizer

DAOrganizer is a Windows desktop companion for Dark Ages. It shows saved and live inventory, equipment, and bank contents across characters; supports reviewed slot sorting and item maintenance; and can update selected accounts through organizer-launched clients. A separate `--demo` mode uses fictional, in-memory data. Read [the current architecture map](docs/architecture/overview.md), [user setup and limits](docs/GETTING_STARTED.md), and [verification commands](docs/CI.md) before changing a subsystem.

## Stack and entry points

- .NET 10 SDK (`global.json`), C#, Avalonia 11, SQLite through `Microsoft.Data.Sqlite`, xUnit, and vendored Arbiter protocol/process/sprite projects. `DAOrganizer.slnx` contains app, Core, Game, tests, UI checks, login check, and vendor projects.
- `src/DAOrganizer.App/Program.cs` and `App.cs` start Avalonia. `Organizer` owns the store, sessions, operation cancellation, and demo guard. `MainWindow` partial files render UI and call the organizer.
- `src/DAOrganizer.Core` owns records, SQLite `InventoryStore`, `AccountCatalog`, categories, item rules, slot/organization plans, account queue policy, and WorldLogs route graph. Keep this layer free of UI and native-window code.
- `src/DAOrganizer.Game/GameSession*.cs` owns Arbiter proxy observation, live state, packet-confirmed actions, login, navigation, and maintenance. `ClientLauncher` validates and launches the supported game executable. `vendor/Arbiter` contains upstream code plus documented local protocol changes; edit there only when a protocol type truly requires it.
- `tests/DAOrganizer.Tests` covers Core, app logic, and synthetic packet replay. `tests/DAOrganizer.UiChecks` runs headless Avalonia interaction checks and demo gallery. `tests/DAOrganizer.LoginCheck` is a separate executable, not part of the routine CI test sequence.

## State, safety, and configuration

- Default profile: `%LOCALAPPDATA%\DAOrganizer\inventory.db`; `DAORGANIZER_DATA_DIR` selects another profile. `Organizer` creates the directory. Demo uses SQLite `:memory:` and blocks live operations and credential changes. `DAORGANIZER_GAME_DATA` optionally points demo sprite loading at a local game-data folder.
- `InventoryStore` owns SQLite schema and migrations (`PRAGMA user_version`, currently 4), snapshots, freshness, settings, account relationships, storage roles, item metadata, overrides, trade observations, saved exact organization plans, and the pre-send transfer journal. v1, v2, and v3 migrations create local backups. Do not bypass it with another database or overwrite saved snapshots on incomplete scans. Test migrations with disposable fixtures.
- Optional passwords live in Windows Credential Manager through `CredentialVault`, not SQLite. Client path and WorldLogs path are settings in the profile. No checked-in `.env` or required environment secrets. No game installation, account, WorldLogs, or external service is needed for demo, build, and synthetic tests.
- Live integration requires Windows, the exact supported `Darkages.exe` verified by `ClientLauncher`, and a game network connection. WorldLogs are user-provided route data for travel and transfers; a nearby-NPC bank read can work without them. Only organizer-launched clients are tracked. Do not commit profiles, credentials, logs, raw captures, game binaries/assets, or private character details. See `.gitignore` and `CONTRIBUTING.md`.
- Never close, stop, kill, restart, or relaunch `Excalibur.exe` unless the user explicitly permits that in the current conversation. Building and synthetic validation need no Excalibur process action.

## Run and validate (repository root, Windows PowerShell)

```powershell
dotnet restore DAOrganizer.slnx -m:1
dotnet build DAOrganizer.slnx -c Release --no-restore -m:1 -p:UsedAvaloniaProducts=
dotnet test tests/DAOrganizer.Tests -c Release --no-build -m:1
dotnet tests/DAOrganizer.UiChecks/bin/Release/net10.0/DAOrganizer.UiChecks.dll artifacts/ui-checks
dotnet tests/DAOrganizer.UiChecks/bin/Release/net10.0/DAOrganizer.UiChecks.dll artifacts/gallery --gallery
dotnet run --project src/DAOrganizer.App -c Release --no-build -- --demo
```

The `dotnet run` command opens a desktop window; use it only when interactive UI inspection helps. `tools/Package.ps1` makes a self-contained Windows x64 package. CI uses the same restore/build/unit/UI/gallery sequence in `.github/workflows/build.yml`. There is no separate lint or typecheck command: the .NET build compiles all solution projects with nullable reference checking. Use focused xUnit tests while iterating, then the relevant CI checks. Full live-game behavior cannot be proved by synthetic tests alone.

## Existing patterns to reuse

- For identity and collection grouping, use `ItemGroups.Key` (name, sprite, color), `AccountCatalog`, and `ItemCategoryResolver`; category inference never changes item identity. Avoid a second category/metadata registry.
- For saved state, extend `InventoryStore` partial files and `Contracts.cs` carefully. Use its locking, transactions, and migration path. For organization proposals, use `ReadOrganizationState()` and `OrganizationPlanner`; it currently produces read-only estimates, not executable transfers. Keep potential savings distinct from verified savings.
- For account operations, use `Organizer.RunOperation`, `AccountUpdateQueue`, `GameSession`, `MaintenancePlan`/`MaintenanceRunner`, and `Navigation`. Preserve cancellation, ownership of launched clients, server confirmation, and the one-attempt rule for destructive actions. Never infer success from sending a packet.
- For manual trades, use `ManualTradeTrace` and `ManualTradeAnalyzer` for opt-in, bounded local observation. Do not duplicate their evidence path or turn a read-only proposal into an automated exchange without explicit task scope and protocol proof.
- For UI, follow `Styles.axaml`, `MainWindow` partial files, `SlotButton`, and `ItemImages`. Preserve the game-aligned 12-column inventory, pin/drag behavior, scroll anchor, compact layout, demo isolation, black-and-gold palette, and readable empty/error states. See `docs/design/black-and-gold.md`.

## Task discipline for local and Cloud agents

Before editing: inspect the relevant implementation and its callers/tests; search for existing services, utilities, and components; trace UI -> organizer -> Core/Game -> persistence or proxy and back. Preserve behavior unless the task asks to change it. Extend reasonable existing abstractions rather than creating parallel ones.

While editing: follow `.editorconfig` and nearby C# conventions; keep one task's diff narrow; avoid speculative rewrites; preserve existing functions and backward compatibility where practical; handle errors, cancellation, stale data, and uncertain protocol results. Existing documentation and plans may describe future work, so confirm current code before treating a proposal as shipped.

Before done: run appropriate focused tests, then build and relevant CI checks; fix errors introduced by the change; inspect `git diff` and `git status` for unrelated files; report exact commands/results and what remains unverified. Do not include another developer's uncommitted work in your task. For protocol changes, add synthetic packet evidence and keep live validation distinct. For UI, run headless checks and inspect demo at normal/compact sizes. Review risky item actions against `docs/GETTING_STARTED.md` limits.

For large features, start with [the feature plan template](docs/plans/FEATURE_TEMPLATE.md). Divide work by real boundaries: Core item intelligence and planning; account/storage UI; Game session/protocol behavior; tests/UI checks. Shared contracts, schema/migrations, `Organizer`, and `MainWindow` integration are collision points. Agree their interface and sequencing first. Each Cloud task should have an owner, explicit inputs/outputs, reviewable diff, and checks it can run independently. If tasks share an unsettled data contract or migration, say they must run in sequence. Recheck worktree state before each handoff.
