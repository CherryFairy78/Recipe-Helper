# Recipe Helper Project History

This file is the durable hand-off record for Recipe Helper. Read it before making changes and update it whenever functionality, integrations, architecture, dependencies, known issues, or build instructions change.

## Project snapshot

- Last updated: 2026-07-13
- Plugin name: Recipe Helper
- Internal name: `DalamudRecipeHelper`
- Version: `1.1.46.0`
- Framework: Dalamud API 15
- Target: `.NET 10` on Windows x64
- Command: `/recipehelper`
- Build command: `dotnet build .\DalamudRecipeHelper.csproj --no-restore`
- Debug output: `bin\Debug\DalamudRecipeHelper.dll`
- Last verified build: 2026-07-13, Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.46.zip` was created from the verified Release output with SHA-256 `9795E6E14873CB840795D1FD18D82EF124B25C95FC68FD1A4FD03ED5416A3741`.

## Recent release

- Version: `1.1.46.0`
- Added a stable Can Craft column to Direct Ingredients, with enabled actions for available pre-crafts and disabled actions that name the missing live-inventory material.
- Direct pre-craft checks now include available intermediate materials, matching Craftable Now behaviour.
- Always-available gathering rows now display Always Up instead of a dash.
- Updated the package metadata and custom repository feed for `v1.1.46`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.46.zip` was created from the verified Release output with SHA-256 `9795E6E14873CB840795D1FD18D82EF124B25C95FC68FD1A4FD03ED5416A3741`.

## Previous release

- Version: `1.1.45.0`
- Fully pre-crafted raw materials are now omitted from the Missing Items Overlay.
- The main Raw Materials section continues to show those rows with their pre-craft coverage state and explanatory hover.
- Updated the package metadata and custom repository feed for `v1.1.45`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.45.zip` was created from the verified Release output with SHA-256 `7C31BBB9580BFCA70CE8E89A50708653A9AC2C6A25D3AAB000A16D4C0B7FA02C`.

## Earlier release

- Version: `1.1.44.0`
- Restored the full raw-material recipe tree while separately identifying branches covered by owned pre-crafts.
- Fully covered raw rows are green, labelled Pre-crafted, and explain the covering pre-craft in their hover.
- Applied the same distinction to the Missing Items Overlay.
- Updated the package metadata and custom repository feed for `v1.1.44`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.44.zip` was created from the verified Release output with SHA-256 `4F7635005509B6B254AE5DB0A858819CA0B31476F30F589F5E51C1D5AA193F66`.

## Earlier release

- Version: `1.1.43.0`
- Raw-material planning now consumes owned pre-crafts before expanding their ingredients, so materials underneath already-owned intermediates are no longer shown as missing.
- Applies to individual recipes, combined recipe plans, saved plans, and the Missing Items Overlay.
- Updated the package metadata and custom repository feed for `v1.1.43`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.43.zip` was created from the verified Release output with SHA-256 `64019BFF0F53FA7DD564696C46D9689E3709614C8EAB280E6C5C78560D2A7AF5`.

## Earlier release

- Version: `1.1.42.0`
- Added a log-status filter for items obtained or not obtained in Gathering, Fishing, Spearfishing, and confirmed Crafting Logs.
- Cosmic Exploration detection now covers all WKS items, suppresses normal gathering-log rows for those items, and collapses duplicate log labels for other items.
- Excluded Cosmic Exploration, quest, society quest, special-tool, and ephemeral-node items from Log Status filtering.
- Updated the package metadata and custom repository feed for `v1.1.42`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.42.zip` was created from the verified Release output with SHA-256 `271937F166E499EFFFC2CE8D0FC46D303630E6921FA96A2C97E1411505731234`.

## Earlier release

- Version: `1.1.41.0`
- Folder Rename now gives each dialog its own input state, preserves the selected folder's parent, and supports slashes in renamed subfolder labels without creating extra levels.
- Expanded the indented folder picker so more parent and subfolder choices are visible at once.
- Folklore hovers now use the actual tome item name and prefer current Purple Gatherers' Scrip exchanges over obsolete Regional Folklore Trader's Token costs.
- Corrected folklore special-shop shard placeholders to show the actual Purple Gatherers' Scrip cost and Fieldcraft Items, Folklore Items exchange.
- Hardened folklore special-shop cost parsing against empty generated rows.
- Added an FSH-only fish-type filter for Regular Fish, Big Fish, Spearfishing, Ocean Fishing, and other GatherBuddy fish categories.
- Added a crafting-job-only Master Recipe book filter populated from each job's recipe unlocks.
- Added a MIN/BTN/FSH Folklore Book filter, alongside the FSH Fish Type filter.
- Updated the package metadata and custom repository feed for `v1.1.41`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.41.zip` was created from the verified Release output with SHA-256 `B14A017840EDE117D86326C166A66B3E2850C5DC1F509C21D76DA38278969EC7`.

## Earlier release

- Version: `1.1.39.0`
- Rounded action buttons locally and aligned the Missing Items and Artisan overlay section and table headers with the main folder rounding.
- Rounded the tooltip window background itself so the square corners no longer show behind its accent outline.
- Updated plan labels and saved-folder hover headings to use the brighter interface accent colour for readability.
- Kept Save new plan and Update plan visible in every plan section. Scoped updates now preserve the other saved-plan sections.
- Updated the package metadata and custom repository feed for `v1.1.39`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.39.zip` was created from the verified Release output with SHA-256 `72BA780D86B1AE43F9B011317AA3DDECBCB76C4693DF0FD12A9EAD5EEB2BEDEC`.

