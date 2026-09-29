# Screenshot gallery

These are unedited renders of the actual Avalonia app using its fictional `--demo` collection. The headless gallery uses the same controls and styling as the desktop window. Names and quantities are fictional. Original game sprites are read from a local installation; their ownership remains with the game rights holders. No raw game assets are bundled.

## Collection

![Collection](collection-v0.15.0.png)

## Item ownership and rules

![Item details](item-details-v0.15.0.png)

## Character inventory

![Inventory](inventory-v0.15.0.png)

## Crystal arrows first

![Arrow and potion ordering](rogue-inventory-v0.15.0.png)

## Compact window

![Compact inventory](inventory-compact-v0.15.0.png)

## Account manager

![Account manager](accounts-v0.15.0.png)

## Empty search

![Empty search](empty-state-v0.15.0.png)

## Regenerate

Build the solution, then run:

```powershell
dotnet tests/DAOrganizer.UiChecks/bin/Release/net10.0/DAOrganizer.UiChecks.dll artifacts/gallery --gallery --require-sprites
```

Set `DAORGANIZER_GAME_DATA` to your installed game-data folder if it is outside the default location. `--require-sprites` fails if real art is unavailable; CI omits it and tests text fallbacks without game files.

Review all seven images before copying them into this directory. Use a new version suffix in screenshot filenames for every published update, and update the README links, so GitHub image caches cannot serve an older release. The script checks demo isolation and blocks live operations before capturing the gallery. Do not substitute screenshots from a personal profile.
