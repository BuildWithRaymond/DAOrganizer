# Manual single-item exchange observation

Captured from two distinct DAOrganizer-proxied sessions on 2026-09-29. Raw local JSON stays in the user profile and is **not** in this repository. This note omits character names, runtime IDs, and inventory lists. It describes one successful trade of one nonstackable item; it does not validate stack transfers or general transfer automation.

## Observed sequence

| Relative time | Sender | Recipient | Meaning |
| --- | --- | --- | --- |
| 5.566 s | client `0x4A` action 0, 5-byte payload | | Open; action + 4-byte partner ID. |
| 5.567 s | client `0x4A` action 1, 6-byte payload | | Add item; action + partner ID + inventory slot. **No quantity byte.** |
| 6.057–6.152 s | server `0x42` event 0 and event 2 | server `0x42` event 0 and event 2 | Both see expected partner and matching offer. Manual client sent action 1 before its event 0 arrived; automated flow should wait for both starts. |
| 6.059 s | server `0x10` removes source slot | | Removal occurred at offer time, before both accepted. |
| 8.427–8.666 s | client `0x4A` action 5; server `0x42` event 5 | server `0x42` event 5 | Each received `Accepted` with “You exchanged.” **before recipient acceptance**. Event 5 alone is not proof of completion. |
| 10.247–11.068 s | server `0x42` event 5 for other party | client `0x4A` action 5; server `0x0F` add; server `0x42` event 5 | Recipient gained exactly one item. Both sides eventually saw event 5 for both parties. |

The partner names in event 0 matched the opposite captured session. Every outbound partner ID matched that side's event 0. Both offer packets matched on sprite, color, trade-window index and displayed name; the party byte was `You` on sender and `Them` on recipient. The offer displayed a durability suffix (`100%`) that the inventory item's name did not contain, so exact offer-name equality is unsafe without normalization. Before/after snapshots show exactly one source slot removed, one recipient slot added with the same item and quantity, all other inventory entries stable, and unchanged gold. Neither trace was truncated.

## Codec results

- Arbiter's `ClientExchangeMessage.Deserialize` already handles action 1 without a quantity byte. Its `Serialize` wrote an extra `1`; fixed to write only the slot for action 1. Capture-derived tests assert reader and writer bytes for actions 0, 1 and 5.
- Server `0x42` event 2 decodes as party, trade-window index, big-endian sprite, color, String8 offer name. No offer quantity appears in this single-item packet. Event 5 decodes as party plus String8 message. Capture-derived tests round-trip event 2 and decode event 5.
- Recipient server `0x0F` decodes and re-encodes as inventory slot, big-endian sprite, color, String8 name, big-endian 4-byte quantity, stackability byte, big-endian max durability and current durability. This capture had all durability fields.
- Source server `0x10` had **four payload bytes**. The first was the removed inventory slot; the meaning of the other three bytes is unresolved. Arbiter currently reads only the slot. Do not claim the full `0x10` layout is verified.
- No server `0x37` packet appeared, so this capture cannot settle the item-versus-equipment opcode question.

## Remaining gate

A [partial-stack capture](2026-09-29-manual-partial-stack.md) now validates action 2 and the quantity prompt. Further variants and a verified two-session coordinator are still needed before enabling automated exchange, especially because event 2 has no typed quantity field and event 5 can precede completion. Keep every operation in review if quantity or final custody cannot be proven.