## Historical release

- Version: `1.1.38.0`
- Restored shared title-bar and button spacing by removing the tooltip window-level rounding override.
- Rounded the main window's collapsible sections, saved-plan folders, and table-header cards. Matching table-header cards in the Missing Items and Artisan overlays are rounded too.
- Added independent Direct Ingredients and Raw Materials saved-plan types. They store item snapshots rather than recipe links and load, update, duplicate, import/export, combine, and feed the overlay independently of recipes.
- Saved-plan parent-folder hovers list immediate subfolders, while leaf-folder hovers list their direct recipes.
- Long leaf-folder recipe lists now split into two balanced columns in their hover.
- Transparent Missing Items Overlay mode now uses dark, lightly tinted Missing and Available/timer cards for stronger text contrast.
- Darkened Missing amount text slightly and restored transparent backgrounds for non-timed Always Up availability cards.
- Updated the package metadata and custom repository feed for `v1.1.38`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.38.zip` was created from the verified Release output with SHA-256 `523955054F1CFE1B8E71D9D52A1C0CE523346E948494DE1FD5F2220BE951D793`.

## Historical release

- Version: `1.1.37.0`
- Added aetherial-reduction result items to supplemental search, so materials such as Levinchrome Aethersand can be found and added from search.
- Added Aetherial Reduction hover details listing the material sources that can produce each result.
- Extended saved plans, duplicate, import/export, and loading flows to preserve standalone gatherables and collectables alongside recipes.
- Added a Ctrl-protected folder delete action that removes folders and subfolders while moving contained plans to Unfiled.
- Added scoped Recipe Plan, Gatherable Plan, and Collectable Plan save controls with separate names and folder destinations.
- Removed the oversized selected-recipe summary strip and broadened Crafting Log checks to recognize any completed recipe variant for the same result item.
- Changed Crafting Log hovers to show only confirmed completions, avoiding false "Not yet crafted" results from incomplete client history; Master Recipe entries are excluded because FFXIV does not record their completion state.
- Added a thin accent-colour border to item and collectible information hovers, including the overlay.
- Loading saved plans now closes their containing folders, and individual or saved-plan Artisan craft actions are disabled with an explanation until their required materials are in live inventory.
- Added a Clear selected action that clears saved-plan checkmarks and the current recipe plan without affecting the saved plans themselves.
- Saved Plans, Gatherables, and Collectables now start collapsed when Recipe Helper first opens, while relevant gathering sections still open automatically when items are added.
- Plan controls now separate Save new plan from Update plan; updating refreshes the loaded plan's recipes, gatherables, and collectables while retaining its existing name and folder. Saved plan rows also retain their direct Update action.
- Rounded item and collectible hover panels and their accent outlines to match the plugin's softer layout styling.
- Updated the package metadata and custom repository feed for `v1.1.37`.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.37.zip` was created from the verified Release output with SHA-256 `7DE27F89AD003FC20547AE4B084497BB7FF48779CC59A485134074B29A13E05B`.

## Historical release

- Version: `1.1.36.0`
- Expanded source hovers with folklore and master-recipe unlocks, required tools, marketboard availability, Restoration and society-quest details, and matching overlay coverage.
- Added fishing bait, fish type, zones, spots, availability, quest-fish fallbacks, cosmic exploration details, and quest locations.
- Added live Fishing, Gathering, Spearfishing, and Crafting Log progress to relevant hovers.
- Added integration status and GitHub links for Artisan, GatherBuddy, Lifestream, and Auto-Retainer in Settings.
- Updated the live repo manifest metadata and release package target to `v1.1.36` for publish readiness.
- Verification: Debug and Release builds succeeded with zero warnings and zero errors. `releases\DalamudRecipeHelper-v1.1.36.zip` was created from the verified Release output with SHA-256 `9022F3D89BF77DAE4EC9DC1DAAE1F39CA4EAC1F1B6773E50BD81B2EB4C337AAE`.

## Purpose

Recipe Helper searches FFXIV recipes, calculates the materials required for a chosen output amount, compares those requirements with the character's available inventory, and connects crafting or gathering actions to specialist plugins.

## Current functionality

### Recipes and quantities

