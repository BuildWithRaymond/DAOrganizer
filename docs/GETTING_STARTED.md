# Getting started

## Install or update

Download the Windows x64 ZIP from the repository's Releases page. Extract all files together. Run `DAOrganizer.exe`; no separate .NET runtime installation is needed. Releases are currently unsigned.

Use one organizer at a time. When updating, finish or stop current operations and safely log out clients launched through the old organizer before exiting it. Exiting disconnects its proxied sessions. Opening a new build uses the same saved profile. The organizer does not stop or update Excalibur.

To explore without a game installation, run `DAOrganizer.exe --demo`. The six fictional characters and all item rules are in memory and reset when the demo closes. Demo mode never opens your real profile or stores credentials.

## Set up your game

1. Open **Settings**. Select the supported `Darkages.exe` from your DATester 7.41 installation. The launcher verifies its SHA-256 hash before launch.
2. Set **WorldLogs folder** to your own map/route data. Walking to banks requires this data. No WorldLogs are shipped here.
3. Use **Add character**. Passwords are optional; if supplied, they are stored in Windows Credential Manager, never in SQLite.
4. Select the character and choose **Launch client**. Inventory and equipment update while connected through the organizer.

The supported executable SHA-256 is defined in `src/DAOrganizer.Game/ClientLauncher.cs`. The app does not patch files on disk. Its launcher applies checked in-memory patches only to a client it creates.

## Find and arrange items

**All accounts** groups saved inventory, bank and equipment by category. Search crosses displayed characters. **Characters…** changes visibility without deleting history. Each badge totals matching name, sprite and color variants. Click a tile to see who owns it and where. The grid preserves the clicked item's viewport position when details opens and preserves your place on refresh or close.

Inventory matches the game: 12 columns and five rows, with gold at position 60. Drag movable items to swap. Right-click to pin a slot. Sorting and saved-layout restore show a preview before sending moves, and each swap waits for confirmation.

When present, one stack of each potion is placed in its fixed slot:

| Slot | Potion |
| --- | --- |
| 1 | Komadium |
| 2 | Red Potion |
| 3 | Exkuranum |
| 4 | Dibenomum |
| 5 | Hemloch |

These positions override conflicting pins; other pins stay fixed. Missing potions do not shift the hotkeys. Correctly placed potions are protected from organizer moves and maintenance. Duplicate stacks are retained. Other sorted items fill bottom rows first, left to right. Sorting groups category, family, name, then original slot. Manual category overrides remain available in inventory item details.

## Read bank contents

At a visible NPC, use **Scan nearby**. You can also open **Withdraw Items** in the game. A complete response saves a snapshot. **Scan Bank** travels to a selected destination using your WorldLogs routes.

A correlated withdrawal-list request with no response for 10 seconds records an empty bank. Unrelated dialogs, malformed responses, movement and connection loss remain failures and preserve previous data.

## Keep, bank or clean out

Item rules apply to matching variants across characters:

- **Keep:** no maintenance action.
- **Auto-deposit:** included in **Deposit marked** and account-update deposit passes. There is no inactivity timer or inferred 30-day rule.
- **Junk:** included in **Junk cleanout**. Review and uncheck any stack you do not want dropped.

Ground drops can be lost. Equipment, pins and designated potion stacks are excluded. Bank junk is withdrawn one unit at a time and then dropped. A failed or unconfirmed drop stops further attempts for that item on that character. There is no automatic transfer/drop retry.

**Update selected accounts** logs into selected offline accounts one at a time, scans the nearest reachable bank, deposits marked items, requests safe logout and closes only its newly created client after success. Existing connected clients are skipped. A failed operation leaves its client open. **Stop**, manual game actions, movement, damage and connection errors interrupt operations. Travel continues when the game loses focus.

## Data and backups

- Profile: `%LOCALAPPDATA%\DAOrganizer\inventory.db`.
- Passwords: Windows Credential Manager entries beginning `DAOrganizer/da0.kru.com/`.
- `DAORGANIZER_DATA_DIR` can select a separate profile for development.
- Before copying a backup, exit the organizer and copy its profile folder, including any SQLite sidecar files. Credentials are separate.
- Removing a saved login removes the credential and hides the character; item history remains.

## Operational limits

- Windows is required for live client launch, input and credential storage.
- Only clients launched through the organizer are tracked.
- Only the exact supported executable is accepted.
- Bank contents are snapshots, not a live account-wide API. Bank gold is not tracked.
- Login completeness uses observed appearance/map/status/control messages and a quiet period; the server provides no inventory total.
- Transferability is unknown from these item packets. A rejected action is reported, not assumed successful.
- Routes describe observed map connections, not character permissions or every dynamic obstacle. Some destinations require manual approach.
- Background sound packets do not affect inventory tracking. Malformed inventory or equipment data still stops operations.
- Synthetic test success does not replace an in-game check on your own character and client version.

## Troubleshooting

Copy the exact message from **Results**, plus the app version and steps. Do not attach passwords, the account database, raw packet captures or game files. A “Tracking packet 0x…” message identifies the decoder involved; a manual-action message identifies the interfering input. Never repeatedly retry an unconfirmed transfer without checking the actual inventory first.
