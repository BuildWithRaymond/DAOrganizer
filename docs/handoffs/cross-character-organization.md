# Cross-character organization handoff

Updated: 2026-09-29. Status: Phase 0/1 complete; Cloud A/B/C accepted after correction and full validation. Phase 4 not started. No exchange automation, release signing, updater, or network telemetry exists.

## Cloud A/B/C integration readback

- **A:** `BuildExact` bounds chunks to 255 or smaller known stack caps, limits each plan to 256 steps, uses overflow-safe quantity sums, and limits additions to known room in existing destination stacks. Bank/inventory free-slot counts are absent from frozen `OrganizationState`; all affected steps remain `NeedsScan` or `ManualOnly`. Unknown exact caps yield only potential savings. No approval-ready transfer is produced.
- **B:** manual-capture analysis never returns `Verified` because event `0x42/5`, both accepts, and snapshot conservation do not prove successful exchange-window close. `Organizer.SaveManualTradeCapture` records no local success for inconclusive analysis. Explicit item-specific denial remains distinct from generic failure. Controlled manual capture still needed: both sessions' payload-free opcode timelines through second accept, recipient inventory gain, and window close. Inspect a candidate close payload only after timeline review.
- **C:** tag push builds/validates candidate without publication. Manual dispatch defaults to `publish=false`; `publish=true` with an existing matching tag rebuilds and verifies candidate in the same run before `gh release create`. Environment reviewers are an additional gate. Manifest remains unsigned and informational. No production release was created.
- **Phase 4 reassessment:** `READY WITH GATES` for plan-review UI, journal, orchestration scaffolding, and non-sending integration only. Outbound exchange stays disabled until finality/offer quantity/target identity evidence, recipient room, and controlled low-value live validation are established. User approval required before Phase 4 begins.
- **Final validation:** Release solution build 0 warnings/errors; full unit suite **262/262**; headless UI checks and demo gallery passed; helper self-tests and final package/ZIP/checksum/manifest verification passed; `git diff --check` passed. Release dry run only, no tag push or GitHub publication. Schema remains **3**.

## Read first

- Design and ordered gates: `docs/superpowers/specs/2026-09-29-cross-character-organization-design.md`.
- Current boundaries: `docs/ARCHITECTURE.md` and `docs/GETTING_STARTED.md`.
- User instruction: preserve existing systems, show/approve plan before actions, verify recipient and final bank, keep telemetry and updates optional.
- Global instruction: never close, stop, kill, restart, or relaunch `Excalibur.exe` without user permission in current conversation.

## Current evidence

