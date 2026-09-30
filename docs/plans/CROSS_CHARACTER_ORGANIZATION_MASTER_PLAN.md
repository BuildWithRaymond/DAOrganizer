# Cross-character organization: master execution plan

Status: **proposed handoff plan; implementation paused pending user approval**. Checked against the local worktree on 2026-09-29. This plan records completed work and allocates remaining work; it does not authorize automated trades, telemetry, deployment, or app updates.

## Goal and user-facing behavior

Analyze saved inventory and banks across all known characters; show duplicate groups, realistic bank-slot savings, preferred holders, exact proposed moves, direct or middleman routes, blockers, and an approval screen. Execute only approved, fresh, verifiable steps. Learn item tradeability locally, optionally share minimal anonymous corrections with a consensus service, and optionally install a verified stable/beta update. Existing sorting, bank scans, account updates, credential storage, and demo behavior must continue working.

Users should see an empty state when there are no opportunities, an explicit `Needs scan`/unknown state for stale data or uncertain stack limits, a clear `Manual only` route when movement cannot be safely planned, and an operation recovery screen after ambiguous actions. Potential savings and confirmed savings remain separate. No background action follows a recommendation without approval.

## Source of truth and present state

Read [architecture map](../architecture/overview.md), [current handoff](../handoffs/cross-character-organization.md), [capture notes](../protocol-captures/2026-09-29-manual-partial-stack.md), and [original design](../superpowers/specs/2026-09-29-cross-character-organization-design.md). **Source code wins where the older design speaks in future tense about work already done.**

| Area | Implemented now in local worktree | Partial or missing |
| --- | --- | --- |
| Profile and models | `InventoryStore` schema **v2**, v1 SQLite backup/migration, future-version rejection; nullable observed `Item.IsStackable`; item key remains `ItemGroups.Key` (sprite/color/name). `ReadOrganizationState()` reads snapshots/rules/accounts/knowledge in one transaction and hashes inputs. | No v2→v3 plan/journal migration, per-step preconditions, saved approvals or expiry. Fingerprint exists but is not enforced before effects. No reliable bank stack cap or capacity evidence. |
| Accounts and storage | `AccountCatalog`/`InventoryStore.Accounts.cs` store game-account membership, same-account and cross-account coexistence policies, middleman flag, multiple storage roles with exact/category/all rules and priorities. `MainWindow.Storage.cs` exposes setup. Unknown coexistence stays manual. | `minimum_to_keep` column exists but UI/planner do not use it. No verified middleman capacity/schedule or transfer execution. |
| Item preferences | Exact item category/destination/never-move overrides, canonical/community category fields, local trade observation counts, and resolver precedence exist. Existing Keep/Junk/AutoDeposit still work. | No confidence decay, explicit item-denial observer, opt-in sharing, outbox, synchronization, or central service. User override must remain highest priority. |
| Organization | `OrganizationPlanner` groups bank duplicates across characters, includes matching inventory owners, chooses holder using user override/roles, estimates **potential** bank slots, previews direct/middleman/manual routes, respects Junk/never-move and inventory pins. `MainWindow.Organization.cs` shows holder picker and blockers. Water Dungeon Chest test uses observed stackability and allows four *potential* saved slots across five bank owners. | Preview requires a bank entry and multiple owners; no inventory-only opportunity, exact approved source slots/quantities, same-character step graph, keep-quantity handling, destination capacity proof, or verified savings. No approval or execution UI. Water Dungeon Chest exact cap remains unknown; do not seed the conflicting bound/stack-five external claim. |
| Exchange evidence | Opt-in `ManualTradeTrace`/`ManualTradeAnalyzer` save bounded, local dual-session payloads/snapshots and record one deduplicated **local** success only when partner, offer, accepts and item conservation agree. Captures validated single nonstackable item and a 2-of-25 Queen's Chest trade. Vendored action-1 writer and trailing-byte round trips have capture-derived tests. | Capture filters may miss a separate final-close opcode; event 5 arrived before recipient accepted. Offer quantity is display text, not a typed field. Human target runtime identity, two-session coordinator, outbound exchange, recovery journal, and live validation are absent. Raw user traces remain outside repo and must never enter telemetry. |
| Release | Windows .NET 10/Avalonia portable self-contained package via `tools/Package.ps1`; `build.yml` restores, builds, tests, renders UI/demo and uploads a CI artifact. App version is 0.15.0; profile lives under `%LOCALAPPDATA%`, separate from package; credentials live in Windows Credential Manager. | No tag-triggered published release manifest, stable/beta feed, integrity signature, in-app checker, staged install, migration-aware rollback, or central API compatibility policy. `docs/CI.md` reports older 0.15.0 release counts and needs updating at integration. |

