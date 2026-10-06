# Feature: Consolidate to bank

## Goal and user-facing behavior

- From an item detail drawer, choose one destination character and confirm a single **Consolidate to bank** action.
- The confirmation names the item, destination bank holder, every source character, and eligible quantities.
- After confirmation, DAOrganizer keeps the destination online at one reachable bank and processes source characters sequentially. The player sees only current character/action, completed transfers, and one blocker or recovery action.
- Existing item browsing, maintenance, profiles, schema 5 data, packet diagnostics, durable transfer journals, and synthetic protocol tests remain intact.

## Existing infrastructure and change shape

- `ItemGroups.Key`, `OrganizationPlanner`, account coexistence, `Organizer` login/travel helpers, direct exchange execution, bank actions, and the transfer journal remain the only identity, route, action, and recovery paths.
- Add a pure consolidation preflight/summary contract in Core, orchestration and progress in App, and an item-detail entry point. Remove Organization plan from the normal navigation; keep its code and manual-capture diagnostics available internally for tests and troubleshooting.
- Add navigation to a specified bank and a safe meet-near-destination helper. Do not add a proxy, item registry, credential store, or database migration.
- Data model/schema/migration changes: **none**. Each quantity chunk continues to use the existing saved exact plan and transfer journal.

## Implementation and handoff

| Phase/task | Scope and likely files | Input/contract needed | Output and validation | Depends on |
| --- | --- | --- | --- | --- |
| 1 | Core consolidation summary/preflight | item key, destination, saved state, coexistence and protection rules | deterministic eligible sources and one clear blocker; unit tests | existing organization state |
| 2 | Hands-off orchestration in `Organizer` and navigation | phase 1 summary; existing login, route, direct transfer and journal APIs | destination retained; sources processed sequentially; progress and safe stop | phase 1 |
| 3 | Item drawer confirmation/progress and normal-navigation cleanup | phase 1/2 contracts | one understandable entry point and progress surface; UI checks/screenshots | phase 2 |
| 4 | Docs and full verification | final behavior | build, unit, headless UI, gallery, diff review, live-validation statement | phases 1–3 |

Shared integration files are changed sequentially in this task; there is no parallel schema or contract owner.

## Edge cases and recovery

- Block before item movement for Junk/Never move/pinned sources, unknown or denied account coexistence, missing saved credentials for offline characters, pending custody, missing route data, or no eligible quantity.
- Re-read snapshots after login/bank scans and before every one-unit protocol-safe chunk. Never withdraw a quantity reserved by a pending run.
- Every exchange and deposit retains packet/state confirmation. An uncertain action stops the workflow, reports the journal's last verified holder, and exposes only the existing safe continuation action where one is proven.
- A completed source is safely logged out and closed. On cancellation or an uncertain action, the destination and current source remain available for custody inspection and recovery. Excalibur is never controlled.

## Verification and done

- Focused tests: summary quantities/protection/coexistence/pending custody, sequential orchestration decisions, specified-bank routing/meeting, and item-detail confirmation/progress interaction.
- Run the Release build, unit suite, headless UI checks, gallery, and `git diff --check` from repository instructions.
- Done means the normal UI has one item-detail consolidation path, old planning/testing controls are absent from normal navigation, operations journal each chunk and stop safely, existing profiles open without migration, and remaining live-only evidence is reported.

## Future considerations

- A proven middleman handoff can later satisfy same-account sources. Until then those sources are rejected before movement.
- Dedicated decoding of the bank-full NPC text can improve the blocker label; confirmed inventory non-decrease remains the safe stop today.
