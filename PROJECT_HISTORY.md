# Recipe Helper Project History

This file is the durable hand-off record for Recipe Helper. Read it before making changes and update it whenever functionality, integrations, architecture, dependencies, known issues, or build instructions change.

## Project snapshot

- Last updated: 2026-06-28
- Plugin name: Recipe Helper
- Internal name: `DalamudRecipeHelper`
- Version: `1.1.0.0`
- Framework: Dalamud API 15
- Target: `.NET 10` on Windows x64
- Command: `/recipehelper`
- Build command: `dotnet build .\DalamudRecipeHelper.csproj --no-restore`
- Debug output: `bin\Debug\DalamudRecipeHelper.dll`
- Last verified build: 2026-06-28, succeeded with zero warnings and zero errors

## Purpose

Recipe Helper searches FFXIV recipes, calculates the materials required for a chosen output amount, compares those requirements with the character's available inventory, and connects crafting or gathering actions to specialist plugins.

## Current functionality

### Recipes and quantities

- Searches the FFXIV Lumina `Recipe` sheet by result name or item ID.
- Lets the user add multiple search results to one recipe plan.
- Lets the user set the desired number of finished items independently for every selected recipe and remove recipes from the plan.
- Saves named recipe plans in the standard plugin configuration, preserving selected recipes and individual output quantities across restarts.
- Saved plans can be loaded, overwritten by saving the same name, or deleted from the recipe window.
- Accounts for recipe yields when calculating the number of crafts.
- Combines duplicate direct ingredients and raw materials across all selected recipes.
- Aggregates shared direct craftable ingredients before expanding the raw-material plan so their recipe yields are applied to the combined quantity.
- Keeps Artisan and Teamcraft actions available for each selected recipe.
- Separates elemental shards, crystals, and clusters into a dedicated combined-requirements table instead of repeating them in the direct and raw-material tables.
- Plan summary, selected recipes, direct ingredients, raw materials, elemental catalysts, and raw-overlay materials can each be collapsed independently.
- Shows whether each missing craftable direct ingredient can be produced from the raw materials currently owned and provides an Artisan crafting button when it can.
- Recursively expands craftable ingredients into a combined raw-material list.
- Hovering a material name shows recipes that directly use it and the quantity consumed per craft.
- Material-usage tooltips are shared by the main tables and raw-material overlay and cap long lists at 18 entries.
- Material tables omit invalid zero-quantity rows and automatically hide optional columns when they contain no useful values.

### Inventory

- Scans normal inventory, crystals, chocobo saddlebags, premium saddlebags, and loaded retainer containers.
- Normalizes high-quality item IDs into their base item IDs.
- Preserves quality during scanning and displays separate NQ and HQ quantities; storage locations are combined without quality prefixes.
- Combines NQ and HQ quantities when determining whether enough material is owned.
- Displays the amount owned and the inventory or retainer name where the item was found.
- Treats the loaded retainer inventory as a whole rather than showing individual retainer page numbers.
- Saves a local inventory snapshot for every retainer after all of its inventory containers are loaded.
- Refreshes an open retainer snapshot after a short load delay and whenever its quantities change.
- Merges all saved retainer snapshots into recipe material totals without double-counting the currently open retainer.
- Displays each stored retainer's total quantity for an item in `Found in`.
- Stores snapshots in `Data\retainer-inventory.json` beneath the Dalamud plugin configuration directory.
- Highlights rows green when the required amount is available.
- Recalculates the selected recipe automatically after Dalamud reports an inventory change.
- Updates the raw-material overlay independently when the main recipe window is closed.

### Material sources

