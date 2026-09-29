# Changelog

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
