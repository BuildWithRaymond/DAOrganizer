# Changelog

## 0.16.0 — Ready to organize

- Added self-contained Windows installer and update-capable portable ZIP with optional stable GitHub updates. Automatic downloads and installation on safe exit are off by default; manual controls are available in Settings. Active operations and open organizer-launched clients block installation.
- Bundled 1,432 WorldLogs maps as route-only data. Fresh profiles need no route setup; unavailable old paths fall back to included routes, with a custom-folder override retained.
- Added reviewed consolidation to one destination bank, sequential source handling, protected quantities and protocol-confirmed custody/recovery. Same-account handoff remains unsupported.
- Included accumulated exchange, withdrawal, delivery confirmation and recovery fixes, plus boundary-portal and route-selection regressions.
- Restored original DAOrganizer launch artwork, refreshed screenshots and setup documentation, and trimmed development material from user packages.
- Profiles and saved logins retain their existing locations. Older ZIPs need one manual upgrade to gain updater support. Releases remain unsigned.


## 0.15.0 — Game sprites and flexible quick slots

- Restored original game sprites in demo mode and replaced the public screenshots. Missing artwork shows item names; invented item drawings have been removed.
- Available quick potions compact from slot 1 in priority order. Carried Dual Crystal Arrows take slot 1 first, regardless of class.
- Vorlof-referenced trinkets fill row one from slot 12 leftward. Overflow and duplicate stacks are preserved.
- Removed forced potion locks from drag, sorting, saved layouts and packet actions. Any slot can be pinned or unpinned; explicit pins override sorting preferences.
- Account updates read banks through the nearest visible NPC after login without routes or walking. Marked deposits and withdrawals still travel to a bank.
- Added regression coverage for compact sorting, pins, arrows, trinkets, manual potion movement and inn bank reads without WorldLogs.

## 0.14.0 — The Celtic Collection

First public source release.

### Added

- Black-and-gold desktop design with original Celtic vector ornaments, serif headings and illustrated item fallbacks.
- Category grids with family labels, combined quantity badges and an ownership drawer.
- A fictional, in-memory `--demo` collection with live operations and credential writes disabled.
- Reproducible screenshots rendered from the actual app, including compact and empty states.
- MIT licensing, contribution and security guidance, architecture documentation, issue templates and Windows CI.
- A self-contained Windows x64 package with dependency notices and user documentation.

### Improved

- Character navigation, action hierarchy, responsive toolbar wrapping and protected potion-slot labels.
- Item families stay adjacent inside category sections.
- Portable packaging discovers the restored .NET runtime instead of depending on a machine-specific version.

### Included from local builds

- Clicking an item preserves the collection's scroll position through drawer reflow and rule changes.
- Inventory tracking ignores unrelated short `0x19` sound messages.
- Explicit Keep, Junk and Auto-deposit rules with previews and single-attempt cleanup.
- Packet-confirmed bank actions and operation guards that distinguish expected dialog transitions.
- Background travel, nation-prompt handling and account inventory/bank refresh.
- Fixed potion slots: Komadium, Red Potion, Exkuranum, Dibenomum and Hemloch.

Automated verification uses synthetic data and packets. This release does not claim new live-server validation of every game action.