- Labels items as gatherable, fishing, vendor, craftable, or other.
- Fishing identification uses `FishParameter.Item.RowId`.
- Spearfishing identification uses `SpearfishingItem.Item.RowId`.
- Labels mapped aethersands, glioaethers, and special reduction rewards as `Aetherial reduction`.
- Maps each reduction result to one or more collectible source items.
- Selects the currently available or next available mapped ephemeral source and displays a live real-time countdown.
- Displays live availability countdowns for ordinary raw materials from unspoiled and other timed mining or botany nodes.
- Sends the selected collectible source to GatherBuddy for its location, marker, and teleport workflow.
- Fishing reduction timers display `Check GatherBuddy` because GatherBuddy's public interface does not expose its complete time, weather, bait, and intuition uptime calculation.

### Plugin integrations

- Artisan:
  - `Craft with Artisan` sends the selected recipe ID and calculated craft count through `Artisan.CraftItem` IPC.
  - `Craft all with Artisan` appears for multi-recipe plans when every combined direct ingredient is owned and dispatches each recipe sequentially after Artisan reports that the previous craft is no longer busy.
  - Artisan does not expose its recipe database or crafting-list contents through stable IPC, so Recipe Helper continues to use Lumina as the canonical recipe source.
- Teamcraft:
  - `Open in Teamcraft` creates a Teamcraft import URL from the selected result item and desired amount.
  - Teamcraft List Maker does not expose a public IPC API and is not required for this hand-off.
- GatherBuddy:
  - Normal gatherable items are sent through `/gather <item name>`.
  - Fish and spearfishing items are sent through `/gatherfish <item name>`.
  - GatherBuddy handles destination selection, map marker, nearest-aetheryte teleport, and gear changes.
  - If the GatherBuddy command is unavailable, Recipe Helper falls back to its built-in gathering-location window.

### Raw-material travel overlay

- `Missing Items Overlay` opens a separate compact panel for the selected recipe plan.
- It lists only missing raw materials that can be gathered.
- Each row includes the missing quantity, live timed-node availability where known, and a `Gather` button.
- Rows with currently active timed nodes use the configured sufficient-row green highlight until their node window closes.
- Materials are rendered with compact cell padding and the window automatically adjusts its height when the Materials section is expanded or collapsed.
- Hovering the selected-recipe count lists every recipe represented by the combined overlay requirements.
- Successful GatherBuddy hand-offs remain silent to keep the overlay tidy; errors are still shown.
- Aetherial-reduction rows send the preferred reducible collectible to GatherBuddy.
- Teleport and map behavior follows the user's GatherBuddy configuration.

### Built-in travel fallback

- Reads mining, botany, fishing, and spearfishing locations from Lumina sheets.
- Finds an unlocked aetheryte in the destination territory.
- Can start a normal in-game teleport.
- Attempts to open the Gathering Log map or place a manual map flag after teleporting.
- Normal gathering-node coordinates are not always directly available from Excel sheets; this was the reason the integration moved to GatherBuddy as the preferred route.

### Logging

- Daily logs are written to a dedicated `Logs` directory beneath the Dalamud plugin configuration directory.
- File names use `recipe-helper-YYYY-MM-DD.log`.
- Logs cover startup, shutdown, commands, searches, recipe calculations, source indexes, inventory scans, travel, GatherBuddy, Artisan, Teamcraft, warnings, and integration errors.
- Cleanup runs on startup and whenever the calendar day changes.
- Files older than 30 days are deleted, and no more than 30 daily files are retained.

### Settings

- The main window and Dalamud plugin configuration entry open a dedicated settings window.
- Users can customise the title bar, window background, main text, interface accent, sufficient-row, success, missing/error, warning, and `Ready to craft` button colours.
- Users can enable and adjust background opacity for the Missing Items Overlay independently of every other plugin window.
- Disabled and secondary text is derived from the chosen main text colour, allowing dark text to remain readable on white backgrounds.
- Title and background colours apply consistently to the main window, settings, raw-material overlay, child panels, and popups.
- Colour changes are applied live and saved automatically in the standard Dalamud plugin configuration.
- A reset button restores the original colour palette.

## Main files

