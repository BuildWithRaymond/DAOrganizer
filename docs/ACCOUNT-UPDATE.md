> Historical implementation notes. Current behavior and setup are documented in [Getting started](GETTING_STARTED.md) and [Architecture](ARCHITECTURE.md).

# Account update extension

Keep the existing app, SQLite database and Arbiter transport. User approved one pass per click.

- Match bank/storage map names; exclude inns and weapon storerooms.
- Inventory grid becomes 12 columns by 5 rows, retaining slots 1–59 and gold at 60.
- All accounts view combines stored inventories, banks and optional equipment; show owner, location, category and freshness. Character checkboxes persist display selection without deleting snapshots.
- Account manager saves names/passwords through the existing Windows credential vault. Display selection and update-queue selection are separate. Allow editing/removing saved logins while retaining item history.
- Update selected accounts runs alphabetically, sequentially, once. Skip characters already connected through organizer. Each new owned client logs in, waits for a complete inventory, travels to the nearest known reachable bank/storage using estimated walking distance, scans, logs out and closes its own window. Stop on failure/cancel; never close unrelated game clients. Failed scans retain previous bank data and leave the owned client open. Successful scans and an explicit organizer Stop request still attempt safe logout and close. Packet walking continues without foreground focus; Escape/arrow key states apply only while the owned game has focus.
- Infer categories from item names and observed equipment slots; manual categories override inference. Show categories so the user can review/change them. Unknown items remain Other.
- Keep current application and client processes running during development; publish this update to a separate version folder. No database schema destruction or credential export.

Checks: bank-name classification, closest reachable route, combined selection/persistence, distinct category sorting, queue ordering/cancellation/failure cleanup, logout acknowledgement, and UI renders at normal/minimum sizes.
