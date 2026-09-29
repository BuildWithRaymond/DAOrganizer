# Game sprites, flexible quick slots and nearby bank reads

## Requested behavior

- Render original sprites from the installed game. Public screenshots use fictional characters with actual sprite IDs and locally loaded game artwork. Do not distribute raw game assets or replace missing art with invented item drawings.
- Sorting packs one carried stack of Komadium, Red Potion, Exkuranum, Dibenomum and Hemloch from the left, skipping absent types. Carried Dual Crystal Arrows precede them regardless of character class.
- Trinkets use the factual names on [Vorlof's trinket page](https://vorlof.com/trinkets.html), plus observed short names for the combo scrolls. Place them from slot 12 leftward, then put overflow in the remaining inventory. Preserve every stack.
- Explicit pins take precedence and can always be removed. Potions are not automatically pinned. Dragging and saved layouts respect the user's choices instead of forcing quick-slot preferences.
- Bank refresh sends only the withdrawal-list request to a visible nearby NPC after login. No routes or walking are needed for a read. No visible NPC means stop with an actionable message and retain the previous snapshot.
- Deposits and withdrawals continue to travel to a bank. Automatic deposits during account updates trigger this travel only when marked, unpinned inventory items exist.

## Verification

Exercise missing potions, full inventories, duplicates, pins, arrow precedence, trinket overflow, manual potion movement. Replay an innkeeper bank read with no WorldLogs and ensure no movement or transfer packets are sent. Verify that a required deposit stops before sending a transfer when no bank route is available.

Render screenshots from the app with original sprites, inspect normal/compact layouts, and rerun the ownership-drawer scroll checks. Headless CI remains independent of a game installation and checks text fallbacks when art is unavailable.