**Validation already performed:** last local full unit run passed **221/221**; headless UI checks passed; package was built in `artifacts/DAOrganizer-2026-09-29-capture/`. These checks were before this documentation-only plan, not a fresh run. Synthetic tests do **not** validate live trades, final-close handling, middleman custody, community abuse controls, updater recovery, or a v2→v3 migration.

**Worktree gate:** feature code, new `AGENTS.md`/architecture docs, and unrelated portrait work are all currently uncommitted or untracked. `git diff --stat` omits untracked files. A Cloud agent starting from remote `main` would miss much of this implementation. Preserve every valid edit; separate the portrait changes from feature changes where possible and establish a reviewed, committed/pushed baseline before any Cloud code task. Do not copy local captures, profiles, credentials, or generated packages into that baseline.

## Existing infrastructure and design shape

```text
MainWindow review -> Organizer coordination -> Core plan/store -> GameSession observation/actions
                                            \-> AccountCatalog + existing navigation/banking
opt-in local observation -> bounded outbox -> central intelligence API -> local cache
CI tag -> portable package + signed manifest -> optional staged local update
```

Core owns item identity, rules, pure decisions, schemas and journals. App owns approvals, session scheduling, cancellation, UI and update prompts. Game owns protocol observation/action and must use the existing Arbiter proxy and `GameSession` action gate. Extend `InventoryStore` partials, `OrganizationPlanner`, `AccountCatalog`, `Organizer`, `GameSession`, `MaintenancePlan`/guarded bank methods, and existing `MainWindow` partials. Do not create a second inventory database, category registry, character manager, bank driver, navigation stack, login path, or proxy.

For physical game actions, “transactional” means a **durable saga**, not a SQLite transaction around the server. Persist intent before an external send, persist observed evidence after it, retain the last verified holder/quantity, and stop on uncertainty. Sending or receiving an early acceptance message never proves delivery.

## Phase map and ownership

`LOCAL / INTEGRATION` stays with the primary local agent. `PARALLEL CLOUD-SAFE` tasks have disjoint files after stated contracts land. `SEQUENTIAL CLOUD` starts only after its prerequisite is merged and checked. No implementation phase below starts until the user approves this plan.

### Phase 0 — Preserve and publish the current baseline (`LOCAL / INTEGRATION`)

- **Goal/dependency:** make current work reviewable and visible to Cloud before branching; no earlier dependency.
- **Reuse/files:** inspect `git status` including untracked files; `InventoryStore*`, `OrganizationPlanner`, account/storage UI, `GameSession`, vendored codec changes, tests/docs, and separate portrait edits. Update the handoff and architecture note only where code differs.
- **Steps:** group feature versus unrelated portrait changes without discarding either; review secrets/raw capture exclusions; run `dotnet build DAOrganizer.slnx -c Release --no-restore -m:1 -p:UsedAvaloniaProducts=`, unit tests, UI checks/gallery, and `git diff --check`; commit/push a reviewable baseline or otherwise provide a branch/worktree accessible to Cloud. Preserve schema v2 and old-profile backup behavior.
- **Edge cases/tests/done:** no real profile in tests, no Excalibur process action, no accidental package/raw-capture commit. Done when Cloud branch contains all currently valid feature code and its tests, with unrelated edits clearly owned and a cleanly described remaining diff. Current 221-test result is evidence, not a substitute for this gate.

