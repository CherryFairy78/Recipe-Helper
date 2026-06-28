using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace DalamudRecipeHelper;

public sealed class RecipeWindow : Window, IDisposable
{
    private readonly RecipeService recipeService;
    private readonly InventoryService inventoryService;
    private readonly TravelService travelService;
    private readonly PluginIntegrationService pluginIntegrationService;
    private readonly AetherialReductionService aetherialReductionService;
    private readonly Configuration configuration;
    private readonly Action openSettings;
    private readonly Action saveConfiguration;
    private readonly RawMaterialsOverlayWindow rawMaterialsOverlayWindow;
    private IReadOnlyList<RecipeMatch> searchResults = [];
    private IReadOnlyDictionary<uint, CraftableRecipeAvailability> craftableAvailability =
        new Dictionary<uint, CraftableRecipeAvailability>();
    private bool showingCraftableRecipes;
    private readonly List<RecipePlanSelection> selectedRecipes = [];
    private IReadOnlyList<ArtisanCraftQueueEntry> artisanCraftQueue = [];
    private string artisanCraftQueueError = string.Empty;
    private string searchText = string.Empty;
    private string resultFilter = string.Empty;
    private RecipePlanDetails? recipePlanDetails;
    private IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems = new Dictionary<uint, OwnedInventoryItem>();
    private IReadOnlyList<GatheringDestination> gatheringDestinations = [];
    private string gatheringItemName = string.Empty;
    private string travelMessage = string.Empty;
    private bool isTravelPopupOpen;
    private bool travelPopupRequested;
    private string integrationMessage = string.Empty;
    private bool integrationError;
    private bool inventoryRefreshRequested;
    private string planName = string.Empty;
    private string planMessage = string.Empty;
    private bool planMessageIsError;
    private DateTime planMessageExpiresAt;
    private bool wasResizingSearchPane;
    private SavedRecipePlan? renamingPlan;
    private string renamePlanName = string.Empty;
    private string renamePlanError = string.Empty;
    private bool renamePlanPopupRequested;
    private bool isRenamePlanPopupOpen;