- Searches the FFXIV Lumina `Recipe` sheet by result name or item ID.
- Adds a right-click `Search in Recipe Helper` action to inventory and marketboard item menus so item lookups can start from the game UI.
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
- Fish tooltips show GatherBuddy's bait and gameplay type (regular, big, spearfishing, or ocean fishing) when available, plus the game's associated best zone and fishing spot.
- Items without a marketboard category no longer request or display a Universalis marketboard snapshot.
- Non-marketboard item hovers can show the society quests that request the item.
- Cosmic Exploration fish hovers show the associated mission and GatherBuddy's required bait.
- Collectible reward hovers use the same fishing and Cosmic Exploration detail sections as regular item hovers.
- Quest-item hovers show the linked quest name and location alongside existing fishing details.
- Labels mapped aethersands, glioaethers, and special reduction rewards as `Aetherial reduction`.
- Maps each reduction result to one or more collectible source items.
- Selects the currently available or next available mapped ephemeral source and displays a live real-time countdown.
- Displays live availability countdowns for ordinary raw materials from unspoiled and other timed mining or botany nodes.
- Sends the selected collectible source to GatherBuddy for its location, marker, and teleport workflow.
- Fishing availability cards use GatherBuddy's active uptime calculation when it is loaded, and fall back to `Check GatherBuddy` when its timer data cannot be read.

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
3. Integrations use public IPC or registered commands where available; GatherBuddy's live fish timer has no public IPC, so it is read reflectively only while GatherBuddy is loaded.
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

### 2026-07-09

- Added Gwen's Dream crystal top-ups so large queues can pause for retainer crystal refills and continue instead of timing out at the crystal inventory cap.
- Fixed Dream's capped crystal quantity handling so partial retainer withdrawals continue from the actual confirmed amount.
- Added marketboard hover data to search results and selected recipe-plan rows, with quantity-scaled totals for planned outputs.
- Switched selected collectable recipe hovers to total scrip values instead of marketboard data.
- Reworked hover pricing layouts to remove internal scrolling, show NQ and HQ world prices side by side, and highlight unit prices more clearly.
- Tightened `Craft all with Artisan` so it only activates when every required ingredient is already in live inventory, and improved disabled hover guidance for both Artisan and Gwen's Dream.
- Redacted local file paths and retainer names in debug reports before sharing.
- Added release notes for `v1.1.32`, refreshed the manifests and package metadata, and prepared the next release zip for publishing.
- Verification: Debug and Release builds both succeeded with zero warnings and zero errors, and `releases/DalamudRecipeHelper-v1.1.32.zip` was created from the verified Release output.

### 2026-07-08