### Phase 1 — Freeze exact organization contracts and durable approval (`LOCAL / INTEGRATION`)

- **Goal/dependency:** after Phase 0, define immutable, exact proposed steps and approval state before the planner or executor grows.
- **Reuse/files:** `OrganizationPlanner.cs`, `Contracts.cs` or a new Core organization-contract file, `InventoryStore.Organization.cs`, `InventoryStore.cs` migration and new `InventoryStore.Plans.cs`; tests in `InventoryStoreMigrationTests.cs` and new plan-contract tests. `MainWindow.Organization.cs` consumes the contract later.
- **New data:** versioned v2→v3 migration with WAL-safe backup and rollback fixture; `organization_plans` and `plan_steps` for plan ID, creation/expiry, user-approved source/destination/item/slot/quantity, input fingerprint, ordered dependencies, and state. Define per-step before/after item fingerprints and an explicit `Unknown/NeedsScan/ManualOnly/Ready` readiness result. Keep operational `transfer_runs/evidence` for Phase 4 unless a tested journal contract needs them earlier.
- **Steps:** define stable `PlannedStep`/route-leg contract and source item identity, route/quantity invariants, approval/expiry rules, and expected state transition API. Make approval compare a fresh `ReadOrganizationState()` fingerprint. Document that execution replaces the initial global fingerprint with confirmed per-step state after its own changes; it must not accept stale external changes or invalidate itself merely because a completed step changed inventory. Freeze these interfaces before Cloud Phase 2.
- **Edge cases/tests/done:** concurrent rules/snapshot changes, reordered slots, malformed saved plans, new binary opening old profile, old binary opening newer schema, interrupted migration. Done when migrations preserve existing data, exact plans round-trip, stale approvals reject, and the shared contract is documented for Cloud handoff. No packets sent.

### Phase 2 — Complete pure cross-character planning (`PARALLEL CLOUD-SAFE`, Cloud Task A)

- **Goal/dependency:** after Phase 1 contract lands, turn current read-only opportunity list into exact, explainable candidate steps without game effects.
- **Reuse/files:** own `src/DAOrganizer.Core/OrganizationPlanner.cs` and `tests/DAOrganizer.Tests/OrganizationPlannerTests.cs`; consume frozen Core records and `ReadOrganizationState()` only. Do not edit migrations, `Contracts.cs`, `InventoryStore*`, App, or Game.
- **Steps/new logic:** include inventory-only duplicates; enumerate withdraw/bank/within-character/consolidate/direct/middleman/manual candidates with exact source slots and quantities; honor inventory pins, never-move, Junk, per-character keep quantities, chosen holder and role precedence. Account for existing destination stacks, observed stack cap, nonstackable instances, and bank/inventory room. Keep optimistic slot savings apart from verified savings; with unknown cap/capacity show upper bound and blocker, never a confirmed value. Do not treat a shared name with different sprite/color or durability-sensitive instance as an interchangeable stack.
- **Edge cases/tests/done:** five-character Water Dungeon Chest (user says transferable and stack above five; exact cap unknown), inventory-only group, stale bank, same-name variant, full destination, capped stacks, multiple source slots with one pin, keep remainder, role tie, conflicting trade evidence, unknown coexistence, overflow and large quantities. Done when each proposed step has deterministic inputs/reasons, no protected quantity is offered, arithmetic is tested, and Core remains side-effect free. Local agent merges and runs full checks before UI work.

### Phase 3 — Finish exchange observation contract (`PARALLEL CLOUD-SAFE`, Cloud Task B)

