# Sanitized exchange-capture evidence

These notes contain anonymized facts derived from local manual captures. Raw JSON, character names, runtime IDs, and full inventories are deliberately excluded. `/dasb` behavior is a research lead only; it is not evidence that DAOrganizer's vendored client uses the same packet layout.

## Read-only observer contract

`ManualTradeTrace` retains full decrypted payloads only for client exchange `0x4A` and server exchange/inventory `0x42`, `0x0F`, `0x10`, and `0x37`. Other packets contribute only a bounded timestamp/direction/opcode entry. Both bounds are reported independently. The payload-free timeline may reveal a candidate close opcode in a later controlled capture without collecting chat or other unrelated private payloads.

`ManualTradeAnalyzer` exposes partner identity, offer, accept, event 5, inventory conservation, denial, and unknown-close evidence separately. A controlled manual exchange with payload-free timelines and visible window close established a narrow two-sided completion pattern; see [final-close observation](2026-09-29-manual-final-close.md). It can return `Verified` only when both sessions receive acceptance for both parties with post-second-accept notices, recipient inventory delivery, exact conservation, and no unknown post-accept server opcode. Early event `0x42/5` alone remains insufficient. This is manual tradeability evidence, not an automated transfer contract.

An item-specific denial is reported only for an explicit server cancellation message tied to the selected source slot while inventories and gold remain unchanged. Generic cancellation, disconnect, timeout, truncation, malformed data, unknown close subtype, or conflicting evidence stays `Inconclusive` and must not create non-tradeable evidence.

## Unresolved final-close question

The two 2026-09-29 captures prove that event 5 can occur before the recipient accepts. They do not prove whether a separate server opcode/subtype closes a successful exchange. A later controlled manual capture must retain both sessions' payload-free opcode timelines through the **second accept, recipient inventory gain, and visible exchange-window close**, including their ordering and timestamps. Keep unrelated payloads filtered. If a candidate close opcode appears, selectively inspect only that candidate payload in a follow-up capture, then compare success and cancellation. Until delivery finality is proven, no outbound exchange should be enabled.