- Fixed Gwen's Dream large retainer withdrawals so requests that span multiple stacks continue chunk-by-chunk instead of timing out or advancing early.
- Fixed Gwen's Dream withdraw completion checks so Dream only advances after a real retainer transfer has actually been issued and observed.
- Updated the main window integration message when a Craft All queue is manually stopped after the current craft finishes.
- Removed the manual `Artisan popup` button now that the crafting progress window opens automatically when Artisan starts.
- Restored `Craft all with Artisan` visibility when the dependency queue can be built from available raw materials even if direct ingredients are not already crafted.
- Updated the GitHub README to explain that Recipe Helper works best with Artisan, GatherBuddy, and optionally Auto-Retainer for automatic retainer withdrawals.
- Added release notes for `v1.1.31` and refreshed the versioned manifests, package target, changelog text, and release zip for publishing.
- Improved the Artisan progress overlay with a `Stop After Craft` control that ends the Recipe Helper queue after the current craft finishes.
- Refined overlay messaging so pre-craft warnings only describe later blocked recipes instead of contradicting an active final craft.
- Kept the overlay action row available after the queue stops, preserved queue order with numbered steps, and made repeated pre-crafts easier to understand.
- Closing the Recipe Helper queue now closes the in-game crafting windows after the current craft stops or the queue completes.
- Added release notes for `v1.1.30` and refreshed the versioned manifests, package target, changelog text, and release zip for publishing.
- Added a shared context-menu search service that opens Recipe Helper from inventory item menus with the clicked item pre-searched.
- Extended the same context-menu search flow to marketboard search results using the hovered market item from supported marketboard addons.
- Added release notes for `v1.1.29` and refreshed the repo manifest, package target, and changelog text for publishing.
- Verification: Debug and Release builds both succeeded with zero warnings and zero errors, and `releases/DalamudRecipeHelper-v1.1.31.zip` was created from the verified Release output.
- Verification: Debug and Release builds both succeeded with zero warnings and zero errors, and `releases/DalamudRecipeHelper-v1.1.30.zip` was created from the verified Release output.
- Verification: Debug build succeeded with zero warnings and zero errors; Release compilation produced verified output but the automated packager hit a locked `DalamudRecipeHelper.json`, so `releases/DalamudRecipeHelper-v1.1.29.zip` was created manually from the verified Release output.

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
- Started the v1.1.2 update with redundant saved-plan persistence. Saved plans remain in the standard Dalamud configuration and are also mirrored atomically to `saved-recipe-plans.json` in the stable plugin configuration directory.
- On startup, Recipe Helper now restores plans from the separate backup if the main configuration contains none, then writes them back into Dalamud's configuration. Intentional saves, updates, and deletions refresh the backup so removed plans do not reappear.
- Verification after adding saved-plan backup and recovery: Debug and clean Release builds both succeeded with zero warnings and zero errors, and the packaged manifest reports version 1.1.2 with the icon metadata retained.
- Added a `Can craft` inventory-discovery view. It checks every recipe independently against combined live inventory, crystals, saddlebags, and saved retainer snapshots, recursively consuming owned intermediate items or crafting them from other owned materials.
- Craftable results remain available for multi-selection and refresh when inventory changes. The view explicitly treats availability as a materials check and does not claim that the current character has the required job level or recipe unlock.
- Verification after adding craftable-recipe discovery: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Extended `Can craft` results with an exact maximum craft count found by exponential and binary stock simulation, plus the resulting total item output after applying each recipe's yield.
- Verification after adding maximum craft and output totals: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Made the recipe search/results panel horizontally adjustable with a visible draggable divider, resize cursor, and hover guidance. The preferred width is saved in plugin configuration and constrained so the recipe details panel remains usable.
- Verification after adding the adjustable search-panel width: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Removed the fixed 40-result recipe-search limit so every matching recipe can be displayed.
- Added saved-plan duplication with automatic unique copy names and a rename dialog that rejects blank or duplicate names. Both operations immediately refresh the redundant saved-plan backup.
- Saving or updating a plan now clears the selected recipe workspace and resets the plan-name field, while keeping the brief confirmation visible in the empty-plan view.
- Verification for the complete v1.1.2 feature set: Debug and clean Release builds succeeded with zero warnings and zero errors; the packaged manifest reports version 1.1.2 and retains the public icon URL.
- Made the saved-plan table columns resizable and added visible vertical separators.
- Added an in-results text filter, multi-selection checkboxes that can add or remove recipes without closing the result list, and a `Clear search` action that leaves the selected plan intact.
- Release-process decision: do not update GitHub source, create a release draft, or point `repo.json` at an unreleased asset until the user explicitly confirms that all planned changes are finished and asks to prepare the release.
- Restored both the local and public `repo.json` targets to the published v1.1.1 asset while v1.1.2 remains under active development.
- Verification after the saved-plan column and search-result improvements: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Kept all saved-plan action buttons on one horizontal line and increased the action column's initial width while retaining user resizing.
- Verification after aligning the saved-plan action buttons: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Unified material timer ordering across the main ingredient tables and Missing Items Overlay. Active timed nodes appear first in remaining-time order, followed by upcoming timed nodes in availability order, then untimed materials.
- Verification after unifying timed-node ordering: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Stabilized equal-timer material ordering by calculating every row against one shared timer snapshot per list and using material name and item ID as deterministic tie-breakers.
- Verification after stabilizing equal-timer rows: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Corrected green-row highlighting for timed materials. Timed-node rows now use active-window availability exclusively, while ordinary untimed materials retain the existing enough-in-stock highlight.
- Verification after correcting timed-node highlighting: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Timed material rows now highlight green when their node is currently active or when the required material quantity is already owned.
- Verification after combining availability and ownership highlighting: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Added an estimated craft duration to the plan summary. It multiplies the plan's total craft count by a user-adjustable average seconds-per-craft value (default 30 seconds), with a tooltip explaining that actual Artisan duration varies.
- Verification after adding the craft-duration estimate: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Extended Craft All to construct a dependency-ordered Artisan queue. It consumes combined owned stock, recursively queues missing craftable intermediates before their dependants, and only enables the action when the complete queue can be supplied.
- The plan's estimated craft duration now includes intermediate crafts whenever a complete Craft All queue is available.
- Verification after adding dependency-ordered intermediate crafting: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Removed the main-window Gather action from material rows once the required quantity has already been obtained.
- Verification after hiding completed Gather actions: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Removed `Short` and placeholder text from the direct-ingredient `From raw` column. Cells now remain empty unless the ingredient is owned or can be crafted.
- Verification after simplifying the `From raw` status: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Made the plan-name field submit on Enter as well as the Save Plan button; both successful save paths clear the field immediately.
- Verification after updating plan-name submission: Debug and clean Release builds both succeeded with zero warnings and zero errors.
- Published Recipe Helper v1.1.2 as the latest public GitHub release with `DalamudRecipeHelper-v1.1.2.zip`. GitHub and the local package report the same SHA-256 digest: `89327F6A92D9C5AFA19F7EC6131A53ED1F9EC1B3D8120963FE4639B37F53833F`.
- Updated the public custom-repository manifest to advertise v1.1.2 only after the matching release asset was available.
- Repaired a GitHub web-editor synchronization issue that had appended previous content to updated text files. GitHub's contents API then confirmed that every file on the current `main` branch exactly matches the local project, and the public `repo.json` parses as one valid v1.1.2 entry.
- The v1.1.2 Dalamud install ZIP and custom-repository feed are verified and unaffected. GitHub's automatically generated v1.1.2 source archives were created from the tag before the web-editor repair; use the current `main` branch for the corrected v1.1.2 source.
- Started post-v1.1.2 development by adding saved-plan selection checkboxes. Multiple checked plans can be merged into the active workspace or sent to Artisan as one combined, dependency-ordered crafting queue.
- Added a persistent main-window toggle to show or hide completed raw materials, shards, crystals, and clusters without hiding direct ingredients.
- Advanced the local development version to 1.1.3.0 while leaving the published custom-repository manifest on v1.1.2 until the next release is explicitly requested.
- Verification after adding multi-plan crafting and completed-raw-material visibility: Debug and clean Release builds both succeeded with zero warnings and zero errors; the development manifest reports version 1.1.3.0.
- Published Recipe Helper v1.1.3 as the latest GitHub release with `DalamudRecipeHelper-v1.1.3.zip`. GitHub and the local package report the same SHA-256 digest: `062406B9985DA08B1E7E0E4481ADBA8D7F05D20E94F7506181BBC7073A93A681`.
- Activated the custom-repository manifest for v1.1.3 only after the matching public release asset was verified.
- Updated Artisan queue status text to use `pre-crafts` instead of `intermediates`. The status reports recipe-plan counts for multi-plan crafting or recipe counts for the regular queue, only mentions pre-crafts when they are actually required, and clears automatically after Artisan completes the queue.
- Advanced the local development and release-candidate version to 1.1.4.0.
- Verification after updating the Artisan queue status lifecycle: Debug and clean Release builds both succeeded with zero warnings and zero errors.

