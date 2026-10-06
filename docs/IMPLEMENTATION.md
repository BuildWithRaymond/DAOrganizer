> Historical implementation note. Current behavior: [architecture map](architecture/overview.md) and [user guide](GETTING_STARTED.md). WorldLogs routes are now bundled.

> Historical implementation notes. Current behavior and setup are documented in [Getting started](GETTING_STARTED.md) and [Architecture](ARCHITECTURE.md).

# DAOrganizer

Approved design: Windows-only dark Avalonia app, Arbiter proxy, SQLite snapshots,
Windows-protected saved logins, manual Scan Bank with WorldLogs navigation,
inventory swaps, sorting, pinned slots and layouts. No unrelated client patches.

Implementation order:
1. Tests for snapshots, item conservation and directed routes.
2. Proxy observations and executable validation.
3. Dark desktop controls and persistence.
4. Banker capture, navigation and orderly slot operations.
5. Build, automated checks, UI rendering and packaged executable.

WorldLogs and game assets remain external read-only inputs. Excalibur is never
stopped or modified. Automatic travel must stop on uncertainty; incomplete bank
scans never replace complete bank contents.

Arbiter source pinned at 3b5af118bfc3310189b42879d2bab59205af9c88.