- `MaterialUsageTooltip.cs` — shared recipe-usage tooltip rendering for material names.
- `WindowTheme.cs` — shared title-bar, background, child-panel, and popup styling.
- `RetainerSnapshotService.cs` — automatic retainer capture, persistent snapshot storage, and change notifications.

- `AetherialReductionService.cs` — reduction-result mappings, collectible selection, ephemeral schedules, and timer formatting.
- `RawMaterialsOverlayWindow.cs` — compact missing-raw-material travel and teleport controls.

- `Plugin.cs` — Dalamud services, startup, command registration, and dependency wiring.
- `RecipeWindow.cs` — search, recipe details, tables, controls, and travel popup.
- `RecipeService.cs` — recipe lookup, scaling, recursive material expansion, and source classification.
- `InventoryService.cs` — inventory and loaded-retainer scanning.
- `TravelService.cs` — built-in location lookup, teleport, and map fallback.
- `PluginIntegrationService.cs` — Artisan, Teamcraft, and GatherBuddy hand-offs.
- `FileLogService.cs` — daily file logs and 30-day retention.
- `Configuration.cs` — persisted user colour preferences and defaults.
- `SettingsWindow.cs` — colour pickers and reset controls.
- `RecipeModels.cs` — shared records used by the services and UI.

## Important design decisions

1. Lumina remains the recipe-data authority because Artisan and Teamcraft List Maker do not provide stable APIs for retrieving complete recipe data.
2. GatherBuddy is preferred for gathering travel because it already owns a richer location database and the complete marker/teleport workflow.
3. Integrations use public IPC or registered commands rather than directly referencing another plugin's internal assemblies or configuration files.
4. Built-in travel is retained as a fallback so the Gather button still has a useful response when GatherBuddy is disabled.
5. Logs contain operational details and item/recipe identifiers, but inventory logs record counts rather than every inventory item.
6. Aetherial-reduction mappings are stored locally so recipe planning does not depend on web requests while the game is running. Review them when new reduction rewards are introduced.
7. Retainer snapshots are keyed by the game's unique retainer ID and stored separately from plugin preferences. A snapshot is only replaced when every tracked retainer container reports as loaded.
8. Snapshot totals are filtered to the current character's content ID so retainers from different characters are never combined.
9. Live inventory updates use Dalamud's batched inventory-change event instead of continuous polling.

## Known constraints

- A retainer must be opened at least once before Recipe Helper can store and include it.
- Stored quantities represent the last time each retainer was opened and may be stale until it is opened again.
- A plugin command being dispatched confirms that GatherBuddy received it, but GatherBuddy remains responsible for reporting an unknown item or unavailable destination.
- Artisan must be loaded and idle enough to accept the crafting request.
- Teamcraft opening depends on the system browser being available.
- Built-in map flags remain less reliable than GatherBuddy for gathering nodes whose exact coordinates live outside standard Excel rows.
- Public source repository: `https://github.com/CherryFairy78/Recipe-Helper`.
- Custom Dalamud repository URL: `https://raw.githubusercontent.com/CherryFairy78/Recipe-Helper/main/repo.json`.
- The project is licensed under the MIT Licence.
- Before sharing a release broadly, personally test the packaged build in game and clearly disclose substantial AI-assisted development where the repository or community requires it.

## Change timeline

### 2026-06-27

