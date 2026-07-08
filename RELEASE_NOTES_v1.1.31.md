# Recipe Helper v1.1.31

## What's New

- Fixed Gwen's Dream so large retainer withdrawals can continue across multiple stacks instead of timing out when the first stack is smaller than the full request.
- Fixed Gwen's Dream withdraw completion checks so Recipe Helper only advances after a real item transfer has actually been issued and observed.
- Updated the main window stop-state message after a manual Artisan stop and removed the old manual `Artisan popup` button now that the progress window opens automatically.
- Restored `Craft all with Artisan` visibility when Recipe Helper can build the dependency queue from available raw materials and pre-crafts.
- Added a clearer GitHub README note explaining that Recipe Helper works best with Artisan, GatherBuddy, and optionally Auto-Retainer for automatic retainer withdrawals.

## Notes For Publish

- Published package path: `releases/DalamudRecipeHelper-v1.1.31.zip`
- Repo manifest target: `v1.1.31`