### 2026-06-29

- Hardened the sequential Artisan `Craft all` queue so the active batch stays tracked until Artisan actually starts and finishes it, reducing the chance of losing the remaining queue after interruptions.
- Merged the old plan summary into the `Selected Recipes` section, removed the estimated craft-time display, changed `Output` to `Quantity`, removed recipe IDs from the selected-recipe rows, widened the plan-name field, and added the plugin version to the main window title area.
- Moved `Missing Items Overlay` and `Refresh Inventory` into the top action row and added broader hover help across search, plan, recipe, and gather actions.
- Normalized button heights and widths across the saved-plan actions, selected-recipe actions, raw-material craft buttons, main gather buttons, and overlay gather buttons for a cleaner release build.
- Published Recipe Helper v1.1.4 as the latest GitHub release with `DalamudRecipeHelper-v1.1.4.zip`.
- Repaired the public custom-repository feed after release. The initial live `repo.json` still advertised v1.1.3, then a GitHub web-editor save duplicated old and new JSON in one file. The published `main` branch now serves one valid v1.1.4 entry with the correct release download URLs.
- Verification after the v1.1.4 release and feed repair: Debug and clean Release builds both succeeded with zero warnings and zero errors, and the live raw `repo.json` now reports `AssemblyVersion` `1.1.4.0` with the v1.1.4 asset links.
- Prepared Recipe Helper v1.1.5.0 to replace the stale v1.1.4 package that was still loading the older UI in game even though the repository feed advertised v1.1.4.
- Updated the plugin manifest, project version, and custom-repository feed together so the release metadata, in-game version display, and GitHub asset target all advance in lockstep to v1.1.5.
- Published Recipe Helper v1.1.5 as the latest GitHub release with `DalamudRecipeHelper-v1.1.5.zip`.
- Updated the public `repo.json` on `main` to point at the v1.1.5 release asset after publishing, then verified the committed GitHub file and direct raw branch URL both report `AssemblyVersion` `1.1.5.0`.
- Noted one short raw GitHub cache delay during verification: the legacy `raw.githubusercontent.com` URL briefly continued serving the older v1.1.4 manifest before catching up, after which the user confirmed the live repository feed was working correctly.
- Replaced the corrupted bullet separator in recipe subtitles with a plain ASCII `|` so `CRAFTABLE NOW` counts and add-to-plan hints render cleanly in game.
- Prepared Recipe Helper v1.1.6.0 for the next live release.
- Updated the plugin manifest, project version, and custom-repository feed together so the release metadata, in-game version display, and GitHub asset target all advance in lockstep to v1.1.6.
- Verification after preparing v1.1.6: Debug build succeeded with zero warnings and zero errors, Release compilation succeeded with the `1.1.6.0` manifest in `bin\Release`, the automated packager could not clean its locked output directory, and the manual upload ZIP `DalamudRecipeHelper-v1.1.6.zip` was created with SHA-256 `CE5AD8BEF190361019C20497F5EF9C64D5CFEDC8EC4D476E1733DE2077F5BE6F`.

### 2026-07-02