- **Goal/dependency:** after Phase 0 baseline, improve **read-only** protocol evidence independently of planner work. No outbound packet send.
- **Reuse/files:** own `ManualTradeTrace.cs`, `ManualTradeAnalyzer.cs`, `ManualTradeTraceTests.cs`, `ManualTradeAnalyzerTests.cs`, `ExchangeCodecCaptureTests.cs`, and protocol note under `docs/protocol-captures/`. Do not edit `ManualTradeReviewTests.cs`, `GameSession.cs`, `Organizer*`, `MainWindow*`, `InventoryStore*` or vendored codec without a separate local handoff.
- **Steps/new logic:** add a bounded direction/opcode/timing-only timeline for otherwise filtered packets while retaining full payload only for relevant exchange/inventory opcodes. Make the analyst distinguish acceptance from final delivery, reject malformed/extra offers or gold, and define a documented result contract for `Verified`, `Inconclusive`, and explicit item denial. Preserve backward reading of the two older local capture JSON shapes. Use synthetic/anonymized fixtures; raw user JSON stays local.
- **Edge cases/tests/done:** one client disconnects, capture truncates, duplicate/out-of-order events, reused runtime ID, early event 5, stack suffix mismatch, unknown close subtype, unrelated inventory/gold change. Done when parser/tests specify evidence required for a future coordinator and never misclassify an ambiguous failure as non-tradeable. The missing close signal may require **one later controlled manual capture** with the improved observer; ask for it only when needed. Local GameSession integration and any vendor edit follow review.

### Phase 4 — Plan review, journal and direct transfer (`LOCAL / INTEGRATION`)

- **Goal/dependency:** merge Phases 1–3 first. Add approval UI and operation journal, then direct transfers only after codec, quantity-offer, finality and human-target evidence gates pass.
- **Reuse/files:** `MainWindow.Organization.cs`, `Organizer.cs`/new `Organizer.Transfers.cs`, `InventoryStore.Plans.cs`, new `InventoryStore.Transfers.cs`, `GameSession.cs`/new `GameSession.Exchange.cs`, existing guarded bank actions, `Navigation.cs`, `ClientLauncher`, `CredentialVault`; vendor codec only if captured bytes demand it. New journal tables through a versioned migration if not in Phase 1.
- **Steps/new components:** show selectable exact steps, fresh scan/capacity requirements, holder and route review, expiry, operation progress and recovery. Journal `Proposed → Approved → Preparing → Offered → Accepting → RecipientVerified → Banking → Complete` plus `NeedsReconciliation/Failed`, with explicit current custody. Reserve one pending source and acquire both session action gates in stable order. Validate distinct known sessions, same map/adjacency, fresh target runtime IDs resolved to expected names, exact source slot/quantity, recipient room, and one offer on both sides. Use captured action 0/1/2/5 layouts; require proven quantity before acceptance. Withdraw via existing guarded bank path only for unambiguous item variants. Verify both inventories and unrelated items/gold after the trade; bank deposit and fresh scan complete the step only after final location/quantity matches.
- **Edge cases/tests/done:** packet timeout, cancel, disconnect, manual input/movement/damage, mismatched partner, extra item/gold, source stack falling at offer, 1..255 quantity split, full recipient, ambiguous name-only bank withdrawal, journal write crash before/after each send, failed deposit. Never resend an uncertain trade; cancel only matching live sessions; leave owned clients open for review. Test replay/state transitions and recovery on disposable DB. **Controlled low-value live validation is a separate gate** before general automation. Done when an approved direct transfer completes only on verified recipient/bank state and every ambiguous result has a visible reconciliation path.

### Phase 5 — Same-account middleman custody (`LOCAL / INTEGRATION`)

- **Goal/dependency:** after direct leg and recovery are proven, compose two verified legs; no separate trade implementation.
- **Reuse/files:** `AccountCatalog` coexistence/flags, `OrganizationPlanner` routes, `Organizer.Transfers.cs`, transfer journal, `GameSession.Login.cs`/`.Logout.cs`, existing account-update queue policy, Navigation/bank methods, recovery UI.
- **Steps/new logic:** require explicit trusted middleman on another usable account, `Yes` coexistence for each leg, capacity and rendezvous checks. Persist A→middleman→B route and both expected quantities before starting. Verify middleman receipt before source logout; verify source truly offline before destination login where accounts conflict; verify B receipt and final bank before completion. Schedule through existing owned sessions/launch rules; never close or relaunch Excalibur.
- **Edge cases/tests/done:** no coexistence route, wrong character logs in, failed logout, login unavailable, middleman full, interruption between legs, B bank unavailable, uncertain first or second leg. Journal must name middleman as last verified holder after leg 1; never auto-replay. Done after synthetic interruption tests plus controlled live two-leg validation and recovery review.

