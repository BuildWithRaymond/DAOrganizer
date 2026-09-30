# Manual partial-stack exchange observation

Captured from two distinct DAOrganizer-proxied sessions on 2026-09-29. Raw local JSON remains in the user profile; this note omits character names, runtime IDs, and inventory lists. One successful trade transferred **2 from a stack of 25**. It complements the [single-item observation](2026-09-29-manual-single-item.md).

## Observed sequence

| Relative time | Sender | Recipient | Meaning |
| --- | --- | --- | --- |
| 5.972 s | client `0x4A` action 0 (5 bytes), then action 1 (6 bytes) | | Open and select source inventory slot 38. |
| 6.469–6.873 s | server `0x42` events 0 and 1 | server `0x42` event 0 | Both see expected partner. Event 1 prompts sender for slot 38. Its payload is 3 bytes: event, slot, unexplained `00`. |
| 7.774 s | client `0x4A` action 2 (7 bytes) | | Action + 4-byte partner ID + slot 38 + one-byte quantity 2. |
| 7.980–8.370 s | server `0x0F` updates source stack 25→23; server `0x42` event 2 | server `0x42` event 2 | Offers match on window index, sprite, color, displayed name and parties. Displayed name includes `(2)`; no separate quantity field was seen. Source decrease occurred before acceptance. |
| 9.034–9.877 s | client action 5; server event 5 | server event 5 | Event 5 reached both sessions **before recipient accepted**, again showing it is not final delivery proof. |
| 10.408–10.972 s | server event 5 for other party | client action 5; server `0x0F` adds 2 in a new slot; server event 5 for other party | Final snapshots show sender 23, recipient +2, all unrelated entries and gold stable. |

The event 0 partner names matched the opposite captured sessions. Each outbound partner ID matched its side's event 0. The two event 2 offers matched, with `You` on sender and `Them` on recipient. Both sides eventually saw event 5 for both parties. Neither capture was truncated.

## Codec results and limits

- Captured action 2 validates Arbiter's current reader and writer layout for this partial-stack transfer. A capture-derived test round-trips its exact 7-byte payload.
- `ServerExchangeMessage.QuantityPrompt` reads the prompted slot correctly but previously discarded a trailing zero byte. The codec now preserves unknown trailing bytes without interpreting them; a capture-derived test round-trips the 3-byte prompt.
- Sender server `0x0F` updates the existing stack with quantity 23, stackable flag 1, and two zero durability fields. Recipient `0x0F` adds quantity 2 with the same fields **plus one unexplained trailing zero byte**. `ServerAddInventoryMessage` now preserves trailing bytes without assigning them a meaning. Both variants have capture-derived decode/encode tests.
- The event 2 offered item text appended `(2)` to the inventory name. This is evidence of the offered quantity in this example, but it is display text, not a typed quantity field. An executor should also require an exact source inventory decrease before acceptance, both matching offers, no extra items or gold, and final recipient gain. Further variants are needed before treating the suffix as a universal contract.
- No `0x10` or `0x37` appeared in this stack trade. Source stack changes via `0x0F`; a single item was removed via `0x10` in the other capture. The capture filter records only selected opcodes, so it cannot rule out a separate close opcode outside those filters.

Two manual trades establish useful reader/writer fixtures, not general trade safety. Automated trading still needs a two-session coordinator, fresh human runtime IDs and positions, an operation journal, verified before/after conservation, timeout recovery, and a final-close interpretation. Until those are built and tested, all transfers remain manual.
