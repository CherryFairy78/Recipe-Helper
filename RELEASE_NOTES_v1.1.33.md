# Recipe Helper v1.1.33

## What's New

- Fixed the Craft All progress tracker so a missed inventory event no longer makes Artisan queue one extra final craft at the end of a batch.
- Stopped the endless retry loop that could keep reopening the crafting window when the last queued craft no longer had enough materials to start.
- Added an immediately usable inventory view for Dream and Artisan planning so saddlebags are no longer treated as craft-ready materials.
- Updated Dream crystal top-up checks and Artisan live-inventory readiness checks to use the same immediate-use inventory rules.

## Notes For Publish

- Published package path: `releases/DalamudRecipeHelper-v1.1.33.zip`
- Repo manifest target: `v1.1.33`