### Phase 6 — Local community contract and opt-in queue (`LOCAL / INTEGRATION`)

- **Goal/dependency:** after baseline; may proceed in parallel with transfer development **only with a distinct owner of `InventoryStore`/settings and frozen event/API contract**. Existing v2 evidence and category precedence stay.
- **Reuse/files:** `InventoryStore.Intelligence.cs`, new store partial/outbox migration, `ItemCategoryResolver`, `MainWindow.Items.cs`/Settings, `Organizer` settings; no full inventory upload.
- **Steps/new data:** define minimal event schema for explicit category X→Y, Junk add/remove, AutoDeposit add/remove, verified trade success and explicit item-specific denial. Include item key, event kind, old/new value where needed, app/protocol version, random installation-scoped reporter token and idempotency key. Never include character/account name, slot, credentials, snapshots, route or raw packet. Default telemetry off; clear enable/disable/delete controls and bounded local outbox. Extend local evidence with confidence/conflicts/age/version, but one rejection never overwrites canonical data. Add cache records with revision/ETag/expiry. Keep precedence: user override → trusted canonical → qualified community recommendation → existing heuristic/unknown.
- **Edge cases/tests/done:** offline queue, revoke opt-in, repeated toggles, duplicate capture, stale client, conflicting evidence, corrupted cache, future API version. Done when organization works offline, opt-out sends nothing, payload-schema tests prove excluded fields, and migration/backups pass. Freeze this wire contract before Cloud Phase 7.

### Phase 7 — Central item intelligence service (`SEQUENTIAL CLOUD`, Cloud Task D)

- **Goal/dependency:** after Phase 6 event contract and local decision on hosting/operations. New service lives separately; it does not own character inventories.
- **Reuse/files:** new isolated `services/item-intelligence/` with its own API/storage/tests/docs. Do not edit `src/DAOrganizer.*`, root schema, or current CI without local integration review.
- **Steps/new components:** implement versioned `POST /v1/observations` (bounded idempotent events), `GET /v1/items/changes?since=<revision>` (ETag/expiry/provenance/confidence), and `GET /v1/client-policy` (supported versions/defaults). TLS; scoped rotating/revocable installation credentials; per-install/IP/item rate and vote caps; event dedupe; client-version filtering; reporter-diversity thresholds, ageing and delayed promotion. Keep raw observations, community recommendations, and human-reviewed canonical metadata separate. Installation ID limits influence but cannot prove unique people. Publish only aggregate recommendations with evidence counts and freshness; no single report changes canonical metadata.
- **Edge cases/tests/done:** spam/replay, old client, conflicting votes, poisoning attempt, service outage, withdrawn recommendation, schema-compatible delta, privacy/log retention. Done with API contract tests, abuse-limit tests, deployment/rollback runbook, and a reviewable isolated diff. Deployment endpoint and signing/credential operations remain local decisions before production use.

### Phase 8 — Client sync and organization signals (`LOCAL / INTEGRATION`)

- **Goal/dependency:** merge Phase 7 service and Phase 6 local queue; no hard dependency for planning.
- **Reuse/files:** `Organizer`/new intelligence client, `InventoryStore.Intelligence.cs` and cache/outbox partial, `ItemCategoryResolver`, Settings and item-rule UI, Core planner consumption.
- **Steps:** submit only opt-in bounded outbox events; dedupe/retry with backoff; pull deltas using revision/ETag; validate provenance, expiry, compatible API/client version and item key before caching. Show recommendation and confidence separately from canonical or user override; let user decline it. Keep scans/organization working with last trusted cache when API is offline.
- **Edge cases/tests/done:** server down, malformed response, replayed revision, opt-out during in-flight request, stale recommendation, local override conflict. Done when offline tests, privacy payload tests and precedence tests pass; telemetry can be fully disabled without losing organizer function.

