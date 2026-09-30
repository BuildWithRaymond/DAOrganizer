# Manual trade capture for exchange codec validation

Use a build containing the **Organization plan → Manual trade packet capture** control. DAOrganizer observes only clients launched through its proxy. This feature records exchange and inventory packets; it sends no trade packets.

1. Connect two distinct, ready characters on accounts that can be online together. Put them on the same map and adjacent tiles. Use only low-value items.
2. Open **Organization plan**. Select the two characters and choose **Start capture**. The operation ID appears below the plan.
3. In the game clients, manually exchange a **stack quantity**, such as 2 from a stack of at least 3. Include opening the trade, choosing the slot, the quantity prompt and reply, both offer windows, both accepts, final close, and visible inventory updates. Do not include extra items or gold.
4. Return to **Organization plan** and choose **Stop and save capture**. Keep both clients connected until after this step. The folder path appears in the dialog.
5. Repeat in a separate capture with a **single item** so we can check whether its quantity flow differs from the stack flow.
6. Send the local folder paths for both captures. Each folder contains `first.json`, `second.json`, and `analysis.json`. Check that neither session says `"Truncated": true`. If the app is in demo mode, the capture controls are unavailable.

Default output: `%LOCALAPPDATA%\DAOrganizer\diagnostics\manual-trades\<operation-id>\`. A custom `DAORGANIZER_DATA_DIR` changes the base directory. The JSON includes character/session names, process IDs, item names, inventory snapshots, timestamps, and full **decrypted payloads** for client `0x4A` and server `0x42`, `0x0F`, `0x10`, and `0x37`. Keep files local. Before sharing outside this workspace, redact account/character identifiers in both JSON fields and payload bytes without changing protocol lengths; do not include passwords or chat. These files are never sent to community telemetry.

Validation gate: compare the captured inbound **and outbound** byte layouts with Arbiter's decoder/encoder, including partner runtime IDs, slot versus trade-window index, quantity, both accepts, final close, and inventory deltas. No automated exchange send should be enabled from `/dasb` notes alone.

`analysis.json` reports whether both sessions showed the same partner, one item offer, both accepts, matching inventory update packets, exact quantity conservation, and stable unrelated items/gold. Only a verified result adds one local tradeability success observation for that capture ID. An incomplete, ambiguous, cancelled, or malformed capture adds no tradeability evidence. This is still a manual-trade observer: it does not prove a separate final-close opcode, send exchange packets, or upload any trace.