- Created the recipe search and material comparison workflow.
- Added inventory discovery and a `Found in` column.
- Replaced retainer-page labels with the active retainer name.
- Removed unnecessary inventory numbering.
- Added recursive raw-material calculations.
- Added green sufficient-quantity highlighting.
- Added adjustable recipe output quantities.
- Added gatherable, vendor, craftable, fishing, and other source labels.
- Added built-in gathering destination, teleport, and map support.
- Guarded invalid Lumina `RowRef` access in aetheryte lookup.
- Investigated repeated map/flag failures using Broccoli as a test item.
- Added Gathering Log map fallback after teleport.
- Integrated Artisan crafting, direct Teamcraft list imports, and GatherBuddy travel.
- Added separate GatherBuddy fishing travel.
- Corrected fishing classification to use the actual item references rather than fish-sheet row IDs.
- Added daily core-function logs with 30-day retention.
- Established this project-history and maintenance convention.
- Verification: the latest plugin build succeeded with zero warnings and zero errors.
- Added separate NQ and HQ inventory quantities and labelled locations while retaining combined material sufficiency checks.
- Verification after the NQ/HQ change: build succeeded with zero warnings and zero errors.
- Added a `From raw` status for direct ingredients. `Ready` means the missing intermediate quantity can be crafted from the combined NQ/HQ raw materials currently owned.
- Verification after the `From raw` change: build succeeded with zero warnings and zero errors.
- Replaced the direct-ingredient `Ready` label with a `Ready to craft` button that sends the intermediate recipe ID and required craft count to Artisan.
- Verification after the Artisan intermediate-crafting button change: build succeeded with zero warnings and zero errors.
- Narrowed the recipe-results pane and assigned compact fixed widths to all material columns, with horizontal table scrolling for smaller windows.
- Verification after the column-layout change: build succeeded with zero warnings and zero errors.
- Fixed the scrolling tables so the direct-ingredients table no longer consumes all remaining height; both direct and raw-material panels now have bounded heights and independent scrolling.
- Verification after restoring the raw-material panel: build succeeded with zero warnings and zero errors.
- Removed the material tables' vertical scrollbars; tables now expand to their full row count while retaining horizontal column scrolling.
- Verification after removing the table scrollbars: build succeeded with zero warnings and zero errors.
- Direct ingredients no longer show a numeric missing value when the missing quantity is fully craftable from owned raw materials.
- Verification after the direct-ingredient missing-value change: build succeeded with zero warnings and zero errors.
- Added a saved colour-settings window with live colour pickers and default reset controls.
- Verification after adding colour settings: build succeeded with zero warnings and zero errors.
- Redesigned the main interface with a modern search toolbar, rounded controls and panels, accent-driven interaction states, a compact recipe list, summary metrics, stronger section headings, and simplified horizontal table borders.
- Verification after the modern interface redesign: build succeeded with zero warnings and zero errors.
- Added aetherial-reduction source recognition, GatherBuddy hand-off using the reducible collectible, and an `Available` timer column driven by Lumina ephemeral-node schedules.
- Verified the timer mapping against installed game data, including Electrocoal's 20:00-00:00 Eorzea window.
- Verification after the aetherial-reduction feature: build succeeded with zero warnings and zero errors.
- Added a compact 760×540 first-open layout, reduced the minimum size to 460×320, narrowed the recipe pane and material columns, and tightened control, panel, row, and cell spacing.
- Verification after the compact-layout change: build succeeded with zero warnings and zero errors.
- Extended the `Available` column to raw materials from unspoiled and other timed gathering nodes using cached Lumina node schedules.
- Included transient `GatheringItemPoint` associations when resolving timed nodes, matching items that are not directly listed on the base gathering point.
- Verification after adding raw-material node timers: build succeeded with zero warnings and zero errors.
- Removed the NQ and HQ prefixes from `Found in` and de-duplicated locations across both qualities.
- Verification after simplifying `Found in`: build succeeded with zero warnings and zero errors.
- Removed `OK` from the `Missing` column when an item quantity is fully owned; the sufficient-row highlight remains.
- Verification after removing the `OK` label: build succeeded with zero warnings and zero errors.
- Added a compact raw-material travel overlay with missing quantities, availability timers, reduction-source handling, and GatherBuddy teleport buttons.
- Verification after adding the raw-material overlay: build succeeded with zero warnings and zero errors.

### 2026-06-28