    public RecipeWindow(
        RecipeService recipeService,
        InventoryService inventoryService,
        TravelService travelService,
        PluginIntegrationService pluginIntegrationService,
        AetherialReductionService aetherialReductionService,
        Configuration configuration,
        Action openSettings,
        Action saveConfiguration,
        RawMaterialsOverlayWindow rawMaterialsOverlayWindow)
        : base("Recipe Helper###DalamudRecipeHelper")
    {
        this.recipeService = recipeService;
        this.inventoryService = inventoryService;
        this.travelService = travelService;
        this.pluginIntegrationService = pluginIntegrationService;
        this.aetherialReductionService = aetherialReductionService;
        this.configuration = configuration;
        this.openSettings = openSettings;
        this.saveConfiguration = saveConfiguration;
        this.rawMaterialsOverlayWindow = rawMaterialsOverlayWindow;
        this.inventoryService.InventoryChanged += this.OnInventoryChanged;
        this.Size = new Vector2(760, 540);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public string SearchText
    {
        get => this.searchText;
        set => this.searchText = value;
    }

    public override void PreDraw() => WindowTheme.Push(this.configuration);

    public override void PostDraw() => WindowTheme.Pop();

    public void Dispose()
    {
        this.inventoryService.InventoryChanged -= this.OnInventoryChanged;
        this.inventoryService.Dispose();
        this.travelService.Dispose();
    }

    public override void Draw()
    {
        if (this.inventoryRefreshRequested)
        {
            this.inventoryRefreshRequested = false;
            this.RefreshDetails(true);
        }

        this.PushModernStyle();
        try
        {
            this.DrawHeader();

            var paneArea = ImGui.GetContentRegionAvail();
            const float splitterWidth = 5f;
            var paneSpacing = ImGui.GetStyle().ItemSpacing.X;
            var maximumSearchWidth = Math.Max(
                150f,
                paneArea.X - splitterWidth - (paneSpacing * 2) - 260f);
            var leftWidth = Math.Clamp(
                this.configuration.SearchPaneWidth,
                150f,
                maximumSearchWidth);
            if (ImGui.BeginChild("results", new Vector2(leftWidth, 0), true))
            {
                var displayedResults = string.IsNullOrWhiteSpace(this.resultFilter)
                    ? this.searchResults
                    : this.searchResults
                        .Where(result => result.ResultName.Contains(
                            this.resultFilter.Trim(),
                            StringComparison.CurrentCultureIgnoreCase))
                        .ToList();
                ImGui.TextColored(
                    this.configuration.AccentColor,
                    this.showingCraftableRecipes ? "CRAFTABLE NOW" : "RECIPES");
                ImGui.SameLine();
                ImGui.TextDisabled(
                    string.IsNullOrWhiteSpace(this.resultFilter)
                        ? $"  {this.searchResults.Count}"
                        : $"  {displayedResults.Count}/{this.searchResults.Count}");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (this.searchResults.Count > 0)
                {
                    ImGui.SetNextItemWidth(-1);
                    ImGui.InputTextWithHint(
                        "##result-filter",
                        "Filter these results",
                        ref this.resultFilter,
                        128);
                    if (ImGui.SmallButton("Clear search"))
                        this.ClearSearch();
                    if (!string.IsNullOrWhiteSpace(this.resultFilter))
                    {
                        ImGui.SameLine();
                        if (ImGui.SmallButton("Clear filter"))
                            this.resultFilter = string.Empty;
                    }

                    ImGui.Spacing();
                }

                if (this.searchResults.Count == 0)
                {
                    if (this.showingCraftableRecipes)
                    {
                        ImGui.TextDisabled("No recipes can currently");
                        ImGui.TextDisabled("be made from stored items.");
                    }
                    else
                    {
                        ImGui.TextDisabled("Search for a crafted item");
                        ImGui.TextDisabled("to begin planning.");
                    }
                }
                else if (displayedResults.Count == 0)
                {
                    ImGui.TextDisabled("No results match this filter.");
                }

                foreach (var result in displayedResults)
                {
                    ImGui.PushID((int)result.RecipeId);
                    var isSelected = this.selectedRecipes.Any(
                        selection => selection.Recipe.RecipeId == result.RecipeId);
                    var shouldBeSelected = isSelected;
                    if (ImGui.Checkbox("##selected-recipe", ref shouldBeSelected))
                        this.SetRecipeSelected(result, shouldBeSelected);
                    ImGui.SameLine();
                    var subtitle = this.showingCraftableRecipes &&
                                   this.craftableAvailability.TryGetValue(
                                       result.RecipeId,
                                       out var availability)
                        ? $"{availability.CraftCount:N0} crafts  •  {availability.OutputAmount:N0} items"
                        : isSelected
                            ? "Added to plan"
                            : $"Click to add  •  Yield {result.ResultAmount}";
                    var label = $"{result.ResultName}\n{subtitle}";
                    if (ImGui.Selectable(
                            label,
                            isSelected,
                            ImGuiSelectableFlags.None,
                            new Vector2(0, 34)))
                        this.SetRecipeSelected(result, !isSelected);
                    ImGui.PopID();
                }
            }

            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                WithAlpha(this.configuration.AccentColor, 0.22f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonHovered,
                WithAlpha(this.configuration.AccentColor, 0.55f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonActive,
                WithAlpha(this.configuration.AccentColor, 0.78f));
            ImGui.Button(
                "##recipe-search-pane-splitter",
                new Vector2(splitterWidth, Math.Max(1f, paneArea.Y)));
            ImGui.PopStyleColor(3);

            var isResizingSearchPane = ImGui.IsItemActive();
            if (ImGui.IsItemHovered() || isResizingSearchPane)
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Drag to resize the recipe search area.");
            if (isResizingSearchPane)
            {
                this.configuration.SearchPaneWidth = Math.Clamp(
                    leftWidth + ImGui.GetIO().MouseDelta.X,
                    150f,
                    maximumSearchWidth);
            }

            if (this.wasResizingSearchPane && !isResizingSearchPane)
                this.saveConfiguration();
            this.wasResizingSearchPane = isResizingSearchPane;

            ImGui.SameLine();

            if (ImGui.BeginChild("details", Vector2.Zero, true))
                this.DrawDetails();

            ImGui.EndChild();
        }
        finally
        {
            ImGui.PopStyleColor(7);
            ImGui.PopStyleVar(5);
        }
    }

    private void DrawHeader()
    {
        ImGui.TextColored(this.configuration.AccentColor, "RECIPE HELPER");
        ImGui.SameLine();
        ImGui.TextDisabled("Plan  •  Check  •  Craft");

        var searchButtonWidth = ImGui.CalcTextSize("Search").X + 20;
        var craftableButtonWidth = ImGui.CalcTextSize("Can craft").X + 20;
        var settingsButtonWidth = ImGui.CalcTextSize("Settings").X + 20;
        var searchWidth = MathF.Max(
            150,
            ImGui.GetContentRegionAvail().X -
            searchButtonWidth -
            craftableButtonWidth -
            settingsButtonWidth -
            24);

        ImGui.SetNextItemWidth(searchWidth);
        var searchChanged = ImGui.InputTextWithHint(
            "##recipe-search",
            "Search crafted item or item ID",
            ref this.searchText,
            128);
        ImGui.SameLine();
        if (ImGui.Button("Search", new Vector2(searchButtonWidth, 0)) ||
            searchChanged && this.searchText.Length >= 3)
            this.RefreshSearch();

        ImGui.SameLine();
        if (ImGui.Button("Can craft", new Vector2(craftableButtonWidth, 0)))
            this.RefreshCraftableRecipes(true);

        ImGui.SameLine();
        if (ImGui.Button("Settings", new Vector2(settingsButtonWidth, 0)))
            this.openSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void PushModernStyle()
    {
        var accent = this.configuration.AccentColor;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 4));

        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(accent, 0.72f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AdjustColor(accent, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AdjustColor(accent, -0.08f));
        ImGui.PushStyleColor(ImGuiCol.Header, WithAlpha(accent, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, WithAlpha(accent, 0.48f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, WithAlpha(accent, 0.62f));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, WithAlpha(accent, 0.22f));
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);

    private static Vector4 AdjustColor(Vector4 color, float amount) =>
        new(
            Math.Clamp(color.X + amount, 0, 1),
            Math.Clamp(color.Y + amount, 0, 1),
            Math.Clamp(color.Z + amount, 0, 1),
            color.W);

    public void RefreshSearch()
    {
        this.showingCraftableRecipes = false;
        this.resultFilter = string.Empty;
        this.craftableAvailability =
            new Dictionary<uint, CraftableRecipeAvailability>();
        this.searchResults = this.recipeService.Search(this.searchText);
    }

    private void RefreshCraftableRecipes(bool scanInventory)
    {
        if (scanInventory)
            this.ownedItems = this.inventoryService.GetOwnedItems();

        this.showingCraftableRecipes = true;
        if (scanInventory)
            this.resultFilter = string.Empty;
        this.searchText = string.Empty;
        var available = this.recipeService.GetCraftableRecipes(this.ownedItems);
        this.craftableAvailability = available.ToDictionary(
            entry => entry.Recipe.RecipeId);
        this.searchResults = available
            .Select(entry => entry.Recipe)
            .ToList();
    }

    private void AddRecipe(RecipeMatch recipe)
    {
        if (this.selectedRecipes.Any(
                selection => selection.Recipe.RecipeId == recipe.RecipeId))
            return;

        this.selectedRecipes.Add(new RecipePlanSelection(
            recipe,
            Math.Max(1, recipe.ResultAmount)));
        this.RefreshDetails(true);
    }

    private void SetRecipeSelected(RecipeMatch recipe, bool selected)
    {
        if (selected)
        {
            this.AddRecipe(recipe);
            return;
        }

        if (this.selectedRecipes.RemoveAll(
                selection => selection.Recipe.RecipeId == recipe.RecipeId) > 0)
            this.RefreshDetails(false);
    }

    private void ClearSearch()
    {
        this.searchText = string.Empty;
        this.resultFilter = string.Empty;
        this.searchResults = [];
        this.showingCraftableRecipes = false;
        this.craftableAvailability =
            new Dictionary<uint, CraftableRecipeAvailability>();
    }

    private void RefreshDetails(bool scanInventory)
    {
        if (scanInventory)
        {
            this.ownedItems = this.inventoryService.GetOwnedItems();
            if (this.showingCraftableRecipes)
                this.RefreshCraftableRecipes(false);
        }

        if (this.selectedRecipes.Count == 0)
        {
            this.recipePlanDetails = null;
            this.artisanCraftQueue = [];
            this.artisanCraftQueueError = string.Empty;
            this.rawMaterialsOverlayWindow.SetMaterials([], []);
            return;
        }

        this.recipePlanDetails = this.recipeService.GetPlanDetails(
            this.selectedRecipes,
            this.ownedItems);
        if (this.recipePlanDetails is { } details)
        {
            if (!this.recipeService.TryBuildArtisanCraftQueue(
                    details.Recipes,
                    this.ownedItems,
                    out this.artisanCraftQueue,
                    out this.artisanCraftQueueError))
                this.artisanCraftQueue = [];

            this.rawMaterialsOverlayWindow.SetMaterials(
                details.Recipes.Select(recipe => recipe.ResultName).ToList(),
                details.RawMaterials);
        }
    }

    private void DrawDetails()
    {
        if (this.recipePlanDetails is null)
        {
            ImGui.Dummy(new Vector2(0, 24));
            ImGui.TextColored(
                this.configuration.AccentColor,
                this.showingCraftableRecipes
                    ? $"{this.searchResults.Count} recipes craftable from current stock"
                    : "No recipes selected");
            ImGui.TextDisabled(
                this.showingCraftableRecipes
                    ? "Select a recipe from the list. Materials only; job level and recipe unlocks are not checked."
                    : "Choose one or more recipes from the list to build a combined plan.");
            this.DrawPlanMessage();
            if (this.configuration.SavedRecipePlans.Count > 0 &&
                this.DrawCollapsibleSection(
                    "SAVED PLANS",
                    "Load, duplicate, rename, or delete a named recipe plan",
                    "saved-plans-empty-section"))
                this.DrawSavedPlans();
            return;
        }

        var details = this.recipePlanDetails;
        ImGui.TextColored(this.configuration.AccentColor, "RECIPE PLAN");
        ImGui.SameLine();
        ImGui.TextDisabled($"{details.Recipes.Count} selected");
        this.DrawSavePlanControls();
        if (this.configuration.SavedRecipePlans.Count > 0 &&
            this.DrawCollapsibleSection(
                "SAVED PLANS",
                "Load, duplicate, rename, or delete a named recipe plan",
                "saved-plans-section"))
            this.DrawSavedPlans();

        if (this.DrawCollapsibleSection(
                "PLAN SUMMARY",
                "Combined recipe, craft, and raw-item totals",
                "plan-summary-section"))
            this.DrawSummary(details);

        if (this.DrawCollapsibleSection(
                "SELECTED RECIPES",
                "Output quantities and recipe actions",
                "selected-recipes-section"))
            this.DrawSelectedRecipes(details);

        ImGui.Spacing();
        var canCraftAll =
            this.artisanCraftQueue.Count > 0 &&
            (details.Recipes.Count > 1 ||
             this.artisanCraftQueue.Any(recipe => recipe.IsIntermediate));
        if (canCraftAll)
        {
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                this.configuration.ReadyButtonColor);
            var craftAllClicked = ImGui.Button("Craft all with Artisan");
            ImGui.PopStyleColor();
            if (craftAllClicked)
            {
                this.integrationError =
                    !this.pluginIntegrationService.CraftAllWithArtisan(
                        this.artisanCraftQueue,
                        out this.integrationMessage);
            }

            ImGui.SameLine();
        }

        if (ImGui.Button("Missing Items Overlay"))
            this.rawMaterialsOverlayWindow.IsOpen = true;

        if (!string.IsNullOrWhiteSpace(this.integrationMessage))
        {
            ImGui.TextColored(
                this.integrationError
                    ? this.configuration.MissingTextColor
                    : this.configuration.SuccessTextColor,
                this.integrationMessage);
        }

        if (ImGui.Button("Refresh inventory"))
            this.RefreshDetails(true);

        ImGui.SameLine();
        ImGui.TextDisabled(
            $"{this.inventoryService.LastItemStacks} stacks  •  " +
            $"{this.inventoryService.LastScannedContainers} live containers  •  " +
            $"{this.inventoryService.LastStoredRetainers} retainers saved");

        ImGui.Spacing();

        if (details.Ingredients.Count == 0)
        {
            ImGui.TextColored(this.configuration.WarningTextColor, "No ingredients found for the selected recipes.");
            ImGui.TextDisabled("If this appears for normal craftable items, send Codex the debug line below.");
            var debugInfo = string.Join(
                Environment.NewLine,
                details.Recipes
                    .Select(recipe => recipe.DebugInfo)
                    .Where(info => !string.IsNullOrWhiteSpace(info)));
            if (!string.IsNullOrWhiteSpace(debugInfo))
                ImGui.TextWrapped(debugInfo);
            return;
        }

        var directIngredients = details.Ingredients
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        var rawMaterials = details.RawMaterials
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        var elementalCatalysts = details.RawMaterials
            .Where(IsElementalCatalyst)
            .ToList();

        if (directIngredients.Count > 0 &&
            this.DrawCollapsibleSection(
                "DIRECT INGREDIENTS",
                "Combined items used by the selected recipes",
                "direct-ingredients-section"))
            this.DrawMaterialsTable("ingredients", directIngredients);

        if (rawMaterials.Count > 0 &&
            this.DrawCollapsibleSection(
                "RAW MATERIALS",
                "Combined requirements from scratch",
                "raw-materials-section"))
            this.DrawMaterialsTable("raw-materials", rawMaterials);

        if (elementalCatalysts.Count > 0 &&
            this.DrawCollapsibleSection(
                "SHARDS, CRYSTALS & CLUSTERS",
                "Combined elemental catalyst requirements",
                "elemental-catalysts-section"))
            this.DrawMaterialsTable("elemental-catalysts", elementalCatalysts);

        this.DrawTravelPopup();
    }

    private void DrawSavePlanControls()
    {
        ImGui.SetNextItemWidth(190);
        var submitted = ImGui.InputTextWithHint(
            "##recipe-plan-name",
            "Plan name",
            ref this.planName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if (ImGui.Button("Save plan") || submitted)
            this.SaveCurrentPlan();

        this.DrawPlanMessage();
    }

    private void DrawPlanMessage()
    {
        if (!string.IsNullOrWhiteSpace(this.planMessage) &&
            DateTime.UtcNow < this.planMessageExpiresAt)
        {
            ImGui.TextColored(
                this.planMessageIsError
                    ? this.configuration.MissingTextColor
                    : this.configuration.SuccessTextColor,
                this.planMessage);
        }
        else
        {
            this.planMessage = string.Empty;
        }
    }

    private void DrawSavedPlans()
    {
        SavedRecipePlan? planToLoad = null;
        SavedRecipePlan? planToDuplicate = null;
        SavedRecipePlan? planToRename = null;
        SavedRecipePlan? planToDelete = null;
        if (ImGui.BeginTable(
                "saved-recipe-plans",
                3,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Plan", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Recipes", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 220);
            ImGui.TableHeadersRow();

            var displayedPlans = this.configuration.SavedRecipePlans
                .OrderBy(plan => plan.Name)
                .ToList();
            for (var planIndex = 0; planIndex < displayedPlans.Count; planIndex++)
            {
                var savedPlan = displayedPlans[planIndex];
                ImGui.PushID($"saved-plan-{planIndex}");
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(savedPlan.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(savedPlan.Recipes.Count.ToString());
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Load"))
                    planToLoad = savedPlan;
                ImGui.SameLine();
                if (ImGui.SmallButton("Duplicate"))
                    planToDuplicate = savedPlan;
                ImGui.SameLine();
                if (ImGui.SmallButton("Rename"))
                    planToRename = savedPlan;
                ImGui.SameLine();
                if (ImGui.SmallButton("Delete"))
                {
                    if (ImGui.GetIO().KeyCtrl)
                        planToDelete = savedPlan;
                    else
                        this.ShowPlanMessage("Hold Ctrl while clicking Delete to remove a saved plan.", true);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Hold Ctrl and click to delete this saved plan.");
                    ImGui.EndTooltip();
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (planToLoad is not null)
            this.LoadSavedPlan(planToLoad);

        if (planToDuplicate is not null)
            this.DuplicateSavedPlan(planToDuplicate);

        if (planToRename is not null)
        {
            this.renamingPlan = planToRename;
            this.renamePlanName = planToRename.Name;
            this.renamePlanError = string.Empty;
            this.isRenamePlanPopupOpen = true;
            this.renamePlanPopupRequested = true;
        }

        if (planToDelete is not null)
        {
            this.configuration.SavedRecipePlans.Remove(planToDelete);
            this.saveConfiguration();
            this.ShowPlanMessage($"Deleted plan '{planToDelete.Name}'.", false);
        }

        this.DrawRenamePlanPopup();
    }

    private void DuplicateSavedPlan(SavedRecipePlan source)
    {
        var baseName = $"{source.Name} Copy";
        var newName = baseName;
        var suffix = 2;
        while (this.configuration.SavedRecipePlans.Any(plan =>
                   string.Equals(plan.Name, newName, StringComparison.OrdinalIgnoreCase)))
            newName = $"{baseName} {suffix++}";

        this.configuration.SavedRecipePlans.Add(new SavedRecipePlan
        {
            Name = newName,
            Recipes = source.Recipes.Select(CloneSavedRecipe).ToList(),
        });
        this.saveConfiguration();
        this.ShowPlanMessage($"Duplicated plan as '{newName}'.", false);
    }

    private void DrawRenamePlanPopup()
    {
        if (this.renamePlanPopupRequested)
        {
            ImGui.OpenPopup("Rename saved plan");
            this.renamePlanPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Rename saved plan",
                ref this.isRenamePlanPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "New name",
            ref this.renamePlanName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);

        if (!string.IsNullOrWhiteSpace(this.renamePlanError))
            ImGui.TextColored(this.configuration.MissingTextColor, this.renamePlanError);

        if (ImGui.Button("Rename") || submitted)
            this.TryRenameSavedPlan();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            this.isRenamePlanPopupOpen = false;
            this.renamingPlan = null;
            this.renamePlanError = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void TryRenameSavedPlan()
    {
        if (this.renamingPlan is null)
            return;

        var cleanName = this.renamePlanName.Trim();
        if (cleanName.Length == 0)
        {
            this.renamePlanError = "Enter a name for this plan.";
            return;
        }

        if (this.configuration.SavedRecipePlans.Any(plan =>
                !ReferenceEquals(plan, this.renamingPlan) &&
                string.Equals(plan.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
        {
            this.renamePlanError = "A saved plan already uses that name.";
            return;
        }

        var oldName = this.renamingPlan.Name;
        this.renamingPlan.Name = cleanName;
        this.saveConfiguration();
        this.ShowPlanMessage($"Renamed plan '{oldName}' to '{cleanName}'.", false);
        this.isRenamePlanPopupOpen = false;
        this.renamingPlan = null;
        this.renamePlanError = string.Empty;
        ImGui.CloseCurrentPopup();
    }

    private static SavedRecipePlanEntry CloneSavedRecipe(SavedRecipePlanEntry recipe) =>
        new()
        {
            RecipeId = recipe.RecipeId,
            ResultItemId = recipe.ResultItemId,
            ResultName = recipe.ResultName,
            ResultAmount = recipe.ResultAmount,
            DesiredAmount = recipe.DesiredAmount,
        };

    private void SaveCurrentPlan()
    {
        var cleanName = this.planName.Trim();
        if (cleanName.Length == 0)
        {
            this.ShowPlanMessage("Enter a name before saving the plan.", true);
            return;
        }

        if (this.selectedRecipes.Count == 0)
        {
            this.ShowPlanMessage("Add at least one recipe before saving.", true);
            return;
        }

        var savedPlan = new SavedRecipePlan
        {
            Name = cleanName,
            Recipes = this.selectedRecipes.Select(selection =>
                new SavedRecipePlanEntry
                {
                    RecipeId = selection.Recipe.RecipeId,
                    ResultItemId = selection.Recipe.ResultItemId,
                    ResultName = selection.Recipe.ResultName,
                    ResultAmount = selection.Recipe.ResultAmount,
                    DesiredAmount = selection.DesiredAmount,
                }).ToList(),
        };
        var existingIndex = this.configuration.SavedRecipePlans.FindIndex(plan =>
            string.Equals(
                plan.Name,
                cleanName,
                StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            this.configuration.SavedRecipePlans[existingIndex] = savedPlan;
        else
            this.configuration.SavedRecipePlans.Add(savedPlan);

        this.saveConfiguration();
        this.ShowPlanMessage(
            existingIndex >= 0
                ? $"Updated plan '{cleanName}'."
                : $"Saved plan '{cleanName}'.",
            false);
        this.planName = string.Empty;
        this.selectedRecipes.Clear();
        this.integrationMessage = string.Empty;
        this.RefreshDetails(false);
    }

    private void LoadSavedPlan(SavedRecipePlan savedPlan)
    {
        this.selectedRecipes.Clear();
        foreach (var savedRecipe in savedPlan.Recipes)
        {
            this.selectedRecipes.Add(new RecipePlanSelection(
                new RecipeMatch(
                    savedRecipe.RecipeId,
                    savedRecipe.ResultItemId,
                    savedRecipe.ResultName,
                    Math.Max(1, savedRecipe.ResultAmount)),
                Math.Max(1, savedRecipe.DesiredAmount)));
        }

        this.planName = savedPlan.Name;
        this.ShowPlanMessage($"Loaded plan '{savedPlan.Name}'.", false);
        this.searchText = string.Empty;
        this.searchResults = [];
        this.RefreshDetails(true);
    }

    private void ShowPlanMessage(string message, bool isError)
    {
        this.planMessage = message;
        this.planMessageIsError = isError;
        this.planMessageExpiresAt = DateTime.UtcNow.AddSeconds(3);
    }

    private void DrawSelectedRecipes(RecipePlanDetails details)
    {
        var refreshPlan = false;
        uint? recipeToRemove = null;
        if (ImGui.BeginTable(
                "selected-recipes",
                4,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Output", ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 210);
            ImGui.TableHeadersRow();

            foreach (var recipe in details.Recipes)
            {
                var selectionIndex = this.selectedRecipes.FindIndex(
                    selection => selection.Recipe.RecipeId == recipe.RecipeId);
                if (selectionIndex < 0)
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(recipe.ResultName);
                ImGui.TextDisabled($"Recipe #{recipe.RecipeId}  Yield {recipe.ResultAmount}");

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var desiredAmount = (int)recipe.DesiredAmount;
                if (ImGui.InputInt($"##plan-amount-{recipe.RecipeId}", ref desiredAmount))
                {
                    this.selectedRecipes[selectionIndex] =
                        this.selectedRecipes[selectionIndex] with
                        {
                            DesiredAmount = (uint)Math.Clamp(desiredAmount, 1, 9999),
                        };
                    refreshPlan = true;
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(recipe.CraftCount.ToString());

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Craft Items##plan-artisan-{recipe.RecipeId}"))
                {
                    this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                        recipe.RecipeId,
                        recipe.CraftCount,
                        out this.integrationMessage);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton($"Teamcraft##plan-teamcraft-{recipe.RecipeId}"))
                {
                    this.integrationError = !this.pluginIntegrationService.OpenInTeamcraft(
                        recipe.ResultItemId,
                        recipe.DesiredAmount,
                        out this.integrationMessage);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##plan-remove-{recipe.RecipeId}"))
                    recipeToRemove = recipe.RecipeId;
            }

            ImGui.EndTable();
        }

        if (recipeToRemove is { } removeId)
        {
            this.selectedRecipes.RemoveAll(
                selection => selection.Recipe.RecipeId == removeId);
            refreshPlan = true;
        }

        if (refreshPlan)
            this.RefreshDetails(false);
    }

    private void DrawSummary(RecipePlanDetails details)
    {
        var totalCrafts = details.Recipes
            .Aggregate(0UL, (total, recipe) => total + recipe.CraftCount);
        var queuedCrafts = this.artisanCraftQueue.Count > 0
            ? this.artisanCraftQueue.Aggregate(
                0UL,
                (total, recipe) => total + recipe.CraftCount)
            : totalCrafts;
        var intermediateCrafts = queuedCrafts > totalCrafts
            ? queuedCrafts - totalCrafts
            : 0;
        var estimatedSeconds =
            queuedCrafts *
            (ulong)Math.Clamp(this.configuration.EstimatedSecondsPerCraft, 5, 120);
        if (!ImGui.BeginTable(
                "recipe-summary",
                4,
                ImGuiTableFlags.SizingStretchSame |
                ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.PadOuterX))
            return;

        ImGui.TableNextColumn();
        ImGui.TextDisabled("RECIPES");
        ImGui.TextUnformatted(details.Recipes.Count.ToString());
        ImGui.TableNextColumn();
        ImGui.TextDisabled("TOTAL CRAFTS");
        ImGui.TextUnformatted(totalCrafts.ToString());
        ImGui.TableNextColumn();
        ImGui.TextDisabled("EST. CRAFT TIME");
        ImGui.TextUnformatted(FormatDuration(estimatedSeconds));
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(
                $"Based on {this.configuration.EstimatedSecondsPerCraft} seconds per craft.");
            if (intermediateCrafts > 0)
                ImGui.TextUnformatted(
                    $"Includes {intermediateCrafts:N0} intermediate craft(s).");
            ImGui.TextDisabled("Actual Artisan time varies by recipe, solution, and character stats.");
            ImGui.EndTooltip();
        }
        ImGui.TableNextColumn();
        ImGui.TextDisabled("RAW ITEMS");
        ImGui.TextUnformatted(details.RawMaterials.Count.ToString());
        ImGui.EndTable();
    }

    private static string FormatDuration(ulong totalSeconds)
    {
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"{hours}h {minutes}m {seconds}s"
            : minutes > 0
                ? $"{minutes}m {seconds}s"
                : $"{seconds}s";
    }

    private bool DrawCollapsibleSection(string title, string subtitle, string sectionId)
    {
        ImGui.Spacing();
        var isOpen = ImGui.CollapsingHeader(
            $"{title}##{sectionId}",
            ImGuiTreeNodeFlags.DefaultOpen);
        if (!isOpen)
            return false;

        ImGui.TextDisabled(subtitle);
        ImGui.Spacing();
        return true;
    }

    private static bool IsElementalCatalyst(IngredientNeed material) =>
        material.ItemId is >= 2 and <= 19;

    private void DrawMaterialsTable(string tableId, IReadOnlyList<IngredientNeed> materials)
    {
        var displayedMaterials = this.aetherialReductionService.OrderByAvailability(
            materials.Where(material =>
                material.ItemId != 0 &&
                material.Required > 0 &&
                !string.IsNullOrWhiteSpace(material.Name)));
        if (displayedMaterials.Count == 0)
            return;

        var isDirectIngredients = tableId == "ingredients";
        var showTravel = displayedMaterials.Any(material => material.IsGatherable);
        var showAvailable = displayedMaterials.Any(material =>
            material.ReductionSources is { Count: > 0 } ||
            this.aetherialReductionService.GetGatheringTimerText(material.ItemId) is not null);
        var showNq = displayedMaterials.Any(material => material.OwnedNq > 0);
        var showHq = displayedMaterials.Any(material => material.OwnedHq > 0);
        var showFoundIn = displayedMaterials.Any(material => material.Locations.Count > 0);
        var showRawCraftStatus =
            isDirectIngredients &&
            displayedMaterials.Any(material => material.CanCraftMissingFromRaw.HasValue);
        const bool showMissing = true;
        var columnCount =
            3 +
            (showTravel ? 1 : 0) +
            (showAvailable ? 1 : 0) +
            (showNq ? 1 : 0) +
            (showHq ? 1 : 0) +
            (showFoundIn ? 1 : 0) +
            (showRawCraftStatus ? 1 : 0) +
            (showMissing ? 1 : 0);
        var tableFlags =
            ImGuiTableFlags.BordersOuterH |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingFixedFit;
        var requestedTableWidth =
            140f +
            110f +
            45f +
            (showTravel ? 70f : 0f) +
            (showAvailable ? 115f : 0f) +
            (showNq ? 45f : 0f) +
            (showHq ? 45f : 0f) +
            (showFoundIn ? 150f : 0f) +
            (showRawCraftStatus ? 110f : 0f) +
            (showMissing ? 75f : 0f) +
            (ImGui.GetStyle().CellPadding.X * 2 * columnCount) +
            2f;
        var availableTableWidth = ImGui.GetContentRegionAvail().X;
        var needsHorizontalScroll = requestedTableWidth > availableTableWidth;
        if (needsHorizontalScroll)
            tableFlags |= ImGuiTableFlags.ScrollX;

        var lastColumnKey = showRawCraftStatus
            ? "raw"
            : showFoundIn
                ? "found"
                : showHq
                    ? "hq"
                    : showNq
                        ? "nq"
                        : showAvailable
                            ? "available"
                            : showTravel
                                ? "travel"
                                : "missing";
        void SetupColumn(string label, string key, float width)
        {
            var stretch = !needsHorizontalScroll && key == lastColumnKey;
            ImGui.TableSetupColumn(
                label,
                stretch
                    ? ImGuiTableColumnFlags.WidthStretch
                    : ImGuiTableColumnFlags.WidthFixed,
                stretch ? 1f : width);
        }

        var tableHeight =
            (25f * (displayedMaterials.Count + 1)) +
            (needsHorizontalScroll ? ImGui.GetStyle().ScrollbarSize : 0f) +
            2f;
        if (ImGui.BeginTable(
                tableId,
                columnCount,
                tableFlags,
                new Vector2(Math.Max(1f, availableTableWidth), tableHeight)))
        {
            SetupColumn("Ingredient", "ingredient", 140);
            SetupColumn("Source", "source", 110);
            SetupColumn("Need", "need", 45);
            if (showMissing)
                SetupColumn("Missing", "missing", 75);
            if (showTravel)
                SetupColumn("Travel", "travel", 70);
            if (showAvailable)
                SetupColumn("Available", "available", 115);
            if (showNq)
                SetupColumn("NQ", "nq", 45);
            if (showHq)
                SetupColumn("HQ", "hq", 45);
            if (showFoundIn)
                SetupColumn("Found in", "found", 150);
            if (showRawCraftStatus)
                SetupColumn("From raw", "raw", 110);
            ImGui.TableHeadersRow();

            foreach (var ingredient in displayedMaterials)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 25);
                var hasTimedWindow = this.aetherialReductionService.HasTimedWindow(
                    ingredient.ItemId,
                    ingredient.ReductionSources);
                var highlightRow =
                    ingredient.HasEnough ||
                    (hasTimedWindow &&
                     this.aetherialReductionService.IsCurrentlyAvailable(
                        ingredient.ItemId,
                        ingredient.ReductionSources));
                if (highlightRow)
                    ImGui.TableSetBgColor(
                        ImGuiTableBgTarget.RowBg0,
                        ImGui.GetColorU32(this.configuration.EnoughRowColor));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ingredient.Name);
                MaterialUsageTooltip.Draw(
                    this.recipeService,
                    this.configuration,
                    ingredient);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ingredient.Source);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ingredient.Required.ToString());

                if (showMissing)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.HasEnough)
                        ImGui.TextUnformatted(string.Empty);
                    else if (showRawCraftStatus && ingredient.CanCraftMissingFromRaw is true)
                        ImGui.TextUnformatted(string.Empty);
                    else
                        ImGui.TextColored(this.configuration.MissingTextColor, ingredient.Missing.ToString());
                }

                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(ingredient.ReductionSources);
                if (showTravel)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.IsGatherable &&
                        !ingredient.HasEnough &&
                        ImGui.SmallButton($"Gather##{tableId}-{ingredient.ItemId}"))
                    {
                        this.integrationError = !this.pluginIntegrationService.GatherWithGatherBuddy(
                            reductionSource?.Name ?? ingredient.Name,
                            reductionSource?.IsFishing ?? ingredient.IsFishing,
                            out this.integrationMessage);

                        if (this.integrationError)
                            this.OpenTravelPopup(ingredient, reductionSource);
                        else
                            this.IsOpen = false;
                    }
                    else if (!ingredient.IsGatherable || ingredient.HasEnough)
                    {
                        ImGui.TextDisabled("-");
                    }
                }

                if (showAvailable)
                {
                    ImGui.TableNextColumn();
                    if (reductionSource is not null)
                        ImGui.TextUnformatted(this.aetherialReductionService.GetTimerText(reductionSource));
                    else if (this.aetherialReductionService.GetGatheringTimerText(ingredient.ItemId) is { } gatheringTimer)
                        ImGui.TextUnformatted(gatheringTimer);
                    else
                        ImGui.TextDisabled("-");
                }

                if (showNq)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(ingredient.OwnedNq.ToString());
                }

                if (showHq)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(ingredient.OwnedHq.ToString());
                }

                if (showFoundIn)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.Locations.Count > 0)
                        ImGui.TextUnformatted(string.Join(", ", ingredient.Locations));
                    else
                        ImGui.TextDisabled("-");
                }

                if (showRawCraftStatus)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.HasEnough)
                        ImGui.TextDisabled("Owned");
                    else if (ingredient.CanCraftMissingFromRaw is true &&
                             ingredient.RawCraftRecipeId is { } rawCraftRecipeId)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, this.configuration.ReadyButtonColor);
                        var readyToCraftClicked =
                            ImGui.SmallButton($"Ready to craft##{tableId}-raw-{ingredient.ItemId}");
                        ImGui.PopStyleColor();
                        if (readyToCraftClicked)
                        {
                            this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                                rawCraftRecipeId,
                                ingredient.RawCraftCount,
                                out this.integrationMessage);
                        }
                    }
                }

            }

            ImGui.EndTable();
        }
    }

    private void OpenTravelPopup(
        IngredientNeed ingredient,
        AetherialReductionSource? reductionSource = null)
    {
        this.gatheringItemName = reductionSource?.Name ?? ingredient.Name;
        this.gatheringDestinations = this.travelService.GetDestinations(
            reductionSource?.ItemId ?? ingredient.ItemId);
        this.travelMessage = string.Empty;
        this.isTravelPopupOpen = true;
        this.travelPopupRequested = true;
    }

    private void DrawTravelPopup()
    {
        if (this.travelPopupRequested)
        {
            ImGui.OpenPopup("Gathering locations");
            this.travelPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Gathering locations",
                ref this.isTravelPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted(this.gatheringItemName);
        ImGui.Separator();

        if (this.gatheringDestinations.Count == 0)
        {
            ImGui.TextColored(
                this.configuration.WarningTextColor,
                "No gathering location was found in the game data.");
        }

        for (var i = 0; i < this.gatheringDestinations.Count; i++)
        {
            var destination = this.gatheringDestinations[i];
            ImGui.PushID(i);
            ImGui.TextUnformatted($"{destination.ZoneName} - {destination.LocationName}");

            if (destination.AetheryteId is not null)
            {
                ImGui.TextDisabled($"{destination.AetheryteName} ({destination.TeleportCost:N0} gil)");
                if (destination.ItemId <= ushort.MaxValue)
                {
                    if (ImGui.Button("Show map"))
                    {
                        if (this.travelService.ShowOnMap(destination))
                        {
                            this.isTravelPopupOpen = false;
                            this.IsOpen = false;
                            ImGui.CloseCurrentPopup();
                        }
                        else
                        {
                            this.travelMessage = "The map could not be opened for this location.";
                        }
                    }

                    ImGui.SameLine();
                }

                if (ImGui.Button("Teleport"))
                {
                    if (this.travelService.Teleport(destination))
                    {
                        this.isTravelPopupOpen = false;
                        this.IsOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else
                    {
                        this.travelMessage = "Teleport could not be started right now.";
                    }
                }
            }
            else
            {
                ImGui.TextColored(
                    this.configuration.WarningTextColor,
                    "No unlocked aetheryte is available in this zone.");
            }

            ImGui.PopID();
            if (i + 1 < this.gatheringDestinations.Count)
                ImGui.Separator();
        }

        if (!string.IsNullOrWhiteSpace(this.travelMessage))
        {
            ImGui.Spacing();
            ImGui.TextColored(this.configuration.MissingTextColor, this.travelMessage);
        }

        ImGui.Spacing();
        if (ImGui.Button("Close"))
        {
            this.isTravelPopupOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void OnInventoryChanged() => this.inventoryRefreshRequested = true;
}
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace DalamudRecipeHelper;

public sealed class RecipeWindow : Window, IDisposable
{
    private readonly RecipeService recipeService;
    private readonly InventoryService inventoryService;
    private readonly TravelService travelService;
    private readonly PluginIntegrationService pluginIntegrationService;
    private readonly AetherialReductionService aetherialReductionService;
    private readonly Configuration configuration;
    private readonly Action openSettings;
    private readonly Action saveConfiguration;
    private readonly RawMaterialsOverlayWindow rawMaterialsOverlayWindow;
    private IReadOnlyList<RecipeMatch> searchResults = [];
    private IReadOnlyDictionary<uint, CraftableRecipeAvailability> craftableAvailability =
        new Dictionary<uint, CraftableRecipeAvailability>();
    private bool showingCraftableRecipes;
    private readonly List<RecipePlanSelection> selectedRecipes = [];
    private string searchText = string.Empty;
    private RecipePlanDetails? recipePlanDetails;
    private IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems = new Dictionary<uint, OwnedInventoryItem>();
    private IReadOnlyList<GatheringDestination> gatheringDestinations = [];
    private string gatheringItemName = string.Empty;
    private string travelMessage = string.Empty;
    private bool isTravelPopupOpen;
    private bool travelPopupRequested;
    private string integrationMessage = string.Empty;
    private bool integrationError;
    private bool inventoryRefreshRequested;
    private string planName = string.Empty;
    private string planMessage = string.Empty;
    private bool planMessageIsError;
    private DateTime planMessageExpiresAt;
    private bool wasResizingSearchPane;
    private SavedRecipePlan? renamingPlan;
    private string renamePlanName = string.Empty;
    private string renamePlanError = string.Empty;
    private bool renamePlanPopupRequested;
    private bool isRenamePlanPopupOpen;

    public RecipeWindow(
        RecipeService recipeService,
        InventoryService inventoryService,
        TravelService travelService,
        PluginIntegrationService pluginIntegrationService,
        AetherialReductionService aetherialReductionService,
        Configuration configuration,
        Action openSettings,
        Action saveConfiguration,
        RawMaterialsOverlayWindow rawMaterialsOverlayWindow)
        : base("Recipe Helper###DalamudRecipeHelper")
    {
        this.recipeService = recipeService;
        this.inventoryService = inventoryService;
        this.travelService = travelService;
        this.pluginIntegrationService = pluginIntegrationService;
        this.aetherialReductionService = aetherialReductionService;
        this.configuration = configuration;
        this.openSettings = openSettings;
        this.saveConfiguration = saveConfiguration;
        this.rawMaterialsOverlayWindow = rawMaterialsOverlayWindow;
        this.inventoryService.InventoryChanged += this.OnInventoryChanged;
        this.Size = new Vector2(760, 540);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 320),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public string SearchText
    {
        get => this.searchText;
        set => this.searchText = value;
    }

    public override void PreDraw() => WindowTheme.Push(this.configuration);

    public override void PostDraw() => WindowTheme.Pop();

    public void Dispose()
    {
        this.inventoryService.InventoryChanged -= this.OnInventoryChanged;
        this.inventoryService.Dispose();
        this.travelService.Dispose();
    }

    public override void Draw()
    {
        if (this.inventoryRefreshRequested)
        {
            this.inventoryRefreshRequested = false;
            this.RefreshDetails(true);
        }

        this.PushModernStyle();
        try
        {
            this.DrawHeader();

            var paneArea = ImGui.GetContentRegionAvail();
            const float splitterWidth = 5f;
            var paneSpacing = ImGui.GetStyle().ItemSpacing.X;
            var maximumSearchWidth = Math.Max(
                150f,
                paneArea.X - splitterWidth - (paneSpacing * 2) - 260f);
            var leftWidth = Math.Clamp(
                this.configuration.SearchPaneWidth,
                150f,
                maximumSearchWidth);
            if (ImGui.BeginChild("results", new Vector2(leftWidth, 0), true))
            {
                ImGui.TextColored(
                    this.configuration.AccentColor,
                    this.showingCraftableRecipes ? "CRAFTABLE NOW" : "RECIPES");
                ImGui.SameLine();
                ImGui.TextDisabled($"  {this.searchResults.Count}");
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (this.searchResults.Count == 0)
                {
                    if (this.showingCraftableRecipes)
                    {
                        ImGui.TextDisabled("No recipes can currently");
                        ImGui.TextDisabled("be made from stored items.");
                    }
                    else
                    {
                        ImGui.TextDisabled("Search for a crafted item");
                        ImGui.TextDisabled("to begin planning.");
                    }
                }

                foreach (var result in this.searchResults)
                {
                    var isSelected = this.selectedRecipes.Any(
                        selection => selection.Recipe.RecipeId == result.RecipeId);
                    var subtitle = this.showingCraftableRecipes &&
                                   this.craftableAvailability.TryGetValue(
                                       result.RecipeId,
                                       out var availability)
                        ? $"{availability.CraftCount:N0} crafts  •  {availability.OutputAmount:N0} items"
                        : isSelected
                            ? "Added to plan"
                            : $"Click to add  •  Yield {result.ResultAmount}";
                    var label = $"{result.ResultName}\n{subtitle}##{result.RecipeId}";
                    if (ImGui.Selectable(
                            label,
                            isSelected,
                            ImGuiSelectableFlags.None,
                            new Vector2(0, 34)))
                        this.AddRecipe(result);
                }
            }

            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                WithAlpha(this.configuration.AccentColor, 0.22f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonHovered,
                WithAlpha(this.configuration.AccentColor, 0.55f));
            ImGui.PushStyleColor(
                ImGuiCol.ButtonActive,
                WithAlpha(this.configuration.AccentColor, 0.78f));
            ImGui.Button(
                "##recipe-search-pane-splitter",
                new Vector2(splitterWidth, Math.Max(1f, paneArea.Y)));
            ImGui.PopStyleColor(3);

            var isResizingSearchPane = ImGui.IsItemActive();
            if (ImGui.IsItemHovered() || isResizingSearchPane)
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Drag to resize the recipe search area.");
            if (isResizingSearchPane)
            {
                this.configuration.SearchPaneWidth = Math.Clamp(
                    leftWidth + ImGui.GetIO().MouseDelta.X,
                    150f,
                    maximumSearchWidth);
            }

            if (this.wasResizingSearchPane && !isResizingSearchPane)
                this.saveConfiguration();
            this.wasResizingSearchPane = isResizingSearchPane;

            ImGui.SameLine();

            if (ImGui.BeginChild("details", Vector2.Zero, true))
                this.DrawDetails();

            ImGui.EndChild();
        }
        finally
        {
            ImGui.PopStyleColor(7);
            ImGui.PopStyleVar(5);
        }
    }

    private void DrawHeader()
    {
        ImGui.TextColored(this.configuration.AccentColor, "RECIPE HELPER");
        ImGui.SameLine();
        ImGui.TextDisabled("Plan  •  Check  •  Craft");

        var searchButtonWidth = ImGui.CalcTextSize("Search").X + 20;
        var craftableButtonWidth = ImGui.CalcTextSize("Can craft").X + 20;
        var settingsButtonWidth = ImGui.CalcTextSize("Settings").X + 20;
        var searchWidth = MathF.Max(
            150,
            ImGui.GetContentRegionAvail().X -
            searchButtonWidth -
            craftableButtonWidth -
            settingsButtonWidth -
            24);

        ImGui.SetNextItemWidth(searchWidth);
        var searchChanged = ImGui.InputTextWithHint(
            "##recipe-search",
            "Search crafted item or item ID",
            ref this.searchText,
            128);
        ImGui.SameLine();
        if (ImGui.Button("Search", new Vector2(searchButtonWidth, 0)) ||
            searchChanged && this.searchText.Length >= 3)
            this.RefreshSearch();

        ImGui.SameLine();
        if (ImGui.Button("Can craft", new Vector2(craftableButtonWidth, 0)))
            this.RefreshCraftableRecipes(true);

        ImGui.SameLine();
        if (ImGui.Button("Settings", new Vector2(settingsButtonWidth, 0)))
            this.openSettings();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void PushModernStyle()
    {
        var accent = this.configuration.AccentColor;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(7, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 4));

        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(accent, 0.72f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AdjustColor(accent, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AdjustColor(accent, -0.08f));
        ImGui.PushStyleColor(ImGuiCol.Header, WithAlpha(accent, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, WithAlpha(accent, 0.48f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, WithAlpha(accent, 0.62f));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, WithAlpha(accent, 0.22f));
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);

    private static Vector4 AdjustColor(Vector4 color, float amount) =>
        new(
            Math.Clamp(color.X + amount, 0, 1),
            Math.Clamp(color.Y + amount, 0, 1),
            Math.Clamp(color.Z + amount, 0, 1),
            color.W);

    public void RefreshSearch()
    {
        this.showingCraftableRecipes = false;
        this.craftableAvailability =
            new Dictionary<uint, CraftableRecipeAvailability>();
        this.searchResults = this.recipeService.Search(this.searchText);
    }

    private void RefreshCraftableRecipes(bool scanInventory)
    {
        if (scanInventory)
            this.ownedItems = this.inventoryService.GetOwnedItems();

        this.showingCraftableRecipes = true;
        this.searchText = string.Empty;
        var available = this.recipeService.GetCraftableRecipes(this.ownedItems);
        this.craftableAvailability = available.ToDictionary(
            entry => entry.Recipe.RecipeId);
        this.searchResults = available
            .Select(entry => entry.Recipe)
            .ToList();
    }

    private void AddRecipe(RecipeMatch recipe)
    {
        if (this.selectedRecipes.Any(
                selection => selection.Recipe.RecipeId == recipe.RecipeId))
            return;

        this.selectedRecipes.Add(new RecipePlanSelection(
            recipe,
            Math.Max(1, recipe.ResultAmount)));
        if (!this.showingCraftableRecipes)
        {
            this.searchText = string.Empty;
            this.searchResults = [];
        }
        this.RefreshDetails(true);
    }

    private void RefreshDetails(bool scanInventory)
    {
        if (scanInventory)
        {
            this.ownedItems = this.inventoryService.GetOwnedItems();
            if (this.showingCraftableRecipes)
                this.RefreshCraftableRecipes(false);
        }

        if (this.selectedRecipes.Count == 0)
        {
            this.recipePlanDetails = null;
            this.rawMaterialsOverlayWindow.SetMaterials([], []);
            return;
        }

        this.recipePlanDetails = this.recipeService.GetPlanDetails(
            this.selectedRecipes,
            this.ownedItems);
        if (this.recipePlanDetails is { } details)
        {
            this.rawMaterialsOverlayWindow.SetMaterials(
                details.Recipes.Select(recipe => recipe.ResultName).ToList(),
                details.RawMaterials);
        }
    }

    private void DrawDetails()
    {
        if (this.recipePlanDetails is null)
        {
            ImGui.Dummy(new Vector2(0, 24));
            ImGui.TextColored(
                this.configuration.AccentColor,
                this.showingCraftableRecipes
                    ? $"{this.searchResults.Count} recipes craftable from current stock"
                    : "No recipes selected");
            ImGui.TextDisabled(
                this.showingCraftableRecipes
                    ? "Select a recipe from the list. Materials only; job level and recipe unlocks are not checked."
                    : "Choose one or more recipes from the list to build a combined plan.");
            this.DrawPlanMessage();
            if (this.configuration.SavedRecipePlans.Count > 0 &&
                this.DrawCollapsibleSection(
                    "SAVED PLANS",
                    "Load, duplicate, rename, or delete a named recipe plan",
                    "saved-plans-empty-section"))
                this.DrawSavedPlans();
            return;
        }

        var details = this.recipePlanDetails;
        ImGui.TextColored(this.configuration.AccentColor, "RECIPE PLAN");
        ImGui.SameLine();
        ImGui.TextDisabled($"{details.Recipes.Count} selected");
        this.DrawSavePlanControls();
        if (this.configuration.SavedRecipePlans.Count > 0 &&
            this.DrawCollapsibleSection(
                "SAVED PLANS",
                "Load, duplicate, rename, or delete a named recipe plan",
                "saved-plans-section"))
            this.DrawSavedPlans();

        if (this.DrawCollapsibleSection(
                "PLAN SUMMARY",
                "Combined recipe, craft, and raw-item totals",
                "plan-summary-section"))
            this.DrawSummary(details);

        if (this.DrawCollapsibleSection(
                "SELECTED RECIPES",
                "Output quantities and recipe actions",
                "selected-recipes-section"))
            this.DrawSelectedRecipes(details);

        ImGui.Spacing();
        var canCraftAll =
            details.Recipes.Count > 1 &&
            details.Ingredients.Count > 0 &&
            details.Ingredients.All(ingredient => ingredient.HasEnough);
        if (canCraftAll)
        {
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                this.configuration.ReadyButtonColor);
            var craftAllClicked = ImGui.Button("Craft all with Artisan");
            ImGui.PopStyleColor();
            if (craftAllClicked)
            {
                this.integrationError =
                    !this.pluginIntegrationService.CraftAllWithArtisan(
                        details.Recipes,
                        out this.integrationMessage);
            }

            ImGui.SameLine();
        }

        if (ImGui.Button("Missing Items Overlay"))
            this.rawMaterialsOverlayWindow.IsOpen = true;

        if (!string.IsNullOrWhiteSpace(this.integrationMessage))
        {
            ImGui.TextColored(
                this.integrationError
                    ? this.configuration.MissingTextColor
                    : this.configuration.SuccessTextColor,
                this.integrationMessage);
        }

        if (ImGui.Button("Refresh inventory"))
            this.RefreshDetails(true);

        ImGui.SameLine();
        ImGui.TextDisabled(
            $"{this.inventoryService.LastItemStacks} stacks  •  " +
            $"{this.inventoryService.LastScannedContainers} live containers  •  " +
            $"{this.inventoryService.LastStoredRetainers} retainers saved");

        ImGui.Spacing();

        if (details.Ingredients.Count == 0)
        {
            ImGui.TextColored(this.configuration.WarningTextColor, "No ingredients found for the selected recipes.");
            ImGui.TextDisabled("If this appears for normal craftable items, send Codex the debug line below.");
            var debugInfo = string.Join(
                Environment.NewLine,
                details.Recipes
                    .Select(recipe => recipe.DebugInfo)
                    .Where(info => !string.IsNullOrWhiteSpace(info)));
            if (!string.IsNullOrWhiteSpace(debugInfo))
                ImGui.TextWrapped(debugInfo);
            return;
        }

        var directIngredients = details.Ingredients
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        var rawMaterials = details.RawMaterials
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        var elementalCatalysts = details.RawMaterials
            .Where(IsElementalCatalyst)
            .ToList();

        if (directIngredients.Count > 0 &&
            this.DrawCollapsibleSection(
                "DIRECT INGREDIENTS",
                "Combined items used by the selected recipes",
                "direct-ingredients-section"))
            this.DrawMaterialsTable("ingredients", directIngredients);

        if (rawMaterials.Count > 0 &&
            this.DrawCollapsibleSection(
                "RAW MATERIALS",
                "Combined requirements from scratch",
                "raw-materials-section"))
            this.DrawMaterialsTable("raw-materials", rawMaterials);

        if (elementalCatalysts.Count > 0 &&
            this.DrawCollapsibleSection(
                "SHARDS, CRYSTALS & CLUSTERS",
                "Combined elemental catalyst requirements",
                "elemental-catalysts-section"))
            this.DrawMaterialsTable("elemental-catalysts", elementalCatalysts);

        this.DrawTravelPopup();
    }

    private void DrawSavePlanControls()
    {
        ImGui.SetNextItemWidth(190);
        ImGui.InputTextWithHint(
            "##recipe-plan-name",
            "Plan name",
            ref this.planName,
            80);
        ImGui.SameLine();
        if (ImGui.Button("Save plan"))
            this.SaveCurrentPlan();

        this.DrawPlanMessage();
    }

    private void DrawPlanMessage()
    {
        if (!string.IsNullOrWhiteSpace(this.planMessage) &&
            DateTime.UtcNow < this.planMessageExpiresAt)
        {
            ImGui.TextColored(
                this.planMessageIsError
                    ? this.configuration.MissingTextColor
                    : this.configuration.SuccessTextColor,
                this.planMessage);
        }
        else
        {
            this.planMessage = string.Empty;
        }
    }

    private void DrawSavedPlans()
    {
        SavedRecipePlan? planToLoad = null;
        SavedRecipePlan? planToDuplicate = null;
        SavedRecipePlan? planToRename = null;
        SavedRecipePlan? planToDelete = null;
        if (ImGui.BeginTable(
                "saved-recipe-plans",
                3,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Plan", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Recipes", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 135);
            ImGui.TableHeadersRow();

            var displayedPlans = this.configuration.SavedRecipePlans
                .OrderBy(plan => plan.Name)
                .ToList();
            for (var planIndex = 0; planIndex < displayedPlans.Count; planIndex++)
            {
                var savedPlan = displayedPlans[planIndex];
                ImGui.PushID($"saved-plan-{planIndex}");
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(savedPlan.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(savedPlan.Recipes.Count.ToString());
                ImGui.TableNextColumn();
                if (ImGui.SmallButton("Load"))
                    planToLoad = savedPlan;
                ImGui.SameLine();
                if (ImGui.SmallButton("Duplicate"))
                    planToDuplicate = savedPlan;
                if (ImGui.SmallButton("Rename"))
                    planToRename = savedPlan;
                ImGui.SameLine();
                if (ImGui.SmallButton("Delete"))
                {
                    if (ImGui.GetIO().KeyCtrl)
                        planToDelete = savedPlan;
                    else
                        this.ShowPlanMessage("Hold Ctrl while clicking Delete to remove a saved plan.", true);
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Hold Ctrl and click to delete this saved plan.");
                    ImGui.EndTooltip();
                }

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        if (planToLoad is not null)
            this.LoadSavedPlan(planToLoad);

        if (planToDuplicate is not null)
            this.DuplicateSavedPlan(planToDuplicate);

        if (planToRename is not null)
        {
            this.renamingPlan = planToRename;
            this.renamePlanName = planToRename.Name;
            this.renamePlanError = string.Empty;
            this.isRenamePlanPopupOpen = true;
            this.renamePlanPopupRequested = true;
        }

        if (planToDelete is not null)
        {
            this.configuration.SavedRecipePlans.Remove(planToDelete);
            this.saveConfiguration();
            this.ShowPlanMessage($"Deleted plan '{planToDelete.Name}'.", false);
        }

        this.DrawRenamePlanPopup();
    }

    private void DuplicateSavedPlan(SavedRecipePlan source)
    {
        var baseName = $"{source.Name} Copy";
        var newName = baseName;
        var suffix = 2;
        while (this.configuration.SavedRecipePlans.Any(plan =>
                   string.Equals(plan.Name, newName, StringComparison.OrdinalIgnoreCase)))
            newName = $"{baseName} {suffix++}";

        this.configuration.SavedRecipePlans.Add(new SavedRecipePlan
        {
            Name = newName,
            Recipes = source.Recipes.Select(CloneSavedRecipe).ToList(),
        });
        this.saveConfiguration();
        this.ShowPlanMessage($"Duplicated plan as '{newName}'.", false);
    }

    private void DrawRenamePlanPopup()
    {
        if (this.renamePlanPopupRequested)
        {
            ImGui.OpenPopup("Rename saved plan");
            this.renamePlanPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Rename saved plan",
                ref this.isRenamePlanPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "New name",
            ref this.renamePlanName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);

        if (!string.IsNullOrWhiteSpace(this.renamePlanError))
            ImGui.TextColored(this.configuration.MissingTextColor, this.renamePlanError);

        if (ImGui.Button("Rename") || submitted)
            this.TryRenameSavedPlan();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            this.isRenamePlanPopupOpen = false;
            this.renamingPlan = null;
            this.renamePlanError = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void TryRenameSavedPlan()
    {
        if (this.renamingPlan is null)
            return;

        var cleanName = this.renamePlanName.Trim();
        if (cleanName.Length == 0)
        {
            this.renamePlanError = "Enter a name for this plan.";
            return;
        }

        if (this.configuration.SavedRecipePlans.Any(plan =>
                !ReferenceEquals(plan, this.renamingPlan) &&
                string.Equals(plan.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
        {
            this.renamePlanError = "A saved plan already uses that name.";
            return;
        }

        var oldName = this.renamingPlan.Name;
        this.renamingPlan.Name = cleanName;
        this.saveConfiguration();
        this.ShowPlanMessage($"Renamed plan '{oldName}' to '{cleanName}'.", false);
        this.isRenamePlanPopupOpen = false;
        this.renamingPlan = null;
        this.renamePlanError = string.Empty;
        ImGui.CloseCurrentPopup();
    }

    private static SavedRecipePlanEntry CloneSavedRecipe(SavedRecipePlanEntry recipe) =>
        new()
        {
            RecipeId = recipe.RecipeId,
            ResultItemId = recipe.ResultItemId,
            ResultName = recipe.ResultName,
            ResultAmount = recipe.ResultAmount,
            DesiredAmount = recipe.DesiredAmount,
        };

    private void SaveCurrentPlan()
    {
        var cleanName = this.planName.Trim();
        if (cleanName.Length == 0)
        {
            this.ShowPlanMessage("Enter a name before saving the plan.", true);
            return;
        }

        if (this.selectedRecipes.Count == 0)
        {
            this.ShowPlanMessage("Add at least one recipe before saving.", true);
            return;
        }

        var savedPlan = new SavedRecipePlan
        {
            Name = cleanName,
            Recipes = this.selectedRecipes.Select(selection =>
                new SavedRecipePlanEntry
                {
                    RecipeId = selection.Recipe.RecipeId,
                    ResultItemId = selection.Recipe.ResultItemId,
                    ResultName = selection.Recipe.ResultName,
                    ResultAmount = selection.Recipe.ResultAmount,
                    DesiredAmount = selection.DesiredAmount,
                }).ToList(),
        };
        var existingIndex = this.configuration.SavedRecipePlans.FindIndex(plan =>
            string.Equals(
                plan.Name,
                cleanName,
                StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            this.configuration.SavedRecipePlans[existingIndex] = savedPlan;
        else
            this.configuration.SavedRecipePlans.Add(savedPlan);

        this.saveConfiguration();
        this.ShowPlanMessage(
            existingIndex >= 0
                ? $"Updated plan '{cleanName}'."
                : $"Saved plan '{cleanName}'.",
            false);
        this.planName = string.Empty;
        this.selectedRecipes.Clear();
        this.integrationMessage = string.Empty;
        this.RefreshDetails(false);
    }

    private void LoadSavedPlan(SavedRecipePlan savedPlan)
    {
        this.selectedRecipes.Clear();
        foreach (var savedRecipe in savedPlan.Recipes)
        {
            this.selectedRecipes.Add(new RecipePlanSelection(
                new RecipeMatch(
                    savedRecipe.RecipeId,
                    savedRecipe.ResultItemId,
                    savedRecipe.ResultName,
                    Math.Max(1, savedRecipe.ResultAmount)),
                Math.Max(1, savedRecipe.DesiredAmount)));
        }

        this.planName = savedPlan.Name;
        this.ShowPlanMessage($"Loaded plan '{savedPlan.Name}'.", false);
        this.searchText = string.Empty;
        this.searchResults = [];
        this.RefreshDetails(true);
    }

    private void ShowPlanMessage(string message, bool isError)
    {
        this.planMessage = message;
        this.planMessageIsError = isError;
        this.planMessageExpiresAt = DateTime.UtcNow.AddSeconds(3);
    }

    private void DrawSelectedRecipes(RecipePlanDetails details)
    {
        var refreshPlan = false;
        uint? recipeToRemove = null;
        if (ImGui.BeginTable(
                "selected-recipes",
                4,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableSetupColumn("Output", ImGuiTableColumnFlags.WidthFixed, 75);
            ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 210);
            ImGui.TableHeadersRow();

            foreach (var recipe in details.Recipes)
            {
                var selectionIndex = this.selectedRecipes.FindIndex(
                    selection => selection.Recipe.RecipeId == recipe.RecipeId);
                if (selectionIndex < 0)
                    continue;

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(recipe.ResultName);
                ImGui.TextDisabled($"Recipe #{recipe.RecipeId}  Yield {recipe.ResultAmount}");

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(-1);
                var desiredAmount = (int)recipe.DesiredAmount;
                if (ImGui.InputInt($"##plan-amount-{recipe.RecipeId}", ref desiredAmount))
                {
                    this.selectedRecipes[selectionIndex] =
                        this.selectedRecipes[selectionIndex] with
                        {
                            DesiredAmount = (uint)Math.Clamp(desiredAmount, 1, 9999),
                        };
                    refreshPlan = true;
                }

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(recipe.CraftCount.ToString());

                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Craft Items##plan-artisan-{recipe.RecipeId}"))
                {
                    this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                        recipe.RecipeId,
                        recipe.CraftCount,
                        out this.integrationMessage);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton($"Teamcraft##plan-teamcraft-{recipe.RecipeId}"))
                {
                    this.integrationError = !this.pluginIntegrationService.OpenInTeamcraft(
                        recipe.ResultItemId,
                        recipe.DesiredAmount,
                        out this.integrationMessage);
                }

                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##plan-remove-{recipe.RecipeId}"))
                    recipeToRemove = recipe.RecipeId;
            }

            ImGui.EndTable();
        }

        if (recipeToRemove is { } removeId)
        {
            this.selectedRecipes.RemoveAll(
                selection => selection.Recipe.RecipeId == removeId);
            refreshPlan = true;
        }

        if (refreshPlan)
            this.RefreshDetails(false);
    }

    private void DrawSummary(RecipePlanDetails details)
    {
        if (!ImGui.BeginTable(
                "recipe-summary",
                3,
                ImGuiTableFlags.SizingStretchSame |
                ImGuiTableFlags.BordersInnerV |
                ImGuiTableFlags.PadOuterX))
            return;

        ImGui.TableNextColumn();
        ImGui.TextDisabled("RECIPES");
        ImGui.TextUnformatted(details.Recipes.Count.ToString());
        ImGui.TableNextColumn();
        ImGui.TextDisabled("TOTAL CRAFTS");
        ImGui.TextUnformatted(details.Recipes
            .Aggregate(0UL, (total, recipe) => total + recipe.CraftCount)
            .ToString());
        ImGui.TableNextColumn();
        ImGui.TextDisabled("RAW ITEMS");
        ImGui.TextUnformatted(details.RawMaterials.Count.ToString());
        ImGui.EndTable();
    }

    private bool DrawCollapsibleSection(string title, string subtitle, string sectionId)
    {
        ImGui.Spacing();
        var isOpen = ImGui.CollapsingHeader(
            $"{title}##{sectionId}",
            ImGuiTreeNodeFlags.DefaultOpen);
        if (!isOpen)
            return false;

        ImGui.TextDisabled(subtitle);
        ImGui.Spacing();
        return true;
    }

    private static bool IsElementalCatalyst(IngredientNeed material) =>
        material.ItemId is >= 2 and <= 19;

    private void DrawMaterialsTable(string tableId, IReadOnlyList<IngredientNeed> materials)
    {
        var displayedMaterials = materials
            .Where(material =>
                material.ItemId != 0 &&
                material.Required > 0 &&
                !string.IsNullOrWhiteSpace(material.Name))
            .ToList();
        if (displayedMaterials.Count == 0)
            return;

        var isDirectIngredients = tableId == "ingredients";
        var showTravel = displayedMaterials.Any(material => material.IsGatherable);
        var showAvailable = displayedMaterials.Any(material =>
            material.ReductionSources is { Count: > 0 } ||
            this.aetherialReductionService.GetGatheringTimerText(material.ItemId) is not null);
        var showNq = displayedMaterials.Any(material => material.OwnedNq > 0);
        var showHq = displayedMaterials.Any(material => material.OwnedHq > 0);
        var showFoundIn = displayedMaterials.Any(material => material.Locations.Count > 0);
        var showRawCraftStatus =
            isDirectIngredients &&
            displayedMaterials.Any(material => material.CanCraftMissingFromRaw.HasValue);
        const bool showMissing = true;
        var columnCount =
            3 +
            (showTravel ? 1 : 0) +
            (showAvailable ? 1 : 0) +
            (showNq ? 1 : 0) +
            (showHq ? 1 : 0) +
            (showFoundIn ? 1 : 0) +
            (showRawCraftStatus ? 1 : 0) +
            (showMissing ? 1 : 0);
        var tableFlags =
            ImGuiTableFlags.BordersOuterH |
            ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.RowBg |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingFixedFit;
        var requestedTableWidth =
            140f +
            110f +
            45f +
            (showTravel ? 70f : 0f) +
            (showAvailable ? 115f : 0f) +
            (showNq ? 45f : 0f) +
            (showHq ? 45f : 0f) +
            (showFoundIn ? 150f : 0f) +
            (showRawCraftStatus ? 110f : 0f) +
            (showMissing ? 75f : 0f) +
            (ImGui.GetStyle().CellPadding.X * 2 * columnCount) +
            2f;
        var availableTableWidth = ImGui.GetContentRegionAvail().X;
        var needsHorizontalScroll = requestedTableWidth > availableTableWidth;
        if (needsHorizontalScroll)
            tableFlags |= ImGuiTableFlags.ScrollX;

        var lastColumnKey = showRawCraftStatus
            ? "raw"
            : showFoundIn
                ? "found"
                : showHq
                    ? "hq"
                    : showNq
                        ? "nq"
                        : showAvailable
                            ? "available"
                            : showTravel
                                ? "travel"
                                : "missing";
        void SetupColumn(string label, string key, float width)
        {
            var stretch = !needsHorizontalScroll && key == lastColumnKey;
            ImGui.TableSetupColumn(
                label,
                stretch
                    ? ImGuiTableColumnFlags.WidthStretch
                    : ImGuiTableColumnFlags.WidthFixed,
                stretch ? 1f : width);
        }

        var tableHeight =
            (25f * (displayedMaterials.Count + 1)) +
            (needsHorizontalScroll ? ImGui.GetStyle().ScrollbarSize : 0f) +
            2f;
        if (ImGui.BeginTable(
                tableId,
                columnCount,
                tableFlags,
                new Vector2(Math.Max(1f, availableTableWidth), tableHeight)))
        {
            SetupColumn("Ingredient", "ingredient", 140);
            SetupColumn("Source", "source", 110);
            SetupColumn("Need", "need", 45);
            if (showMissing)
                SetupColumn("Missing", "missing", 75);
            if (showTravel)
                SetupColumn("Travel", "travel", 70);
            if (showAvailable)
                SetupColumn("Available", "available", 115);
            if (showNq)
                SetupColumn("NQ", "nq", 45);
            if (showHq)
                SetupColumn("HQ", "hq", 45);
            if (showFoundIn)
                SetupColumn("Found in", "found", 150);
            if (showRawCraftStatus)
                SetupColumn("From raw", "raw", 110);
            ImGui.TableHeadersRow();

            foreach (var ingredient in displayedMaterials)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 25);
                if (ingredient.HasEnough)
                    ImGui.TableSetBgColor(
                        ImGuiTableBgTarget.RowBg0,
                        ImGui.GetColorU32(this.configuration.EnoughRowColor));

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ingredient.Name);
                MaterialUsageTooltip.Draw(
                    this.recipeService,
                    this.configuration,
                    ingredient);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ingredient.Source);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(ingredient.Required.ToString());

                if (showMissing)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.HasEnough)
                        ImGui.TextUnformatted(string.Empty);
                    else if (showRawCraftStatus && ingredient.CanCraftMissingFromRaw is true)
                        ImGui.TextUnformatted(string.Empty);
                    else
                        ImGui.TextColored(this.configuration.MissingTextColor, ingredient.Missing.ToString());
                }

                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(ingredient.ReductionSources);
                if (showTravel)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.IsGatherable &&
                        ImGui.SmallButton($"Gather##{tableId}-{ingredient.ItemId}"))
                    {
                        this.integrationError = !this.pluginIntegrationService.GatherWithGatherBuddy(
                            reductionSource?.Name ?? ingredient.Name,
                            reductionSource?.IsFishing ?? ingredient.IsFishing,
                            out this.integrationMessage);

                        if (this.integrationError)
                            this.OpenTravelPopup(ingredient, reductionSource);
                        else
                            this.IsOpen = false;
                    }
                    else if (!ingredient.IsGatherable)
                    {
                        ImGui.TextDisabled("-");
                    }
                }

                if (showAvailable)
                {
                    ImGui.TableNextColumn();
                    if (reductionSource is not null)
                        ImGui.TextUnformatted(this.aetherialReductionService.GetTimerText(reductionSource));
                    else if (this.aetherialReductionService.GetGatheringTimerText(ingredient.ItemId) is { } gatheringTimer)
                        ImGui.TextUnformatted(gatheringTimer);
                    else
                        ImGui.TextDisabled("-");
                }

                if (showNq)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(ingredient.OwnedNq.ToString());
                }

                if (showHq)
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(ingredient.OwnedHq.ToString());
                }

                if (showFoundIn)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.Locations.Count > 0)
                        ImGui.TextUnformatted(string.Join(", ", ingredient.Locations));
                    else
                        ImGui.TextDisabled("-");
                }

                if (showRawCraftStatus)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.HasEnough)
                        ImGui.TextDisabled("Owned");
                    else if (ingredient.CanCraftMissingFromRaw is true &&
                             ingredient.RawCraftRecipeId is { } rawCraftRecipeId)
                    {
                        ImGui.PushStyleColor(ImGuiCol.Button, this.configuration.ReadyButtonColor);
                        var readyToCraftClicked =
                            ImGui.SmallButton($"Ready to craft##{tableId}-raw-{ingredient.ItemId}");
                        ImGui.PopStyleColor();
                        if (readyToCraftClicked)
                        {
                            this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                                rawCraftRecipeId,
                                ingredient.RawCraftCount,
                                out this.integrationMessage);
                        }
                    }
                    else if (ingredient.CanCraftMissingFromRaw is false)
                        ImGui.TextColored(this.configuration.WarningTextColor, "Short");
                    else
                        ImGui.TextDisabled("-");
                }

            }

            ImGui.EndTable();
        }
    }

    private void OpenTravelPopup(
        IngredientNeed ingredient,
        AetherialReductionSource? reductionSource = null)
    {
        this.gatheringItemName = reductionSource?.Name ?? ingredient.Name;
        this.gatheringDestinations = this.travelService.GetDestinations(
            reductionSource?.ItemId ?? ingredient.ItemId);
        this.travelMessage = string.Empty;
        this.isTravelPopupOpen = true;
        this.travelPopupRequested = true;
    }

    private void DrawTravelPopup()
    {
        if (this.travelPopupRequested)
        {
            ImGui.OpenPopup("Gathering locations");
            this.travelPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Gathering locations",
                ref this.isTravelPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextUnformatted(this.gatheringItemName);
        ImGui.Separator();

        if (this.gatheringDestinations.Count == 0)
        {
            ImGui.TextColored(
                this.configuration.WarningTextColor,
                "No gathering location was found in the game data.");
        }

        for (var i = 0; i < this.gatheringDestinations.Count; i++)
        {
            var destination = this.gatheringDestinations[i];
            ImGui.PushID(i);
            ImGui.TextUnformatted($"{destination.ZoneName} - {destination.LocationName}");

            if (destination.AetheryteId is not null)
            {
                ImGui.TextDisabled($"{destination.AetheryteName} ({destination.TeleportCost:N0} gil)");
                if (destination.ItemId <= ushort.MaxValue)
                {
                    if (ImGui.Button("Show map"))
                    {
                        if (this.travelService.ShowOnMap(destination))
                        {
                            this.isTravelPopupOpen = false;
                            this.IsOpen = false;
                            ImGui.CloseCurrentPopup();
                        }
                        else
                        {
                            this.travelMessage = "The map could not be opened for this location.";
                        }
                    }

                    ImGui.SameLine();
                }

                if (ImGui.Button("Teleport"))
                {
                    if (this.travelService.Teleport(destination))
                    {
                        this.isTravelPopupOpen = false;
                        this.IsOpen = false;
                        ImGui.CloseCurrentPopup();
                    }
                    else
                    {
                        this.travelMessage = "Teleport could not be started right now.";
                    }
                }
            }
            else
            {
                ImGui.TextColored(
                    this.configuration.WarningTextColor,
                    "No unlocked aetheryte is available in this zone.");
            }

            ImGui.PopID();
            if (i + 1 < this.gatheringDestinations.Count)
                ImGui.Separator();
        }

        if (!string.IsNullOrWhiteSpace(this.travelMessage))
        {
            ImGui.Spacing();
            ImGui.TextColored(this.configuration.MissingTextColor, this.travelMessage);
        }

        ImGui.Spacing();
        if (ImGui.Button("Close"))
        {
            this.isTravelPopupOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void OnInventoryChanged() => this.inventoryRefreshRequested = true;
}
