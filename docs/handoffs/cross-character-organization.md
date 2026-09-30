# Cross-character organization handoff

Updated: 2026-09-29. Status: Phase 1 foundation implemented; Phase 2 read-only bank consolidation preview implemented. No exchange automation or network telemetry exists yet.

## Read first

- Design and ordered gates: `docs/superpowers/specs/2026-09-29-cross-character-organization-design.md`.
- Current boundaries: `docs/ARCHITECTURE.md` and `docs/GETTING_STARTED.md`.
- User instruction: preserve existing systems, show/approve plan before actions, verify recipient and final bank, keep telemetry and updates optional.
- Global instruction: never close, stop, kill, restart, or relaunch `Excalibur.exe` without user permission in current conversation.

## Current evidence

- Git working tree has pre-existing edits in `DemoCollection.cs`, `MainWindow.cs`, `GameSession.cs`, `SessionTests.cs` and untracked portrait files. Do not overwrite or include them in unrelated commits. Recheck `git status --short` before edits.
- `InventoryStore` now rejects future schema versions, backs up v1 profiles through SQLite backup (including WAL), and migrates to v2. A real v1-shaped fixture verifies preserved items and backup.
- `ReadOrganizationState()` now gathers saved snapshots, account relationships, roles, rules, item metadata, preferences, and relevant settings under one SQLite read transaction. A deterministic SHA-256 input fingerprint changes when pins or account assignments change. The current preview is read-only; no durable approved plan or per-step revision comparison exists yet.
- Game packet `IsStackable` now persists in nullable `Item.IsStackable`; older JSON records remain readable with unknown status. Bank snapshot is read from correlated NPC list. Current bank withdrawal selects by item name and skips ambiguous variants.
- Arbiter has exchange packet types; app has no exchange lifecycle tracker or human rendezvous coordinator.
- Two manual dual-session captures now validate single-item action 1 (no quantity byte) and partial-stack action 2 (slot plus one-byte quantity). Vendored `ClientExchangeMessage.Serialize` was fixed for action 1; captured payload tests cover readers/writers. Server event 2 has no typed quantity field but stack display text showed `(2)`, and sender inventory fell by exactly 2 before acceptance. Event 5 arrived before the recipient accepted; never treat one event 5 as finality. Server `0x0F` recipient stack add and exchange quantity prompt carried extra zero bytes; vendor types now preserve these as unknown trailing bytes. Source single-item `0x10` had three unresolved bytes after slot. No `0x37` appeared. See `docs/protocol-captures/`. `GameSession` still tracks mundanes, not human trade targets.
- User supplied `/dasb` exchange observations: client `0x4A` actions 0/1/2/5, server `0x42` events 0/1/2/3, partner IDs on both sides, one-byte stack quantity, and dual inventory/final-close confirmation. These are research leads, **not** DAOrganizer's verified packet contract. Referenced `/dasb` files were absent from this worktree; internet search did not locate those exact files. The design now contains a manual dual-client capture checklist. Require offer quantity proof before automated acceptance; vendored server event 2 lacks quantity.
- Local `transfer_evidence` is planned for operation-linked relevant packet payloads with bounded retention; raw traces must never enter community telemetry or routine exported diagnostics.
- Existing local categories still work. Exact-item overrides, canonical metadata and community recommendation fields layer through one category resolver. Game account IDs, coexistence policies, middleman flags, multiple storage roles, and item/category/all role rules now exist and have Account Manager controls.
- Existing CI packages Windows portable app. No release workflow or updater.
- User reports Water Dungeon Chest is not character-bound and stacks above five. [Vorlof treasure bag table](https://vorlof.com/daitems/treasurebags.htm) conflicts (`Stack 5`, `Bound`); do not seed canonical metadata from it. Treat four freed slots as a plausible plan estimate once five entries, exact quantities, capacity, and route are checked. Exact cap remains unknown.

## Implemented files and verification

- Core: `InventoryStore.cs`, `InventoryStore.Accounts.cs`, `InventoryStore.Intelligence.cs`, `InventoryStore.Organization.cs`, `AccountCatalog.cs`, `Contracts.cs`, `ItemCategoryResolver.cs`, `OrganizationPlanner.cs`.
- App: `MainWindow.Accounts.cs`, `MainWindow.Storage.cs`, `MainWindow.Organization.cs`, `MainWindow.Actions.cs`, and a sidebar entry in `MainWindow.cs`. `GameSession.cs` now preserves observed stackability.
- Tests: `InventoryStoreMigrationTests.cs`, `StorageAccountTests.cs`, `ItemIntelligenceTests.cs`, `OrganizationPlannerTests.cs`, `ManualTradeTraceTests.cs`, `ManualTradeAnalyzerTests.cs`, `ManualTradeReviewTests.cs`, `ExchangeCodecCaptureTests.cs`, plus focused cases in `InventoryTests.cs`, `SessionTests.cs`, and `DAOrganizer.UiChecks/Program.cs`. Full unit suite passed **221/221** after the manual capture analyzer, codec updates, and pinned-route protection. Headless UI checks passed, including rendered storage setup and organization plan at `artifacts/ui-checks/`. Self-contained Windows package is `artifacts/DAOrganizer-2026-09-29-capture/`. The previously packaged `artifacts/DAOrganizer/` is in use by a running organizer process and must not be treated as the refreshed build.
- Schema version: 2. v1 migration backup: `<inventory.db>.v1.backup`; no credentials are in this database.
- Planner: duplicate bank entries across characters plus matching inventory owners, user-selected holder, storage role precedence, category and existing item-family matches, potential versus verified bank-slot savings, direct/middleman/manual route classification, stale/unknown blockers, Junk exclusion, pinned inventory protection, and explicit trade-rejection review. Equal-priority role conflicts require a holder choice. The preview never sends packets. Water Dungeon Chest is tested as an observed stackable item with five bank owners and four **potential** free slots despite conflicting external metadata.
- Capture preflight: `ManualTradeTrace.cs`, `GameSession.cs`, `Organizer.TradeCapture.cs`, and `MainWindow.Organization.cs` offer bounded, opt-in local capture of relevant decrypted packet payloads and both inventory snapshots. `ManualTradeAnalyzer.cs` conservatively checks two-party partner IDs/names, one offer, quantity prompt, both accepts, source/recipient inventory packets and exact snapshot conservation. Verified manual transfers add one deduplicated **local** success observation; ambiguous outcomes add none. See `docs/MANUAL_TRADE_CAPTURE.md`. User supplied successful single-item and partial-stack captures; both passed the analyzer in a temporary local smoke test and are summarized without private character/account identifiers in `docs/protocol-captures/`. The temporary test was removed. Raw JSON remains outside the repo. Capture is read-only and does not touch `Excalibur.exe`.

## Next safe task

Finish Phase 2: add keep quantities, capacity tests, a durable plan record with approval/expiry, and fingerprint checks. Inventory pins now prevent a source route from being suggested as automatic, including when only one slot in a source group is pinned. Current preview includes inventory owners but bank savings count only bank entries; verified savings remain zero because capacity and trade flow have not been validated. For Phase 3, design the two-session exchange observer and journal against the two captures, add human target tracking and exact offer/quantity proof, and investigate final-close semantics. Do not wire exchange send until those defenses have tests and controlled live validation. Independently implement offline-first telemetry, central aggregation, and optional updates in later phases.

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
