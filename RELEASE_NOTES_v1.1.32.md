# Recipe Helper v1.1.32

## What's New

- Added Gwen's Dream crystal top-ups so long crafting runs can pause, refill crystals from retainers, and continue instead of timing out at the inventory cap.
- Fixed crystal withdrawals so capped retrieve amounts are tracked correctly and no longer leave Dream waiting on the original larger request.
- Added marketboard hover details to search results and selected recipe-plan rows, including quantity-scaled totals for finished items.
- Switched selected collectable recipe hovers to total scrip values instead of marketboard prices.
- Reworked hover pricing layouts to remove the awkward scroll area, show NQ and HQ world pricing side by side, and highlight unit prices more clearly.
- Tightened the `Craft all with Artisan` button so it only activates when every required ingredient is already in live inventory, and improved the disabled hover guidance for both Artisan and Gwen's Dream.
- Redacted local file-system paths and retainer names in the debug report so users can share logs more comfortably.

## Notes For Publish

- Published package path: `releases/DalamudRecipeHelper-v1.1.32.zip`
- Repo manifest target: `v1.1.32`
