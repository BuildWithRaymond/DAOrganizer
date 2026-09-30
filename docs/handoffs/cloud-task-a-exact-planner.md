# Codex Cloud Task A — Exact planner arithmetic

Branch from `feature/cross-character-organization` at exact Phase 1 baseline `23bee187c1f82110c84f2cf376fadafd3f37de02`. Read `docs/plans/CROSS_CHARACTER_ORGANIZATION_MASTER_PLAN.md` Phase 2 and `docs/handoffs/phase1-organization-contract.md`. Implement only pure planning; this task can run simultaneously with Cloud Task B.

## Ownership

Own only `src/DAOrganizer.Core/OrganizationPlanner.cs` and `tests/DAOrganizer.Tests/OrganizationPlannerTests.cs`. No schema/migration edits. No `InventoryStore*`, `Contracts.cs`, `OrganizationContracts.cs`, App, Game, vendor, UI, telemetry, API, updater, or packet changes. If a frozen contract cannot express a needed safe candidate, report it for local integration; do not edit shared files.

## Contract and behavior

- Consume the existing `OrganizationState` supplied by `InventoryStore.ReadOrganizationState()`, including its fingerprint, snapshots, account/coexistence relations, storage roles/rules, overrides, trade evidence and settings. Keep existing `OrganizationPlanner.Build` preview behavior working.
- Add a pure API in `OrganizationPlanner.cs` for exact candidate generation, for example `BuildExact(OrganizationState state, DateTimeOffset createdAt, DateTimeOffset expiresAt)` returning `ExactOrganizationPlan?` (`null` when no candidates). Use the frozen `PlannedOrganizationStep`, route-leg, readiness and expectation records. Document the chosen public signature and deterministic ID/order rule in code. Same input and timestamps must produce same plan/step IDs and order. Do not read the store or clock inside the pure planner.
- Include inventory-only duplicate groups and exact source slots/quantities. Enumerate safe local withdraw/deposit/within-character moves and consolidation routes; express direct and middleman legs explicitly. Honor pinned slots, `minimum_to_keep`, Junk, never-move, chosen holder, role specificity/priority, item/category overrides and unknown coexistence. Never offer protected quantity.
- Calculate potential slots from actual bank entries and destination existing stacks. Respect known stack caps and available space. Unknown cap or bank/inventory capacity yields an upper bound plus `NeedsScan`/`ManualOnly`, never confirmed savings or a guessed `Ready` step. Keep verified savings zero without execution evidence. Keep item identity by sprite/color/name; durability-sensitive instances must remain distinct.
- Generate exact before/after expectations where evidence supports them. Use `OrganizationPlanContract.ItemFingerprint` for destination shape, and `OrganizationPlanContract.Validate` for saved candidates. If a route or after state cannot be justified, preserve it as non-ready with a clear blocker instead of inventing an executable step. No game actions or approval writes.

## Edge cases and tests

Cover five-character Water Dungeon Chests (observed stackable and user reports stack above five; exact cap unknown), inventory-only duplicates, stale bank, same-name variants, durability, full destination, capped stacks, multiple source slots with one pin, keep remainder, equal-priority role conflict, Junk/never-move, conflicting trade evidence, unknown coexistence, quantities above one-byte trade limit, and deterministic ordering. Run focused planner tests and `dotnet build DAOrganizer.slnx -c Release --no-restore -m:1 -p:UsedAvaloniaProducts=` plus the unit suite. Preserve existing preview tests.

Done when exact candidates are explainable, deterministic, validated, and never marked `Ready` on missing proof. Deliver a reviewable commit/PR; report baseline SHA, owned files changed, API signature, test commands/results, remaining blockers, unverified live behavior, and any frozen-contract issue needing local ownership. Do not start transfer execution.
