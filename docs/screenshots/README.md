# Screenshot gallery

These are unedited renders of the actual Avalonia app using its fictional `--demo` collection. The headless gallery uses the same controls and styling as the desktop window. No personal accounts or game artwork appear here.

## Collection

![Collection](collection.png)

## Item ownership and rules

![Item details](item-details.png)

## Character inventory

![Inventory](inventory.png)

## Compact window

![Compact inventory](inventory-compact.png)

## Account manager

![Account manager](accounts.png)

## Empty search

![Empty search](empty-state.png)

## Regenerate

Build the solution, then run:

```powershell
dotnet tests/DAOrganizer.UiChecks/bin/Release/net10.0/DAOrganizer.UiChecks.dll artifacts/gallery --gallery
```

Review all six images before copying them into this directory. The script checks demo isolation and blocks live operations before capturing the gallery. Do not substitute screenshots from a personal profile.