- Added marketboard pricing lookups through Universalis so the main planner and Missing Items Overlay can show live Oceania pricing context for materials.
- Expanded UI customization with button, input-card, and folder-header colors, named theme presets, and an overlay option to keep vendor materials visible when desired.
- Added saved-plan folders with move and rename flows, plus the `Gwen's Dream` retainer-withdraw workflow.
- Hardened `Gwen's Dream` by moving dev builds to a stable XIVLauncher `devPlugins\RecipeHelper\Debug` path, fixing the first retainer-list selection gate, and planning withdrawals so retained pre-craft/direct items are used before missing raw materials are withdrawn.
- Prepared Recipe Helper v1.1.12.0 for the next live release.
- Verification after preparing v1.1.12: Debug and Release builds both succeeded with zero warnings and zero errors, the Release manifest reports `1.1.12.0`, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.12.zip` was created with SHA-256 `207E271E8A046EC44D307ED8D28585D9519387F38DB0E1948E62BAAF58CA80B4`.

### 2026-07-03

- Folded Customisation and Debug access into Settings, added support-report copy and clear actions, and now clear the debug log when the Debug window closes so reports stay short and current.
- Expanded UI scaling with 60%-100% interface steps, global text scaling, broader layout fixes for scaled tables and recipe rows, and side-by-side placement for the selected-recipe Artisan and `Gwen's Dream` actions.
- Added richer colour customization, moved option help text beside controls where needed, and kept the in-game support flow centered on the Settings area rather than opening extra top-level windows.
- Hardened `Gwen's Dream` against AutoRetainer conflicts by detecting when AutoRetainer is already processing retainers, stopping cleanly instead of timing out, and surfacing the busy state in the debug report.
- Rebuilt the local Debug deployment after the v1.1.13 version bump so the active dev plugin copy matches the Release build again, and added a version-parity check to `tools\Prepare-Release.ps1` to catch future Debug/Release/dev-plugin drift before publishing.
- Prepared Recipe Helper v1.1.13.0 for the next live release.
- Verification after preparing v1.1.13: Debug and Release builds both succeeded with zero warnings and zero errors, the Release manifest reports `1.1.13.0`, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.13.zip` was created with SHA-256 `16083E59F18BA39D5B73C1C8CC0E389B986B56DC016AAD43E21A4AFC85ADD284`.

### 2026-07-04

- Fixed a `Gwen's Dream` start-state failure where the flow could time out waiting for the retainer list if the user was already inside a retainer window when Dream began.
- Dream now continues directly when the target retainer inventory or prompt state is already open, and it closes unrelated open retainer windows before retrying the normal list flow.
- Broadened retainer inventory detection so the plain `InventoryRetainer` shell counts as an open retainer inventory even when the grid visibility is inconsistent.
- Prepared Recipe Helper v1.1.14.0 for the next live release.
- Verification after preparing v1.1.14: Debug build succeeded with zero warnings and zero errors, Release compilation produced the fresh `1.1.14.0` DLL but the automated packager hit a manifest file-lock on `DalamudRecipeHelper.json`, the Release manifest was then refreshed manually from the root manifest, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.14.zip` was created with SHA-256 `BB61B9C5491B08D06E37395658DD75A4724732E9E7EBEF53953B2D27C6B9162A`.
- Fixed the v1.1.14 regression where Dream could open the retainer list and immediately close it again because the new recovery path treated the normal retainer list as a pre-open retainer state.
- The pre-open recovery now ignores a normal `RetainerList` and only intervenes for genuine already-open inventory or prompt states.
- Prepared Recipe Helper v1.1.15.0 for the next live release.
- Verification after preparing v1.1.15: Debug build succeeded with zero warnings and zero errors, Release compilation produced the fresh `1.1.15.0` DLL but the automated packager hit the same manifest file-lock on `DalamudRecipeHelper.json`, the Release manifest was then refreshed manually from the root manifest, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.15.zip` was created with SHA-256 `2858A41F36A2FC258EE10BF0F9497394C8F152663A820E848597210B21784C36`.
- Tightened `Gwen's Dream` AutoRetainer busy detection so an enabled scheduler alone no longer blocks Dream when AutoRetainer is otherwise idle.
- Added recipe job abbreviations to both normal search and `Can craft` results, combined multiple jobs when one craftable item can come from different recipes, and added a job filter to the results pane.
- Prepared Recipe Helper v1.1.16.0 for the next live release.
- Verification after preparing v1.1.16: Debug build succeeded with zero warnings and zero errors, Release compilation produced the fresh `1.1.16.0` DLL but the automated packager hit the same manifest file-lock on `DalamudRecipeHelper.json`, the Release manifest was refreshed manually from the root manifest, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.16.zip` was created with SHA-256 `7BF59D457E102A06981A09CC61048097EE842CE3EE910FF03FD10E2C369186C0`.
- Hardened `Gwen's Dream` retainer-list selection so the active target retainer name is always considered during matching, list text is normalized more loosely, and diagnostic logging now reports visible retainer entries when Dream can see the list but cannot select a row yet.
- Prepared Recipe Helper v1.1.17.0 for the next live release.
- Verification after preparing v1.1.17: Debug build succeeded with zero warnings and zero errors, Release compilation produced the fresh `1.1.17.0` DLL but the automated packager hit the same manifest file-lock on `DalamudRecipeHelper.json`, the Release manifest was refreshed manually from the root manifest, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.17.zip` was created with SHA-256 `5A74CBB348D41BF0C1180B55549A36BF775583CFB4F4D22EB7391688A3060845`.

### 2026-07-05

- Added a compact Artisan progress popup that tracks current crafts, queued recipes, completed recipes, and time spent crafting this session, while still allowing Recipe Helper to be reopened from the popup when desired.
- Reworked `Gwen's Dream` so it stays usable when every ingredient is already in live inventory, goes straight into Artisan when no retainer withdrawals are needed, and continues to cooperate more cleanly with AutoRetainer pause and resume states.
- Expanded customisation with hex colour entry, separate accent-text, section-header-text, folder-text, saved-plan-text, and button-text colours, plus built-in blue, pink, purple, green, and orange presets alongside user-saved presets.
- Unified button styling across the main UI, settings, debug window, overlays, and raw-material crafting actions, removing older one-off colour overrides that had started to bloat the theme system.
- Moved Customisation and Debug access into Settings, improved scaling and layout behaviour at smaller interface sizes, and made the `Selected Recipes` action row live inside the section instead of floating below it.
- Added nested saved-plan folders with move and rename flows, clearer parent and subfolder handling, and separate colour control for saved-plan rows and section headers.
- Prepared Recipe Helper v1.1.18.0 for the next live release with a package-visible changelog so users can see what changed directly from the published metadata.
- Verification after preparing v1.1.18: Debug and Release builds both succeeded with zero warnings and zero errors, both generated manifests report `1.1.18.0` with the new changelog field, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.18.zip` was created with SHA-256 `8DBC8FCDCA0DB52D8CDE9B3CDFB381F0559B01A83CDBE93E0F31684F1347BA05`.
- Published Recipe Helper v1.1.18 as the latest GitHub release with `DalamudRecipeHelper-v1.1.18.zip`, using the same user-facing changelog in the package metadata, custom repository feed, and release notes.
- Tightened the Artisan progress popup status text so it now automatically chooses a clearly light or dark colour based on the rendered status-card background, keeping the text readable across very dark and very light themes.
- Prepared Recipe Helper v1.1.19.0 as a quick follow-up release for the popup readability fix.
- Verification after preparing v1.1.19: Debug and Release builds both succeeded with zero warnings and zero errors, both generated manifests report `1.1.19.0` with the new changelog field, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.19.zip` was created with SHA-256 `07ABD348F72E9C97D4360284FAD9DBF6660FC579A62D9927738526E8BDE3BFC1`.
- Published Recipe Helper v1.1.19 as the latest GitHub release with `DalamudRecipeHelper-v1.1.19.zip`, carrying the quick popup readability changelog through the package metadata, custom repository feed, and release notes.
- Renamed the popup window title from `Artisan Progress` to `Recipe Helper Crafting Progress` so users can identify it more clearly in game.
- Prepared Recipe Helper v1.1.20.0 as a quick naming follow-up release for the crafting progress window.
- Verification after preparing v1.1.20: Debug and Release builds both succeeded with zero warnings and zero errors, both generated manifests report `1.1.20.0` with the new changelog field, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.20.zip` was created with SHA-256 `D07648BE3FC188EA38CF424B0BA4529D0C5DA48472BC7A3731EA4BF976057032`.
- Published Recipe Helper v1.1.20 as the latest GitHub release with `DalamudRecipeHelper-v1.1.20.zip`, carrying the quick window-title rename changelog through the package metadata, custom repository feed, and release notes.
- Added gatherable and collectable browsing, planning, and filter improvements, including always-visible job/type/scrip filters with safer gatherable-only behavior.
- Improved collectable support with base and bonus scrip tooltips in the main UI and missing items overlay, plus clearer base hand-in value labeling.
- Fixed several UI issues across the missing items overlay and timed availability cards, including better material-column sizing and a two-stage orange-to-red timer warning.
- Improved Artisan and AutoRetainer resume handling so interrupted recipes retry remaining crafts and completed recipes do not get re-queued after retainer interruptions.
- Prepared Recipe Helper v1.1.21.0 for live publish with updated manifests, repo feed links, release notes, and package metadata.
- Verification after preparing v1.1.21: sequential Debug and Release builds both succeeded with zero warnings and zero errors, both generated manifests report `1.1.21.0`, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.21.zip` was created with SHA-256 `01054404520222E9ACBDF13B37A1D1589D912166737D8897D835BD4C06F5D6B3`.
- Fixed a gatherable job lookup crash triggered by invalid gathering point subcategory data while filtering gatherables or collectables.
- Adjusted the gatherable job lookup to keep using the safer fallback path when explicit class-job data is unavailable.
- Prepared Recipe Helper v1.1.22.0 as a quick stability follow-up release for the gatherable filter crash.
- Verification after preparing v1.1.22: sequential Debug and Release builds both succeeded with zero warnings and zero errors, both generated manifests report `1.1.22.0`, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.22.zip` was created with SHA-256 `1489457ABF42C1F519834DE1CB989F24F3BF0F928CED348D11ADDD8EF31B1EFC`.
- Added built-in `Red`, `Yellow`, and `Grey` theme presets to the customisation window as quick preset options beside the existing palette choices.
- Prepared Recipe Helper v1.1.23.0 as a quick theme-preset follow-up release.
- Verification after preparing v1.1.23: sequential Debug and Release builds both succeeded with zero warnings and zero errors, both generated manifests report `1.1.23.0`, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.23.zip` was created with SHA-256 `EED1405C8ED75E9896438AE840529D73A80FE2AFAFB7C1658406C322AEF5534E`.
- Expanded gatherables and collectables planning with clearer collectible recipe handling, better hand-in value tooltips, and the `Gather` header tab label.
- Made Saved Plans, saved-plan folders, Gatherables, and Collectables start closed on open, and split Raw Materials vs Shards/Crystals/Clusters obtained-item toggles so they behave independently.
- Prepared Recipe Helper v1.1.24.0 for live publish with updated manifests, repo feed links, and release notes.
- Verification after preparing v1.1.24: Debug build succeeded with zero warnings and zero errors, Release compilation produced the fresh `1.1.24.0` DLL but the automated packager hit the known manifest file-lock on `DalamudRecipeHelper.json`, the Release manifest was refreshed manually from the root manifest, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.24.zip` was created with SHA-256 `24BF4B3AFEF90CDC84D89FAE214B29308FFA4EA7BB22354B80869A1AB6010E2C`.
- Renamed the gatherables and collectables quantity column from `Need` to `Qty` for clearer wording.
- Prepared Recipe Helper v1.1.25.0 as a quick wording follow-up release.
- Verification after preparing v1.1.25: Release and final Debug builds both succeeded with zero warnings and zero errors, an earlier parallel build attempt hit the known manifest file-lock on `DalamudRecipeHelper.json`, the Debug and Release manifests were refreshed manually from the root manifest, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.25.zip` was created with SHA-256 `386BB7A82AEE682062CDD5A735CEEF46B073DBEF9BBB987B3D3CC3986C5542E1`.
- Hardened Gwen's Dream retainer UI callback handling so quantity confirmation and nearby retainer actions only operate on visible, ready add-ons.
- Prepared Recipe Helper v1.1.26.0 as a quick Gwen's Dream crash-fix release.
- Verification after preparing v1.1.26: Debug build succeeded with zero warnings and zero errors, Release compilation produced the fresh `1.1.26.0` DLL but the automated packager hit the known manifest file-lock on `DalamudRecipeHelper.json`, the Debug and Release manifests were refreshed manually from the root manifest, and the publish ZIP `artifacts\Release\DalamudRecipeHelper-v1.1.26.zip` was created with SHA-256 `D7D89C26F4E648C40B9F945DA60D8294B584DD0BADD15EE2B494488EEA188D39`.
- Reworked Gwen's Dream withdraw quantity confirmation to use a direct integer callback payload instead of `FireCallbackInt`, matching the safer callback pattern used by ECommons/AutoRetainer for numeric prompts.
- Prepared Recipe Helper v1.1.27.0 as a follow-up Gwen's Dream crash-fix release.
- Verification after preparing v1.1.27: Debug and Release builds both succeeded with zero warnings and zero errors, and the publish ZIP `releases\DalamudRecipeHelper-v1.1.27.zip` was created with SHA-256 `93E00380047A832A5DA1EBDFBA32CA078BAAE589FF8006471EA19BCEF6377182`.
- Added flashing warning states to active timed-node `NOW` availability cards in both the main recipe window and the Missing Items Overlay, turning orange inside two minutes remaining and red inside one minute remaining.
- Prepared Recipe Helper v1.1.28.0 as a timed-node warning follow-up release.
- Verification after preparing v1.1.28: Debug and Release builds both succeeded with zero warnings and zero errors, the release safety script moved the local dev-plugin copy to `C:\Users\megha\AppData\Roaming\XIVLauncher\backups\RecipeHelper\20260708-092749`, and the publish ZIP `releases\DalamudRecipeHelper-v1.1.28.zip` was created with SHA-256 `E3A3EDA320EBEDBDDE6DC331203A58AF364F9C4AB737188050113474E661441C`.

## Continuation checklist

When returning to this project:

1. Read this file and `README.md`.
2. Confirm the installed Dalamud API and the public IPC/command contracts of integrated plugins.
3. Make the requested change without directly coupling to another plugin's internal assemblies.
4. Run `dotnet build .\DalamudRecipeHelper.csproj --no-restore`.
5. Confirm the build has zero errors and review any warnings.
6. Add a dated entry to this history describing the change, decisions, limitations, and verification result.