### Phase 9 — Tag release artifacts and update feed (`PARALLEL CLOUD-SAFE`, Cloud Task C)

- **Goal/dependency:** after Phase 0; independent of planner/protocol. Current `build.yml` uploads CI artifacts but does not publish a tagged release/update feed.
- **Reuse/files:** new `.github/workflows/release.yml`, new release-manifest/checksum helper under `tools/`, and release-specific docs/tests. Reuse `tools/Package.ps1` **without modifying it** in this task. Do not edit app/Core/Game or existing `build.yml`.
- **Steps:** define stable/beta SemVer tag convention and changelog input; on an approved tag, run current Windows restore/build/unit/UI/gallery checks, package portable ZIP, compute SHA-256, generate machine-readable channel manifest with artifact URL, release notes, minimum client/API/schema compatibility, then publish GitHub Release. Keep least-privilege workflow permissions; fail release on any verification failure. Integrity design requires a signed manifest with a pinned verification key before auto-install is enabled; signing secret/key management is a local gate, not a value checked into Git.
- **Edge cases/tests/done:** tag/version mismatch, beta/stable channel mix-up, duplicate tag, missing artifact, bad hash, unsigned manifest. Done when dry-run artifacts/manifest are reproducible and release workflow is reviewable; actual publish is gated by user release approval and signing setup.

### Phase 10 — Optional staged application updater (`LOCAL / INTEGRATION`; parser helper may be `SEQUENTIAL CLOUD`)

- **Goal/dependency:** after Phase 9 manifest contract and signing decision. Current app is an extracted, self-contained Windows folder, not an installer.
- **Reuse/files:** `Organizer`/Settings or new update UI partial, new isolated update checker/stager, `tools/Package.ps1` only if integration proves necessary, profile migration tests and release docs. A Cloud agent may implement only a pure signed-manifest parser/downloader after the format is frozen; local agent owns process lifecycle, UI and migration integration.
- **Steps:** default check optional; Stable/Beta selection; compare SemVer and API/schema policy; show version/release notes; user clicks Update; download to a separate version directory; verify pinned signature and artifact hash before unpacking; reject traversal/corrupt ZIP; preserve `%LOCALAPPDATA%` profile and Windows credentials. Because loaded DLLs cannot be replaced reliably, stage side by side and activate on a deliberate app exit/restart, never by killing game clients or Excalibur. Keep previous binary and a profile backup; migrations use forward version checks. If new schema is incompatible with old binary, rollback requires a safe pre-migration profile restore **only when no newer writes must be retained**, otherwise stop for review rather than silently downgrade.
- **Edge cases/tests/done:** interrupted download/extract, disk full, invalid signature, incompatible API, blocked DLL, active operation/proxied clients, failed first launch, failed migration, beta→stable switch. Done when updater tests cover staging/verification/recovery, user can decline update, current app keeps working offline, and a clean Windows install/update/rollback rehearsal passes without touching Excalibur.

## Concrete Cloud handoffs and merge batches

