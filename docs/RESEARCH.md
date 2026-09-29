# Protocol and implementation notes

## Arbiter

[ewrogers/Arbiter](https://github.com/ewrogers/Arbiter/tree/3b5af118bfc3310189b42879d2bab59205af9c88) is the runtime foundation. Its proxy listens on loopback, forwards official server connections, handles encryption/sequence numbers and server redirects, and offers decoded messages and filters. Its Interop library launches a suspended Windows process and exposes process memory access. The IO and Imaging libraries decode installed game archives and item sprites.

DAOrganizer uses these libraries without the full Arbiter UI. Each new client has a separate proxy on an automatically assigned local port. Launch patches target only that new suspended process, after SHA-256 and original-byte checks. Vendor change: `SuspendedProcess.Start` accepts an optional working directory so the native game resolves its assets correctly.

Saved login keeps the native client's login packet metadata. The game submits a temporary ticket; a matching proxy filter replaces only the password field with the Windows-protected saved password. Expired tickets are blocked. This avoids relying on Arbiter's explicitly untested login serialization transform.

## Other sources

- [ewrogers/da-rpc](https://github.com/ewrogers/da-rpc/tree/afac5587cc4d3d24bd1da70535d43e563d1fcc40): reference for the client bank/dialog list variants, especially ordinary item quantity fields and the 0x004B extended record layout. No RPC dependency or injected RPC DLL is used.
- [ewrogers/darkages-741-re](https://github.com/ewrogers/darkages-741-re/tree/a490eff1d1d0062a3494fc9128747cfd6f31f845): native client behavior and inventory capacity. Item slots are 1–59; slot 60 is gold. The server's 0x58 control packet is not proof that an inventory baseline is complete.
- [eriscorp/DALib](https://github.com/eriscorp/dalib/tree/9a2a72deba67a03f787f9db9b78ffa47fe013824): map format reference: six bytes per tile, three little-endian ushort values. Installed `sotp.dat` supplies foreground collision flags. No DALib code is linked.
- [Chaos](https://github.com/Sichii/Chaos): bank withdrawal list generation provides a cross-check for the ordinary item-list value field containing stored quantity. A private server implementation does not by itself establish official-server behavior.
- [Avalonia headless testing](https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform): the UI check tool renders real controls with Skia into PNGs using a separate synthetic profile.

## Snapshot handling

Inventory/equipment packets update an in-memory session. After login context and a quiet interval, snapshots replace SQLite rows inside a transaction. Later add/remove/quantity updates are debounced. Malformed inventory packets invalidate the baseline and preserve saved data. Bank item lists are accepted only after a matching NPC withdrawal-menu request; arbitrary shops do not overwrite banks. Truncated or oversized bank lists retain previous items and mark the snapshot incomplete.

## WorldLogs

The supplied folder loaded 1,432 maps. Routing treats portals and world-map destinations as directed edges; it does not invent reverse connections. One-tile boundary exits are retained, while unrelated out-of-bounds observations are excluded. Local movement uses installed map collision data, current creature positions, and authoritative server position updates. Routes can still fail when observations are stale, doors/access requirements differ, or an NPC is not visible.

## Verification status

Build and 22 synthetic checks pass; UI renders were inspected at 1180×810 and 980×720. The published desktop app was started successfully. A live session subsequently wrote 36 carried items and 16 equipped items to its normal database. This establishes live packet capture and persistence; inventory completeness, saved-password login, bank quantities, slot organization and travel have not yet been compared against the game.

## Account update extension

The user's correction establishes storage maps as bank candidates and rules out inns. The grid now matches 12×5 native slots. Category inference uses item-name rules and known equipment slots, followed by explicit user overrides. Display filters retain all stored snapshots.

Safe logout follows the native protocol's [CQuit](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/docs/network/client/011-0x0b-quit.md) and [SQuit](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/docs/network/server/076-0x4c-quit.md) documentation: request `0B 01`, accept approval `4C 01`, acknowledge `0B 00`. An approval packet is not a disconnection. Only a client launched by that queue, identified by PID and process start time, is eligible for graceful window closure. No forced process termination is used for account cycling.

Version 2 passes 38 automated checks covering category/display behavior, bank route selection, sequential queue ordering, cancellation, failed logout, and approval packets with or without padding. Nine UI states render successfully. A bank snapshot from the first live build is present, but the new complete account cycle has not yet been exercised with live saved credentials.

## Login and inventory corrections (v3)

Inspected Arbiter's pinned `Arbiter.App/Services/Client/GameClientService.cs` and searched its application code for password entry. Its launcher sets up the proxy and client patches; it does not implement saved-password form entry. DAOrganizer's form entry is its own code. The screenshot showing a numeric token appended to the character name exposed missing scan codes in DAOrganizer's synthesized keyboard events.

The native [keyboard decoder](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/legacy/docs/input/keyboard-and-packets.md) uses the hardware scan code and extended-key bit. Input now supplies both, with ordered delays between clearing, typing, Tab, and submitting. The [native login packet](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/docs/network/client/003-0x03-login.md) contains installation metadata; tests verify all 16 trailing bytes survive password replacement. Native authentication remains responsible for creating that metadata.

The [inventory packet](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/docs/network/server/015-0x0f-add-inventory.md) provides sprite, dye, name, quantity, stackability and durability. It has no drop or trade permission. Neither the documented item pane nor bank list provides one. Stackability is not transferability. Badges therefore display unknown; the app never probes by dropping or giving an item.

Dragging now uses pointer capture inside the inventory grid. The former handlers missed Button's handled press event. A headless test drives actual press/move/release events and verifies the move path is reached without opening item details. Sorting fills row 5 first, left to right, then row 4 and upward; gold slot 60 is excluded. Existing Red Potion/Komadium stacks in slots 1 and 2 and explicit pins are preserved.

## Login readiness and field acknowledgement (v4)

The updater's stored result was a generic action timeout. The previous routine used fixed startup delays, rapid clearing, and unacknowledged Unicode keyboard input; it could not distinguish a field that ignored input from a submitted login awaiting a server response. Live reproduction was unavailable because the Computer Use native pipe was missing and no updater-owned client remained open. The user reported the name present and password field blank.

The new form routine observes the native dialog through read-only Arbiter process memory. It waits for the main-menu stipulation gate, visible registered login pane, absence of ClockPane input blocking, and the intended focused control. It clears only existing text, verifies each character, and submits exactly once after both fields match. Text enters through synchronous WM_CHAR delivery. No client function is injected or called remotely. Readbacks expose lengths and equality flags, not password text. The real saved password still enters only at the matching proxy filter.

Sources: [dialog control order and focus](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/docs/systems/ui.md), [pane and text-buffer layouts](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/docs/appendix/runtime/panes.md), and [event blocking and focus delivery](https://github.com/ewrogers/darkages-741-re/blob/a490eff1d1d0062a3494fc9128747cfd6f31f845/docs/systems/events.md). Login and main-menu vtables 0x67896C and 0x6788EC were independently checked against the supported installed executable's RTTI locator references.

Tests cover delayed screen readiness, delayed focus and character delivery, retained names, ignored password input, cancellation, exact field comparisons, hidden/unregistered dialogs, input blocking, changing event trees, oversized text, and explicit server rejection. Live successful authentication remains unverified.

## Excalibur-safe comparison (v5)

Read-only reference: `<local-reference>/Excalibur/ProxyBase/LoginTypingAutomation.cs` and `User32.cs`. The Excalibur-dev copy uses the same essential sequence. No Excalibur files or processes were changed.

Excalibur sends messages to a captured game HWND with PostMessage. Name uses WM_CHAR; Return advances to password; password uses WM_KEYDOWN/WM_KEYUP with scan codes; Return submits. DAOrganizer v4 instead required foreground focus and sent WM_CHAR for the masked field as well. The latest persisted updater error was focus loss during login.

V5 uses window-addressed posted input for login, separate name/password input paths, and Return to advance from name. It retains observed dialog readiness, per-character readback, and single submission after verified fields. The target window is checked against PID and process start time before input; navigation still requires foreground focus. Only the numeric placeholder enters the game form; saved credentials remain in the proxy.

`tests/DAOrganizer.LoginCheck` is an explicit real-client check. It launches only its own temporary game clients, uses dummy fields, installs a raw opcode 0x03 blocking filter before launch, compares the native login packet, and closes only its own clients. It never reads the credential vault and never forwards a login request to the server. It is not run by ordinary unit tests.

The first real-client batch passed twice, then timed out verifying the first name character. An unchanged diagnostic repeat passed all three. This exposed the remaining startup timing sensitivity, so the final routine also retains Excalibur's settling pause after focus changes (500 ms), followed by another focus check. A regression case models a visible, focused dialog whose text editor is not yet accepting input.

Final verification: 54 automated checks pass. Three fresh official-client runs with the final code each filled both fields and produced the expected native CLogin packet. Every packet was blocked and each test-owned client closed normally. Transcript: `artifacts/login-entry-check-v5.txt`. These checks establish form entry and native submission, not saved-password server acceptance or the complete bank-update cycle.

## Home inn and walking corrections (v6)

Compared the local reference at `<local-reference>/Excalibur`. No reference files or running Excalibur processes were changed.

- `ProxyBase/Client.cs`, `TryWalk`: automation sends client opcode `0x06`, direction and a monotonically wrapping byte counter. `ProxyBase/Server.Messages.cs`, `ClientMessage_0x06_Walking`, rewrites native walking to use the same counter.
- `ProxyBase/Client.TavalyMovementSync.cs`, `TryWalkTavalyAcknowledged` and `ApplyTavalyAuthoritativeMove`: movement can wait for the server's `0x0B` reply before sending a self `0x0C` presentation. `ResolveTavalyAuthoritativeMove` leaves supplied coordinates unchanged when the direction does not represent a step.
- Organizer previously sent immediate arrow down/up events and waited for coordinates to change. This input path could leave the character facing another direction without a walk. Organizer now sends movement packets, uses a shared native/automation counter, and animates confirmed automated steps. It retains one outstanding step, a two-second confirmation limit, the existing 300 ms pause, focus/manual-input checks and authoritative position checks. Explicit rejection can reroute around a blocked tile; missing confirmation stops travel. A late acknowledgment can still present the already-sent step after cancellation.
- Login previously ignored pursuit dialogs and had no home-inn action. It now recognizes a travel offer containing “home inn” during the first 35 seconds of login, strips game color codes, and selects a “Yes” option in either pursuit or merchant menus. Pursuit responses preserve the entity and pursuit, advance the dialog step and send the one-based option. Merchant responses use the selected pursuit. No fixed NPC ID or option order is assumed. Purchases, changing the home inn, missing Yes options and unrelated dialogs are ignored.
- An accepted offer keeps readiness false until a subsequent map and position arrive, followed by the inventory quiet interval. Missing arrival after 15 seconds sets an error so the account queue cannot calculate a bank route from the guildhouse. Repeated copies of the prompt do not send duplicate replies.

Validation: full Release solution build with zero warnings/errors; 73 automated checks pass. Results: `artifacts/test-results-v6/travel-v6.trx`. New tests use synthetic protocol packets through the session observer and registered movement filter; they do not send commands to a live account. The exact live guildhouse prompt and the complete saved-login → home inn → bank → logout cycle remain to be checked in game. Package: `artifacts/DAOrganizer-v6/DAOrganizer.exe`.
## Nation login prompt correction (v7)

The user supplied the actual guildhouse popup text: “Would you like to go to your nation?” with Yes and No. Version 6 required “home inn” in the body, so it ignored this offer and never queued a reply. Version 7 recognizes that exact nation-travel question (case-insensitive, allowing whitespace and game color codes) in both supported menu formats. The existing login window, Yes-choice lookup, duplicate guard and arrival wait still apply.

Regression tests first reproduced the missing reply for the plain and color-prefixed text and for a merchant menu. They check the emitted pursuit/merchant packet bytes, preserve the existing home-inn behavior, wait for map and position before inventory readiness, and reject nation-change and paid-travel prompts. These are synthetic packet replays using the user-confirmed wording; the live server exchange has not been captured. Package: `artifacts/DAOrganizer-v7/DAOrganizer.exe`. Test results: `artifacts/test-results-v7/nation-prompt-v7.trx`.
## Background travel and failed-client preservation (v8)

Read-only inspection of the organizer's saved account results found multiple failures with “Game lost focus. Travel stopped.” The implementation explained the reported window closure: Navigation threw that exception on foreground changes, then AccountUpdateQueue caught it and deliberately requested safe logout and closed its owned game window. The focus requirement remained from keyboard walking even after v6 switched to packets.

Navigation no longer activates the game or requires foreground focus. It only checks Escape and arrow key states when the owned game is focused, so input in another application does not cancel travel. Session availability, Stop cancellation, actual manual-walk packet detection, server confirmation, damage cancellation and path checks remain active.

Queue failures now retain the client and report “client left open for manual handling,” then skip remaining accounts. A local travel interruption keeps its original reason rather than being labeled “Stopped by user.” Successful scans and an explicit organizer Stop request still perform the existing safe logout and owned-window close. The running organizer and game processes were left untouched during development.

The regression checks first reproduced both the focus exception and the automatic logout/close calls. They cover background travel, unavailable characters, cancellation, failed-client retention, local interruption reasons, and successful/explicit-Stop cleanup. Package: `artifacts/DAOrganizer-v8/DAOrganizer.exe`; test results: `artifacts/test-results-v8/background-travel-v8.trx`. Live background travel still needs an in-game check.
## Item grids and maintenance (v9)

The user defined bank silence as empty and chose explicit Auto-deposit marks plus one attempt at dropping marked junk. After a correlated Withdraw Items request, ten seconds without a menu response now writes a complete empty bank snapshot. A changed map/menu, disconnected session, parser error or unrelated shop response cannot clear the bank this way.

All accounts groups by normalized item name, sprite and color. Category grids show summed quantities; the drawer shows each owner, location, slot and snapshot freshness. Persistent Keep/Junk/Auto-deposit rules share that identity. Counts honor the displayed characters and location filter. Maintenance previews use all saved accounts, including hidden characters, and cap actions to selected saved quantities. Missing/moved items are skipped. Bank same-name variants are skipped because the native withdrawal selector accepts a name, not a variant identifier.

Protocol source: local Excalibur-safe working copy, ProxyBase/Client.cs, DialogueRespond (lines 23468 onward) and its packet writer (around 23713). Native bank actions use merchant 0x39, pursuit 0x53 for a deposit slot and 0x54 for quantity (byte 1, byte slot, string8 quantity); withdrawal uses 0x56 with string8 item name, then 0x57 with name and string8 quantity if prompted. The executor first reads server menus and requires these supported IDs. Arbiter ClientMerchantMessage now encodes/decodes the native deposit quantity arguments. Drop uses 0x08 with inventory slot, current tile and quantity. The proxy's registered message type is used for every send.

Inventory/equipment packets contain no transferable permission. Cleanup therefore attempts marked inventory stacks once. Bank cleanup withdraws one unit, observes the inventory increase, then drops that unit; lack of confirmation blocks further attempts for that item on that character. Each send rechecks item identity and rule. Equipment, pins and protected supplies are excluded. Stop and unexpected manual input, movement, health or connection changes leave the current client open. Confirmed operations run sequentially. Marked Auto-deposit items are also processed during account updates. Outcomes persist in maintenanceResults.

Validation: Release solution build, zero warnings/errors; 105 unit/packet tests in artifacts/test-results-v9/tests.trx. Packet replay covers the full deposit and withdrawal prompt sequences through ProxyConnection, silent bank correlation, one-shot rejection, cleanup quantity limits, protection and manual intervention. Headless UI checks and reviewed images are in artifacts/ui-checks-v9-final. These checks use synthetic data only; no live cleanup/deposit was executed. Excalibur and existing organizer/game processes were not closed or restarted.

## Direct Excalibur hax banking (v10)

The user reported junk cleanup did not withdraw and requested Excalibur's hax packet path. Read-only inspection of the latest saved maintenance result found a timeout before a per-item outcome. That result does not identify which earlier wait timed out; no live wire trace was captured. Source comparison found a concrete mismatch: v9 opened NPC menus, required specific list/quantity menu responses, and rescanned inside every withdrawal. Excalibur's hax path sends the final transfer directly to a visible mundane and then sends PopupClose.

Reference: Excalibur-safe WORKING COPY/Excalibur/ProxyBase/Client.cs, /withdraw and /deposit command handlers around lines 5740–6030, DialogueRespond around 23468–23747, PopupClose around 23885–23904, and AutoDeposit around 25768. Client.WarMode.cs HaxWithdrawWarModeExperienceGem explicitly sends Withdraw <name> [1]. These references were read only; Excalibur was not rebuilt, stopped or restarted.

The corrected sequence uses existing proxy encryption and framing:

- Bank list: client 0x39, entity type 1, NPC uint32, pursuit 0x0045, no arguments. Correlation is established before sending, so it does not need a prior root menu. Cancellation clears the outstanding request; a canceled scan cannot later clear saved bank data.
- Withdraw one: client 0x39, type 1, NPC uint32, pursuit 0x0057, string8 item name, string8 "1".
- Deposit one carried item: client 0x39, type 1, NPC uint32, pursuit 0x0053, byte inventory slot.
- Deposit a stack or part of a stack: client 0x39, type 1, NPC uint32, pursuit 0x0054, byte 1, byte inventory slot, string8 quantity.
- After each transfer: client 0x3A, type 1, same NPC uint32, pursuit 0, step 1, no arguments. This is Excalibur PopupClose(npc, 1), not a game-window close.

The proxy already supplies the six-byte dialog header, CRC, dialog encryption and normal packet trailer. Adding Excalibur's header/trailer bytes to the typed payload would duplicate them. Transfer spacing matches Excalibur's 1100ms delay. Inventory changes still confirm success; timeouts do not retry or drop unconfirmed withdrawals. Existing rule, quantity, ambiguity, pin, connection, manual-input and damage checks remain in place. Bank travel and account orchestration remain as before.

Regression tests first failed on v9's root-menu request and prior-bank-read requirement. They now cover single/stack/partial deposits, exact withdrawal and close payloads, direct bank-list correlation, full two-unit withdrawal/drop execution, absent NPCs, cancellation, and no extra transfer/drop after an unconfirmed withdrawal. Full Release solution builds with zero warnings/errors; 112 tests pass. Results: artifacts/test-results-v10/hax-banking.trx. Package: artifacts/DAOrganizer-v10/DAOrganizer.exe. No live account transfers or drops were performed during this fix.

## Popup-close guard and precise stop reasons (v11)

The user reported the generic game-state/manual-input error during Amusement Park Ticket withdrawal, described a stack of two with the bank quantity UI, then confirmed a later attempt succeeded. The saved result contained only the combined guard message. No live packet trace identifies which branch fired, so the exact cause of that particular run remains unconfirmed.

Source inspection showed that every native Pursuit packet incremented the manual-item revision, including a harmless close matching the organizer's own PopupClose request. Packet replay reproduced the same stop with such a close. A bounded allowance now accepts only a same-NPC, type-1 Pursuit, pursuit 0, step 1, no-argument close, within five seconds of a close the organizer sent. Each sent close permits one matching native close. Other NPCs, menu selections, quantity submissions and item commands still interrupt the operation. This is a classifier fix, not removal of the manual-action guard.

The guard now reports its actual reason: tracking error, connection loss, incomplete inventory, map or position change, health decrease, manual movement, or a named client item command with opcode. Tracking exceptions also include the packet opcode without logging payloads or credentials.

Withdrawal packets and quantities remain unchanged. A bank popup followed by delayed inventory confirmation does not send a second withdrawal. The new ticket replay starts with two banked tickets and confirms exactly one received unit. There is no automatic quantity resubmission or timeout retry.

Validation: 118 unit/packet tests, including the reproduced popup-close stop, permitted matching close, rejected manual quantity/other-NPC/menu-choice packets, delayed ticket confirmation without duplicate withdrawal, and precise tracking-error messages. Results: artifacts/test-results-v11/popup-guard.trx. Package: artifacts/DAOrganizer-v11/DAOrganizer.exe. Existing game clients, organizer and Excalibur were left running; no live withdrawal or drop was triggered by development.


## v12: potion hotkeys, category families and sound tracking

User-specified potion positions now apply to both sorts and saved-layout restore: Komadium 1, Red Potion 2, Exkuranum 3, Dibenomum 4, Hemloch 5. One stack is selected for each present potion, preferring the stack already at its correct position, then the lowest original slot. Potion positions override conflicting old pins. Other pins remain fixed. Missing potions do not renumber the hotkeys. Vacant positions can hold other items when capacity requires; duplicate stacks are preserved. Correctly placed potions cannot be dragged out or displaced by an organizer layout and are protected from maintenance. This does not prevent manual moves in the native game.

Remaining sorted items retain the established bottom-row-first traversal. Category sorting now compares category, family, item name, then original slot. The all-accounts view uses the same family order and labels. Exact classifications, specific wearable names and broader fallback rules replace the old keyword list, which incorrectly classified Komadium and Exkuranum as Materials. Current automatic rules supersede old stored automatic categories; explicit user category overrides and observed equipment remain higher priority.

Primary references checked September 29, 2026:

- https://vorlof.com/general/consumables.php ? potions, food/drink, teleport songs, practical and mass items.
- https://vorlof.com/weapons.php ? weapons without obvious generic names, such as Magus Zeus and Snow Secret, plus weapon families.
- https://vorlof.com/armors.php ? Bliaut, Cuirass, Dobok, Dugon and other armor names.
- https://vorlof.com/equipment.php ? equipment slot families.

The bundled rules use selected factual names/categories and local fallback heuristics, not an exhaustive copy of the website. They need no network at runtime. Unsupported names remain Other or can be categorized manually.

During this work, the user supplied a deposit failure screenshot: `Tracking error: Tracking packet 0x19: Cannot read past end of buffer`. The earlier saved result was the generic v10 state guard message. Server 0x19 is SoundEffect. Arbiter's decoder unconditionally reads track and a ushort after sound FF. Excalibur's ServerMessage_0x19_SoundEffect only requires the first byte. Synthetic 1-, 2-, and 3-byte FF sound payloads reproduced the user's exact exception through the organizer's real asynchronous proxy observation queue, including a deposit that otherwise received inventory confirmation.

The organizer does not consume sound data. It now returns before decoding SoundEffect while leaving the original packet forwarding unchanged. Inventory, equipment, map and other tracked packet failures still stop work; no blanket catch or action retry was added. Replays cover all short variants and the existing four-byte format during deposit, plus malformed AddInventory remaining a tracking failure. Banking transfer quantities and packets are unchanged from v11.

Validation: 153 unit/packet checks; full Release build with zero warnings/errors; Avalonia headless checks at normal/minimum sizes, including all five visible potion locks, normal-item drag, category grids, totals, owners and maintenance previews. Test report: artifacts/test-results-v12/v12.trx. Synthetic UI renders: artifacts/ui-checks-v12. Package: artifacts/DAOrganizer-v12/DAOrganizer.exe. No live inventory moves, deposits, withdrawals or drops were performed during development. Excalibur and existing game clients were not stopped or restarted. The updated build needs an in-game check with the affected character.


## v13: preserve item-grid scroll position

Clicking an aggregate item recreated its ScrollViewer with offset zero. Both the tile callback and the common action wrapper forced a refresh, so an initial restoration could also be discarded before layout ran.

The grid now captures its offset and a visible item before rebuilding. Opening details uses the clicked item as the anchor; ordinary refreshes and closing details use the first fully visible tile. After layout establishes the new extent, the same item is restored to its prior viewport height. This accounts for the changed column count when the drawer opens or closes. If the item disappears, the previous offset is retained within the available scroll range. A different search, character or location starts a fresh view. Item and close callbacks rely on the common action wrapper's single refresh.

A regression check reproduced the original failure with a real pointer click on Stored item 070: its vertical viewport position changed from 405 to 2702 and the new scroll offset was zero. The same check now passes, together with a rule-change/timer refresh, drawer closure through reflow, and a fresh search resetting to the top. All checks use a separate synthetic profile. UI renders: artifacts/ui-scroll-v13-final. The harness advances the headless render timer and verifies the drawer finishes appearing. Unit/packet suite: 153 passing, artifacts/test-results-v13/v13.trx. Build: zero warnings/errors. Package: artifacts/DAOrganizer-v13/DAOrganizer.exe. No game actions or process restarts were performed.

## v14: public Celtic collection release

The Avalonia interface now uses obsidian surfaces, warm gold accents, Georgia headings, original Celtic vector ornaments and illustrated fallback icons. Category sections keep families together; totals and item ownership remain available in the drawer. Protected inventory slots have a five-potion legend. A constrained gold label scales down at minimum window size instead of clipping.

`--demo` builds six fictional accounts in an in-memory database. It skips game artwork and WorldLogs loading and blocks live operations, account updates, settings persistence and credential writes. The gallery verifies these boundaries and renders the actual interface at normal and minimum sizes. Each interaction-check run creates a unique synthetic profile, avoiding contamination from a previous run's saved rules.

The source release includes MIT licensing, retained dependency attribution, contribution/security guidance, issue templates, Windows CI, and portable packaging that resolves license files from the restored package folders and runtime version. Public screenshots contain only the demo collection and original vector drawings. User profiles, game files, WorldLogs and build outputs are excluded from source control.


## v15: original sprites, flexible quick slots and bank reads at login

The user revised the fixed-slot requirement: quick potions pack from slot 1 in their established order, skipping missing types. A carried Dual Crystal Arrows stack takes slot 1 first regardless of class, so potions start at 2. Explicit pins override sorting preferences. The old potion lock was removed from UI dragging, saved-layout application and the packet action path; manual pins remain reversible. Trinkets fill the first row from slot 12 leftward. Duplicate stacks and overflow remain in the layout.

Factual trinket names were checked against https://vorlof.com/trinkets.html, including Wake Scroll, Glowing Stone, Nerve Stimulant and Vanishing Elixir. The app does not copy its descriptions or artwork. Observed short names for the two/three-move combo scrolls are also recognized.

The invented item glyphs were removed. The demo now uses selected factual sprite/color identifiers and reads original sprites from a local game installation. Public screenshots use fictional characters and quantities; no account identities, ownership records or raw game assets are published. The gallery's `--require-sprites` mode verifies every demo sprite is available before capture. CI remains able to run without a game installation using text fallbacks.

Account refresh waits for a visible NPC after login and sends the existing direct withdrawal-list request, without requiring WorldLogs or walking. An innkeeper is not saved as a banker preference. If explicitly marked, unpinned items need depositing, bank travel occurs before the transfer. Manual maintenance also retains bank travel for deposits and withdrawals. The bank-scan button now reads locally; explicit travel remains a separate action.

Validation: 161 unit/replay checks, including full inventories, compact potion gaps, arrow priority, trinket overflow, pin preservation, a confirmed manual potion move, an innkeeper read with no routes, and refusal to deposit there without a bank route. Headless interaction checks cover reversible potion pins and preserve drawer scrolling. The original-sprite gallery checks all demo item art. No live inventory actions were performed during this change.