- Added persistent per-retainer inventory snapshots.
- Added automatic capture after all seven retainer pages and the retainer crystal container are loaded.
- Added one-second change polling while the retainer remains open so transfers are retained.
- Merged saved retainers into NQ/HQ material totals without scanning stale live retainer containers.
- Added per-retainer item quantities to `Found in`.
- Added automatic recipe refresh when a retainer snapshot changes.
- Stored snapshot data atomically in the plugin configuration `Data\retainer-inventory.json` file.
- Scoped stored retainers to the current character while retaining other characters' snapshots for later use.
- Verification after adding retainer snapshots: build succeeded with zero warnings and zero errors.
- Verification after character-scoping retainer snapshots: build succeeded with zero warnings and zero errors.
- Added saved title-bar and window-background colour pickers and applied the shared theme to every plugin window, child panel, and popup.
- Verification after adding title and background colours: build succeeded with zero warnings and zero errors.
- Added a saved main-text colour option with automatically derived secondary text for light or white backgrounds.
- Verification after adding text colours: build succeeded with zero warnings and zero errors.
- Added event-driven automatic ingredient updates for item additions, removals, moves, merges, splits, and quantity changes.
- Added independent overlay quantity refresh so missing raw-material rows update while the main window is closed.
- Verification after adding automatic ingredient updates: build succeeded with zero warnings and zero errors.
- Tidied the raw-material overlay with stable column widths, centred travel buttons, consistent row heights, material-name tooltips, and the same accent-coloured table header as the main window.
- Verification after tidying the overlay: build succeeded with zero warnings and zero errors.
- Enabled drag-resizing for every raw-material overlay column while retaining the tidy default widths.
- Verification after enabling adjustable overlay columns: build succeeded with zero warnings and zero errors.
- Classified elemental shards, crystals, and clusters as always available so timed-node associations never produce a misleading availability countdown.
- Verification after removing elemental-item timers: build succeeded with zero warnings and zero errors.
- Added cached material-to-recipe indexing and shared hover tooltips in the main tables and raw-material overlay.
- Verification after adding material recipe tooltips: build succeeded with zero warnings and zero errors.
- Changed gathering availability timers to use fractional Eorzea time and display live hours, minutes, and seconds.
- Verification after adding timer seconds: build succeeded with zero warnings and zero errors.
- Made the direct-ingredient and raw-material tables data-aware: invalid empty rows are removed, while empty optional columns are hidden and the core ingredient, source, and required-quantity columns remain.
- Verification after removing empty material rows and columns: build succeeded with zero warnings and zero errors.
- Removed the remaining visual gaps after the final visible column and below the final material row by sizing each table to its active columns and allowing its height to fit its rendered rows naturally.
- Verification after correcting material-table sizing: build succeeded with zero warnings and zero errors.
- Fixed a regression where the automatically sized direct-ingredients table consumed the remaining details panel and hid the raw-material list. Tables now use an exact bounded height for their header, rendered rows, and horizontal scrollbar only when required.
- Verification after restoring the raw-material list: build succeeded with zero warnings and zero errors.
- Kept the `Missing` column visible in both material tables even when every required quantity is owned; sufficient-material cells remain blank.
- Verification after making the `Missing` column permanent: build succeeded with zero warnings and zero errors.
- Widened the permanent `Missing` column and updated the calculated table width so its header and values are not squeezed or clipped at the right edge.
- Verification after correcting the `Missing` column width: build succeeded with zero warnings and zero errors.
- Moved the `Need` and `Missing` columns directly after `Source` so they remain visible in compact raw-material tables before optional travel, timer, quality, and location columns.
- Verification after repositioning the `Missing` column: build succeeded with zero warnings and zero errors.
- Made both material tables responsive to the available details-panel width. Resizing the plugin window now resizes the tables, the final visible column fills unused width, individual columns remain draggable, and compact layouts retain horizontal scrolling.
- Verification after making the material tables adjustable: build succeeded with zero warnings and zero errors.
- Added multi-recipe planning. Search results can be added to a shared plan, each selected recipe has its own editable output quantity and remove, Artisan, and Teamcraft controls, and duplicate direct/raw requirements are combined into unified material tables and the travel overlay.
- Shared direct craftable ingredients are aggregated before raw expansion so their yields are calculated against the combined requirement.
- Verification after adding multi-recipe selection and combined planning: build succeeded with zero warnings and zero errors.
- Changed raw-overlay resizing so the Material column keeps a stable width and the Travel column absorbs changes to the overlay width.
- Verification after changing the flexible overlay column: build succeeded with zero warnings and zero errors.
- Added a dedicated `Shards, Crystals & Clusters` table using the stable FFXIV elemental item-ID range and removed those items from the direct and general raw-material tables.
- Added independently collapsible sections for the plan summary, selected recipes, direct ingredients, raw materials, elemental catalysts, and missing-items overlay.
- Verification after separating elemental catalysts and adding collapsible tables: build succeeded with zero warnings and zero errors.
- Renamed the `Raw travel overlay` button, window, heading, and documentation to `Missing Items Overlay`.
- Verification after renaming the overlay: build succeeded with zero warnings and zero errors.
- Removed automatic plan selection when a search returns one recipe. Search results now display a `Click to add` prompt and are only added after an explicit click.
- Verification after making recipe addition click-only: build succeeded with zero warnings and zero errors.
- Applied the configured accent colour to shared collapsible-header normal, hovered, and active states, so the overlay `Materials` header matches the main window.
- Verification after synchronising overlay header colours: build succeeded with zero warnings and zero errors.
- Cleared the recipe search text and result list after a recipe is explicitly added to the selected plan.
- Verification after adding post-selection search clearing: build succeeded with zero warnings and zero errors.
- Sorted missing-items overlay rows by live timed-node availability: active nodes appear first, followed by upcoming nodes from soonest to latest, then always-available and GatherBuddy-only entries; equal times are ordered by material name.
- Verification after adding availability ordering to the overlay: build succeeded with zero warnings and zero errors.
- Completed an initial publication-readiness audit. The API 15 Release build and packaged ZIP succeeded with zero warnings and zero errors, and the 500×500 icon meets the official size requirements. Publication remains pending a public source repository/commit, confirmed repository metadata, a licence, refreshed user documentation, personal in-game release testing, and the required AI-use disclosure.
- Prepared the first public GitHub release: changed the version to `1.0.0.0`, set the repository URL to `CherryFairy78/Recipe-Helper`, added an MIT licence and source-control exclusions, replaced the starter README with public installation/features/privacy documentation, and refreshed installer metadata.
- Verification for GitHub release v1.0.0: clean API 15 Release packaging succeeded in `artifacts\Release` with zero warnings and zero errors.
- Styled the Missing Items Overlay `Teleport` buttons with the same configured accent, hover, and active colours as the main window.
- Verification after synchronising overlay Teleport button colours: build succeeded with zero warnings and zero errors.
- Added active-node green highlighting to the Missing Items Overlay and kept it live until each timed window ends.
- Removed the duplicated overlay heading and successful `GatherBuddy is finding...` status text, retained actionable errors, reduced table cell padding, and made the overlay height follow the expanded/collapsed Materials section.
- Added a hover tooltip to the overlay's selected-recipe count listing every recipe in the combined plan.
- Added an overlay-only transparency toggle and opacity slider in Settings.
- Added a conditional `Craft all with Artisan` button and a sequential queue driven by Artisan's public `CraftItem` and `IsBusy` IPC endpoints.
- Assessed Raphael integration and deferred HQ prediction because neither Raphael nor Artisan exposes a supported IPC result containing Raphael's simulated HQ outcome.
- Verification after the overlay, transparency, tooltip, and Craft All changes: build succeeded with zero warnings and zero errors.
- Renamed the Missing Items Overlay action from `Teleport` to `Gather`.
- Reworked overlay auto-height using the actual title, window padding, frame, header, row, and message heights; removed the previous 650-pixel maximum so the expanded Materials table can fit all rendered rows.
- Added a `Raphael` action to each selected recipe. It opens `raphael-xiv.com` and copies the exact recipe name because Raphael currently has no stable URL import or Dalamud IPC for preloading recipes, character stats, or HQ outcomes.
- Verification after the Gather label, auto-height correction, and Raphael hand-off: build succeeded with zero warnings and zero errors.
- Added a GitHub-hosted custom Dalamud repository manifest at `repo.json`, pointing installation and update downloads to the v1.0.0 GitHub Release asset and using the repository icon.
- Replaced manual ZIP installation instructions with the custom-repository URL workflow; end users can now install and update Recipe Helper through Dalamud's Plugin Installer without handling the ZIP directly.
- Corrected the public author and copyright name from `Meghan` to `Meghann` in the project, plugin, custom repository, and MIT licence metadata.
- Removed the standalone Raphael website button and hand-off because Raphael solver functionality is already available through Artisan; restored the selected-recipe Actions column to its compact width and removed Raphael from current public metadata.
- Started the v1.1.0 update with persistent named recipe plans.
- Added save, load, overwrite, and delete controls; each saved plan preserves recipe IDs, result details, and requested output quantities in the standard Dalamud plugin configuration.
- Verification after adding named recipe plans: build succeeded with zero warnings and zero errors.
- Limited saved-plan save, update, load, delete, and validation messages to three seconds so confirmations do not remain in the recipe window.
- Verification after adding timed plan messages: build succeeded with zero warnings and zero errors.
- Added published-build shortcuts `/recipehelper` and `/rchelp`, plus `/rhoverlay` to toggle the Missing Items Overlay.
- Plugins loaded through Dalamud's Dev Plugins screen now register `/recipehelperdev`, `/rchelpdev`, and `/rhoverlaydev` instead, preventing command registration conflicts when development and published copies are loaded together. This uses Dalamud's runtime `IsDev` status rather than the DLL build configuration.
- Verification after switching command selection to Dalamud's runtime development status: Debug and Release builds both succeeded with zero warnings and zero errors.
- Renamed the selected-recipe `Artisan` action button to `Craft Items` while retaining the same Artisan crafting hand-off.
- Verification after renaming the Artisan action: build succeeded with zero warnings and zero errors.
- Published Recipe Helper v1.1.0 on GitHub with persistent named recipe plans, brief plan-status messages, separate published/development commands, and the renamed `Craft Items` action.
- Verified the clean API 15 Release package with zero warnings and zero errors, confirmed its SHA-256 digest (`0913AE191105CBEF9315E0DE909E0322E754824BD00CBB7FFF5E6E1F62CA3DC4`), and updated the public custom-repository manifest to install and update from the v1.1.0 release asset.
- Started the v1.1.1 patch and required Ctrl to be held while clicking a saved plan's `Delete` button; an explanatory message and hover tooltip now make the safeguard visible.
- Added the public icon URL to the plugin's packaged manifest. The custom repository already supplied the URL, but Dalamud's installed manifest was taking the ZIP metadata where `IconUrl` was previously absent.
- Verification for the saved-plan safeguard and icon fix: Debug and clean Release builds both succeeded with zero warnings and zero errors, and both generated manifests contain the v1.1.1 version and public icon URL.
- Published Recipe Helper v1.1.1 on GitHub and marked it as the latest release.
- Verified the release asset and custom-repository update path; the v1.1.1 ZIP SHA-256 digest is `2210851FEEBFCB3CB75518041BCA1305D10773F0A25B49B8494299042C03701F`.

## Continuation checklist

When returning to this project:

1. Read this file and `README.md`.
2. Confirm the installed Dalamud API and the public IPC/command contracts of integrated plugins.
3. Make the requested change without directly coupling to another plugin's internal assemblies.
4. Run `dotnet build .\DalamudRecipeHelper.csproj --no-restore`.
5. Confirm the build has zero errors and review any warnings.
6. Add a dated entry to this history describing the change, decisions, limitations, and verification result.