| Task | Label and dependencies | Owned files / deliverable | Must not modify | Local readback gate |
| --- | --- | --- | --- | --- |
| **A — Exact planner arithmetic** | `PARALLEL CLOUD-SAFE`; Phase 0 + frozen Phase 1 records | `OrganizationPlanner.cs`, `OrganizationPlannerTests.cs`; deterministic exact steps, blockers and capacity tests from Phase 2 | `InventoryStore*`, schema, `Contracts.cs`, App, Game, UI | Merge alone; run full Core/unit and UI preview checks; local agent owns any UI adaptation. |
| **B — Exchange evidence observer** | `PARALLEL CLOUD-SAFE`; Phase 0 | `ManualTradeTrace.cs`, `ManualTradeAnalyzer.cs`, matching focused tests, sanitized protocol notes; Phase 3 read-only result | outbound sends, `GameSession.cs`, vendor, Organizer, profile/schema | Merge alone; replay both sanitized capture-derived fixtures; local agent decides GameSession/vendor changes and live capture gate. |
| **C — Release workflow** | `PARALLEL CLOUD-SAFE`; Phase 0 | `.github/workflows/release.yml`, new helper in `tools/`, release manifest/docs; Phase 9 CI artifact | `build.yml`, `Package.ps1`, app/Core/Game, user profile | Merge alone; inspect workflow permissions, run manifest/ZIP checksum dry run on Windows; no production publish without user release decision. |
| **D — Consensus API** | `SEQUENTIAL CLOUD`; Phase 6 wire contract + hosting decision | isolated `services/item-intelligence/`, API/aggregation/abuse tests and deploy/runbook | desktop source, local DB migrations, app UI, credentials | Review schema and privacy contract, deploy to test service, run client contract/offline checks before Phase 8. |
| **E — Signed manifest parser/stager core (optional)** | `SEQUENTIAL CLOUD`; Phase 9 signed format fixed | isolated update helper and tests only, file names agreed locally first | `Program.cs`, `Organizer.cs`, MainWindow, profile migrations, CI workflows | Local agent integrates UI/lifecycle; malformed ZIP/signature and recovery tests before enabling Update. |

**Parallel Batch A:** A + B + C may run concurrently after Phase 0 and the Phase 1 planner contract freeze. Their expected files do not overlap: Core planner/tests, Game evidence/tests, and release workflow/tools. Task C can start immediately after Phase 0 while local Phase 1 runs. Never ask A to alter schema or B to send exchange packets to make its branch compile. Merge one task at a time with full checks and resolve interface drift locally.

**Parallel Batch B:** after Phase 6 contract and Phase 9 manifest are merged, D and optional E can run concurrently in separate service/update-helper paths. Neither may edit `InventoryStore`, `Organizer`, `MainWindow`, or shared workflows. Local integration of their results remains sequential because it touches app settings, profile migrations and release policy.

No Cloud task should branch from remote `main` until Phase 0 is available to it. Recheck worktree status and ownership before each handoff; avoid concurrent changes to a shared test file even when production files differ.

## Conflict zones and ownership rule

| Collision point | Why conflict is likely | Owner/sequence |
| --- | --- | --- |
| `Contracts.cs`, `OrganizationPlanner.cs`, plan/route records | Exact-step fields and saved-plan JSON are shared by planner, UI and executor | Local freezes record/API in Phase 1; Cloud A owns planner implementation until merged; local integrates afterward. |
| `InventoryStore.cs`, `InventoryStore.*.cs`, SQLite migrations/settings | One schema version and WAL backup path; plan journal, telemetry and cache all need it | Local agent alone owns migrations. Sequence v3 plan/approval, then later journal/outbox versions; test upgrade from every supported version. |
| `AccountCatalog.cs`, `MainWindow.Storage.cs` | Account links, roles, keep quantities and middleman trust feed planner and scheduler | Local owner; no simultaneous account UI and scheduler edits. |
| `GameSession.cs`, action gate, vendor Arbiter codecs | Packet ordering and single live-state owner; capture result and executor share observations | Cloud B stays in pure trace/analyzer files. Local owns GameSession, vendor changes and outbound exchange after B merges. |
| `Organizer.cs`/partials, `MainWindow.cs`/partials | Operation cancellation, progress, approval, recovery, settings and updater all meet here | Local integration owner; sequence transfer UI, telemetry settings and updater UI. Cloud tasks do not edit shared window/composition files. |
| `.github/workflows/build.yml`, `tools/Package.ps1`, release manifest | Build and release must agree about version/artifact layout | Cloud C owns new release files; local owns existing build/package changes and final publish. |
| `docs/architecture`, `docs/handoffs`, this plan | Cloud branches can overwrite current-state notes | Local updates master plan/handoff after each merge; Cloud edits only task-owned protocol/release/service docs. |

