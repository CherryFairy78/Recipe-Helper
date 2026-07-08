# Recipe Helper

Recipe Helper is a Dalamud plugin for planning multiple FFXIV recipes and tracking the combined materials you still need.

## Features

- Add multiple recipes to a shared plan and set an individual output quantity for each.
- Save, name, reload, update, duplicate, rename, and protected-delete recipe plans with their output quantities preserved.
- Select multiple saved plans with checkboxes, combine them into one workspace, or send their dependency-ordered queue directly to Artisan.
- Begin a clean plan automatically after saving, with the selected recipes and plan-name field reset.
- Keep a separate persistent saved-plan backup and restore from it if the main plugin configuration is ever reset.
- Find recipes that can be crafted from combined inventory, saddlebags, and saved retainer stock, including craftable intermediate ingredients, with maximum craft and output totals.
- Drag the divider beside the recipe search list to choose and retain a comfortable search-panel width.
- Search without a fixed result limit.
- Right-click inventory and marketboard items to search them directly in Recipe Helper.
- Filter the current results, select or deselect multiple recipes with checkboxes, and clear a search without clearing the selected plan.
- Resize saved-plan table columns to suit longer plan names or action controls.
- See an estimated total craft duration in the plan summary and adjust the average seconds per craft in Settings.
- Combine duplicate direct ingredients, raw materials, shards, crystals, and clusters.
- Show or hide already obtained raw materials, shards, crystals, and clusters with a remembered toggle.
- Compare requirements with NQ/HQ inventory, saddlebags, and saved retainer snapshots.
- Refresh quantities automatically as inventory changes.
- Show where owned materials were found and highlight completed requirements.
- Identify gatherable, fishing, vendor, craftable, and aetherial-reduction sources.
- Display live timers for timed gathering nodes.
- Open missing materials in a compact, availability-sorted travel overlay.
- Highlight overlay rows while their timed gathering node is currently active.
- Automatically resize and compact the overlay when its Materials section is expanded or collapsed.
- Hover the selected-recipe count in the overlay to see every recipe in the current plan.
- Send recipes to Artisan, lists to Teamcraft, and gathering targets to GatherBuddy.
- Queue missing craftable pre-crafts in dependency order, followed by every selected final recipe, when the combined stock can support the full Artisan queue.
- Customise interface, text, highlight, title-bar, and background colours.
- Apply optional transparency to the Missing Items Overlay without changing other windows.
- Keep capped daily operational logs for 30 days.

## Installation

1. Open Dalamud Settings in FFXIV.
2. Open **Experimental** and find **Custom Plugin Repositories**.
3. Add this repository URL:

   ```text
   https://raw.githubusercontent.com/CherryFairy78/Recipe-Helper/main/repo.json
   ```

4. Save the settings, open the Dalamud Plugin Installer, and install **Recipe Helper**.
5. Use `/recipehelper` to open it.

## Commands

Published builds use:

- `/recipehelper` or `/rchelp` — open Recipe Helper.
- `/rhoverlay` — toggle the Missing Items Overlay open or closed.

Plugins loaded through Dalamud's **Dev Plugins** screen use `/recipehelperdev`, `/rchelpdev`, and `/rhoverlaydev` instead, preventing command conflicts when development and published copies are loaded together.

Recipe Helper is distributed through a third-party custom repository and is not part of the official Dalamud plugin catalogue. It targets Dalamud API 15. Artisan and GatherBuddy are optional; their related buttons fall back or report when those plugins are unavailable.

## Retainer snapshots

Open each retainer at least once after installing Recipe Helper. The plugin stores that retainer's most recently observed inventory locally and refreshes it whenever the retainer is opened again.

Snapshots, saved-plan backups, and logs remain on the user's computer under the standard Dalamud plugin configuration directory.

## Building

Install the Dalamud plugin development environment, then run:

```powershell
dotnet build .\DalamudRecipeHelper.csproj
```

## Privacy

Recipe Helper does not upload inventory, character, or retainer data. Teamcraft links open in the system browser only after the user clicks the relevant button.

## Licence

Recipe Helper is available under the [MIT Licence](LICENSE).

Development history and implementation notes are maintained in [PROJECT_HISTORY.md](PROJECT_HISTORY.md).