- Phase 0 feature baseline `e1dd6765ac42f5afad1395d05b523b22bd7662a7` and final Phase 1 contract baseline `23bee187c1f82110c84f2cf376fadafd3f37de02` are pushed on `feature/cross-character-organization`. Pre-existing portrait edits in `DemoCollection.cs`, `MainWindow.cs`, `GameSession.cs`, `SessionTests.cs` and three untracked portrait files remain outside them. Do not overwrite or include them in organization commits. Recheck `git status --short` before edits.
- `InventoryStore` rejects future schema versions and migrates through v3. v1 and v2 profiles get SQLite backups (including WAL state) before migration. Disposable fixtures verify v2 data/account preservation and failed v2 migration rollback.
- `ReadOrganizationState()` gathers saved snapshots, account relationships, roles, rules, item metadata, preferences, and relevant settings under one SQLite read transaction. A deterministic SHA-256 input fingerprint changes when pins or account assignments change. `OrganizationContracts.cs` and `InventoryStore.Plans.cs` now save exact steps, approve only fresh/ready plans and advance the checkpoint after confirmed state changes. Existing preview is still read-only and has no approval UI or executor. See [Phase 1 contract](phase1-organization-contract.md).
- Game packet `IsStackable` now persists in nullable `Item.IsStackable`; older JSON records remain readable with unknown status. Bank snapshot is read from correlated NPC list. Current bank withdrawal selects by item name and skips ambiguous variants.
- Arbiter has exchange packet types; app has no exchange lifecycle tracker or human rendezvous coordinator.
- Two manual dual-session captures now validate single-item action 1 (no quantity byte) and partial-stack action 2 (slot plus one-byte quantity). Vendored `ClientExchangeMessage.Serialize` was fixed for action 1; captured payload tests cover readers/writers. Server event 2 has no typed quantity field but stack display text showed `(2)`, and sender inventory fell by exactly 2 before acceptance. Event 5 arrived before the recipient accepted; never treat one event 5 as finality. Server `0x0F` recipient stack add and exchange quantity prompt carried extra zero bytes; vendor types now preserve these as unknown trailing bytes. Source single-item `0x10` had three unresolved bytes after slot. No `0x37` appeared. See `docs/protocol-captures/`. `GameSession` still tracks mundanes, not human trade targets.
- User supplied `/dasb` exchange observations: client `0x4A` actions 0/1/2/5, server `0x42` events 0/1/2/3, partner IDs on both sides, one-byte stack quantity, and dual inventory/final-close confirmation. These are research leads, **not** DAOrganizer's verified packet contract. Referenced `/dasb` files were absent from this worktree; internet search did not locate those exact files. The design now contains a manual dual-client capture checklist. Require offer quantity proof before automated acceptance; vendored server event 2 lacks quantity.
- Local `transfer_evidence` is planned for operation-linked relevant packet payloads with bounded retention; raw traces must never enter community telemetry or routine exported diagnostics.
- Existing local categories still work. Exact-item overrides, canonical metadata and community recommendation fields layer through one category resolver. Game account IDs, coexistence policies, middleman flags, multiple storage roles, and item/category/all role rules now exist and have Account Manager controls.
- Existing CI packages Windows portable app; candidate release workflow exists. No updater or signed manifest.
- User reports Water Dungeon Chest is not character-bound and stacks above five. [Vorlof treasure bag table](https://vorlof.com/daitems/treasurebags.htm) conflicts (`Stack 5`, `Bound`); do not seed canonical metadata from it. Treat four freed slots as a plausible plan estimate once five entries, exact quantities, capacity, and route are checked. Exact cap remains unknown.

## Implemented files and verification

- Core: `InventoryStore.cs`, `InventoryStore.Accounts.cs`, `InventoryStore.Intelligence.cs`, `InventoryStore.Organization.cs`, `InventoryStore.Plans.cs`, `OrganizationContracts.cs`, `AccountCatalog.cs`, `Contracts.cs`, `ItemCategoryResolver.cs`, `OrganizationPlanner.cs`.
- App: `MainWindow.Accounts.cs`, `MainWindow.Storage.cs`, `MainWindow.Organization.cs`, `MainWindow.Actions.cs`, and a sidebar entry in `MainWindow.cs`. `GameSession.cs` now preserves observed stackability.
- Tests: `InventoryStoreMigrationTests.cs`, `StorageAccountTests.cs`, `ItemIntelligenceTests.cs`, `OrganizationPlannerTests.cs`, `ManualTradeTraceTests.cs`, `ManualTradeAnalyzerTests.cs`, `ManualTradeReviewTests.cs`, `ExchangeCodecCaptureTests.cs`, plus focused cases in `InventoryTests.cs`, `SessionTests.cs`, and `DAOrganizer.UiChecks/Program.cs`. Full unit suite passed **221/221** after the manual capture analyzer, codec updates, and pinned-route protection. Headless UI checks passed, including rendered storage setup and organization plan at `artifacts/ui-checks/`. Self-contained Windows package is `artifacts/DAOrganizer-2026-09-29-capture/`. The previously packaged `artifacts/DAOrganizer/` is in use by a running organizer process and must not be treated as the refreshed build.
- Schema version: 3. Migration backup: `<inventory.db>.v1.backup` or `<inventory.db>.v2.backup` according to source version; no credentials are in this database. `organization_plans` and `plan_steps` hold draft/approved exact plans; no transfer journal yet.
- Planner: duplicate bank entries across characters plus matching inventory owners, user-selected holder, storage role precedence, category and existing item-family matches, potential versus verified bank-slot savings, direct/middleman/manual route classification, stale/unknown blockers, Junk exclusion, pinned inventory protection, and explicit trade-rejection review. Equal-priority role conflicts require a holder choice. The preview never sends packets. Water Dungeon Chest is tested as an observed stackable item with five bank owners and four **potential** free slots despite conflicting external metadata.
- Capture preflight: `ManualTradeTrace.cs`, `GameSession.cs`, `Organizer.TradeCapture.cs`, and `MainWindow.Organization.cs` offer bounded, opt-in local capture of relevant decrypted packet payloads and both inventory snapshots. `ManualTradeAnalyzer.cs` checks partner IDs/names, offer, quantity prompt, both accepts, inventory packets and snapshot conservation, while withholding `Verified` because final close is unknown. Current manual captures cannot create local tradeability success. See `docs/MANUAL_TRADE_CAPTURE.md` and sanitized `docs/protocol-captures/`; raw JSON stays outside repo. Capture is read-only and does not touch `Excalibur.exe`.

## Next safe task

Stop for user review of `READY WITH GATES` reassessment. If approved, Phase 4 may start with plan-review UI, journal, and non-sending orchestration scaffolding. Obtain the controlled manual dual-session close capture before defining finality or enabling any outbound exchange. Verify recipient capacity and target identity before controlled low-value live transfer tests. Inventory pins and keep quantities remain protected; verified savings remain zero without execution evidence.

## Required handoff after each phase

Update this file with: completed files, exact migration version, tests and results, known limits, new game evidence and source URLs, unresolved safety conditions, and next smallest phase gate. Keep design document updated when an approved decision changes. Use synthetic packets for protocol tests; record live validation separately. Never mark a transfer complete from a sent packet alone.

## Design self-review

- Coverage table maps all 15 user questions to plan sections. Placeholder scan found no `TODO` or `TBD`.
- Corrected conflicting Water Dungeon Chest source and kept exact stack cap unknown.
- Account coexistence now covers both same-account policy and explicit cross-account pairs; unknown is manual-only.
- Migrations, plan fingerprints, conservation checks, privacy, API fallback, and updater rollback limits have explicit gates.
- User approved implementation. Manual game-account grouping and pairwise coexistence are now editable; unknown pairs remain manual-only.

## Review checkpoints

1. User reviews architecture and manual account-link assumption.
2. Phase 1 migration and UI behavior reviewed with old profile fixture.
3. Phase 2 read-only estimates reviewed, especially stale items, uncertain caps, and conflicting external data.
4. Phase 3 controlled live trade validation reviewed before general automation.
5. Each later phase receives its own release and rollback check.