Uncommitted portrait work overlaps `MainWindow.cs` and `GameSession.cs`. Preserve it and integrate or isolate it **before** a Cloud branch touches those files. Nothing in this plan authorizes reverting it.

## Critical path and gates

```text
0 baseline visible to Cloud
  ├→ 1 exact plan/approval contract + migration → 2 exact planner (Cloud A) ─┐
  ├→ 3 protocol evidence (Cloud B) [quantity/finality/target ID gates] ─────┤
  │                                                                         ↓
  │                                                     4 direct verified transfer + live check
  │                                                                         ↓
  │                                                     5 middleman custody + live check
  ├→ 6 local opt-in/event/cache contract → 7 consensus API (Cloud D) → 8 offline client sync
  └→ 9 release workflow (Cloud C) → signed-format decision → 10 staged updater
```

Do not unlock direct-transfer sends from codec unit tests or the two existing manual captures alone. Event 5 can precede final acceptance; the capture filter may omit final close. Unknown offer quantity, ambiguous bank variant, or uncertain custody makes the plan manual/reconciliation-only. Middleman is not an exception to direct-leg verification.

## Verification and handoff discipline

- **Focused checks:** planner arithmetic/pins/caps/roles; v2→v3 migration and rollback fixture; dual-session packet order and inventory conservation; journal crash/recovery at each send; same-account routing; anonymous payload allowlist and abuse caps; release signature/hash and staged rollback.
- **Repository gates:** `dotnet restore DAOrganizer.slnx -m:1` when needed; `dotnet build DAOrganizer.slnx -c Release --no-restore -m:1 -p:UsedAvaloniaProducts=`; `dotnet test tests/DAOrganizer.Tests -c Release --no-build -m:1`; headless UI checks and gallery commands in [`AGENTS.md`](../../AGENTS.md); `git diff --check`. Run focused checks while editing and full affected checks after each merge. The login-check executable is separate from routine CI.
- **Live gates:** use only organizer-owned supported clients and low-value items, with user coordination at the specified milestone. Record sanitized observations and exact result. Never treat synthetic tests as proof of live item safety; never stop/restart/relaunch Excalibur without explicit current-conversation permission.
- **Handoff format:** each Cloud diff states baseline commit, owned files, inputs/assumptions, exact tests/results, unverified live behavior, migration/API compatibility, and remaining risks. Local agent reviews the diff before merging, refreshes this plan/handoff, and opens the next batch only after contracts pass.

## Recommended Next Move

1. **Local agent first:** preserve and commit/push the current feature baseline while isolating portrait edits; run the repository checks. Then freeze exact plan-step/approval contract and v2→v3 migration in Phase 1. No game packets or telemetry while doing this.
2. **First Cloud launch:** Task C, tag release workflow, can start as soon as Phase 0 is available while local Phase 1 is being frozen. **First critical-path Cloud task:** A, exact planner arithmetic, starts after Phase 1; Task B (read-only exchange evidence) can start beside A. C can also wait and join that same parallel batch.
3. **Must wait:** direct exchange waits for planner/observer merge, durable journal, partner/quantity/finality proof and controlled live validation. Middleman waits for verified direct legs. Central service waits for local anonymous event contract and hosting decision; client sync waits for service contract. Updater waits for signed release format and lifecycle design.
4. **Bring work back:** merge C when its release dry run passes; merge A and B **one at a time** after Phase 1, running full build/unit/UI checks after each relevant integration. Then local agent completes approval UI/journal and direct-transfer gates. Start Batch B (D and optional E) only when event and signed-manifest contracts are frozen. Merge each Batch B result into the local branch and repeat privacy, migration, offline and release-recovery checks before enabling either feature.

Implementation remains paused here until the user approves this execution map.
