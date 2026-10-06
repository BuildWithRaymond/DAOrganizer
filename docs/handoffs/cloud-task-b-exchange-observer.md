# Codex Cloud Task B — Read-only exchange evidence

Branch from `feature/cross-character-organization` at exact Phase 1 baseline `23bee187c1f82110c84f2cf376fadafd3f37de02`. Read `docs/plans/CROSS_CHARACTER_ORGANIZATION_MASTER_PLAN.md` Phase 3, `docs/MANUAL_TRADE_CAPTURE.md`, and sanitized notes in `docs/protocol-captures/`. Implement only read-only observation; this task can run simultaneously with Cloud Task A.

## Ownership

Own only `src/DAOrganizer.Game/ManualTradeTrace.cs`, `src/DAOrganizer.Game/ManualTradeAnalyzer.cs`, `tests/DAOrganizer.Tests/ManualTradeTraceTests.cs`, `tests/DAOrganizer.Tests/ManualTradeAnalyzerTests.cs`, `tests/DAOrganizer.Tests/ExchangeCodecCaptureTests.cs`, and sanitized protocol documentation under `docs/protocol-captures/`. No `GameSession` integration, no outbound exchange sends, no inventory schema or `InventoryStore*` changes, no Organizer/App/UI changes, and no vendor/protocol writer modifications unless this task is explicitly sent back for local review. Never commit raw user captures, names, credentials, or full inventories.

## Required behavior

- Retain bounded full payloads only for relevant exchange/inventory packets. Add a bounded direction/opcode/timing timeline for otherwise filtered packets so a separate final-close signal can be found later without recording unrelated private payloads. Preserve backward reading of the two existing local capture JSON shapes.
- Separate offer, acceptance, early event 5, explicit item denial, and verified final delivery. Event 5 appeared before recipient acceptance in the manual capture; it alone is not finality. Require matching expected partner identities, offer item and quantity evidence on both sides, no extra offers or gold, proven successful final signal on both sides, and exact two-sided inventory conservation before `Verified`. If finality is not proven, report `Inconclusive`; do not label ambiguous failures non-tradeable.
- Keep analyzer pure and read-only. Specify result fields/evidence for future coordinator, including whether explicit item-specific denial is actually observed. Unknown close opcode/subtype remains unknown. Use synthetic/anonymized fixtures; distinguish capture-derived fact from `/dasb` research lead.

## Edge cases and tests

Cover one-client disconnect, truncation, duplicate/out-of-order events, runtime-ID reuse, early event 5, partial stack display suffix mismatch, unknown close subtype, extra gold/item, unrelated inventory/gold change, malformed payload, and both old capture shapes. Run focused trace/analyzer/codec tests, Release build and unit suite. No live trade is required in this task; state which final-close question would need a later controlled manual capture.

Done when observer cannot upgrade ambiguous evidence to verified tradeability, capture remains bounded/private, and old captures still parse. Deliver a reviewable commit/PR; report baseline SHA, owned files changed, exact tests/results, evidence contract, unresolved packet questions, unverified live behavior, and any need for local `GameSession`/vendor changes. Do not send packets or implement transfer execution.
