using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace DalamudRecipeHelper;

public sealed class RecipeWindow : Window, IDisposable
{
    private static readonly Vector2 BaseWindowSize = new(760, 540);
    private static readonly Vector2 BaseMinimumWindowSize = new(460, 320);

    private readonly FileLogService fileLog;
    private readonly MarketboardPriceService marketboardPriceService;
    private readonly RecipeService recipeService;
    private readonly InventoryService inventoryService;
    private readonly TravelService travelService;
    private readonly PluginIntegrationService pluginIntegrationService;
    private readonly GwenDreamService gwenDreamService;
    private readonly AetherialReductionService aetherialReductionService;
    private readonly Configuration configuration;
    private readonly Action openSettings;
    private readonly Action saveConfiguration;
    private readonly RawMaterialsOverlayWindow rawMaterialsOverlayWindow;
    private readonly string versionText;
    private IReadOnlyList<RecipeMatch> searchResults = [];
    private IReadOnlyDictionary<uint, CraftableRecipeAvailability> craftableAvailability =
        new Dictionary<uint, CraftableRecipeAvailability>();
    private bool showingCraftableRecipes;
    private readonly List<RecipePlanSelection> selectedRecipes = [];
    private readonly HashSet<SavedRecipePlan> selectedSavedPlans = [];
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
    private uint observedCraftAllCompletionCount;
    private uint observedDreamCompletionSequence;
    private bool dreamCraftPending;
    private bool inventoryRefreshRequested;
    private string planName = string.Empty;
    private string planFolderName = string.Empty;
    private int planInputNonce;
    private string planMessage = string.Empty;
    private bool planMessageIsError;
    private DateTime planMessageExpiresAt;
    private bool wasResizingSearchPane;
    private SavedRecipePlan? renamingPlan;
    private string renamePlanName = string.Empty;
    private string renamePlanError = string.Empty;
    private bool renamePlanPopupRequested;
    private bool isRenamePlanPopupOpen;
    private SavedRecipePlan? movingPlan;
    private IReadOnlyList<SavedRecipePlan> movingSelectedPlans = [];
    private string movePlanFolderName = string.Empty;
    private bool movePlanPopupRequested;
    private bool isMovePlanPopupOpen;
    private bool moveSelectedPlansPopupRequested;
    private bool isMoveSelectedPlansPopupOpen;
    private string renamingFolderSource = string.Empty;
    private string renameFolderName = string.Empty;
    private string renameFolderError = string.Empty;
    private bool renameFolderPopupRequested;
    private bool isRenameFolderPopupOpen;
    private int appliedMainWindowScalePercent;

    public RecipeWindow(
        FileLogService fileLog,
        MarketboardPriceService marketboardPriceService,
        RecipeService recipeService,
        InventoryService inventoryService,
        TravelService travelService,
        PluginIntegrationService pluginIntegrationService,
        GwenDreamService gwenDreamService,
        AetherialReductionService aetherialReductionService,
        Configuration configuration,
        Action openSettings,
        Action saveConfiguration,
        RawMaterialsOverlayWindow rawMaterialsOverlayWindow)
        : base($"Recipe Helper v{typeof(RecipeWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0"}###DalamudRecipeHelper")
    {
        this.fileLog = fileLog;
        this.marketboardPriceService = marketboardPriceService;
        this.recipeService = recipeService;
        this.inventoryService = inventoryService;
        this.travelService = travelService;
        this.pluginIntegrationService = pluginIntegrationService;
        this.gwenDreamService = gwenDreamService;
        this.observedCraftAllCompletionCount =
            pluginIntegrationService.CraftAllCompletionCount;
        this.observedDreamCompletionSequence =
            gwenDreamService.CompletionSequence;
        this.aetherialReductionService = aetherialReductionService;
        this.configuration = configuration;
        this.versionText = typeof(RecipeWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        this.openSettings = openSettings;
        this.saveConfiguration = saveConfiguration;
        this.rawMaterialsOverlayWindow = rawMaterialsOverlayWindow;
        this.inventoryService.InventoryChanged += this.OnInventoryChanged;
        this.appliedMainWindowScalePercent = WindowTheme.GetMainWindowScalePercent(this.configuration);
        this.Size = this.ScaleMainWindowSize(BaseWindowSize, this.appliedMainWindowScalePercent);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.ApplyMainWindowConstraints();
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
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);
        this.ApplyMainWindowScaleIfNeeded();

        if (this.observedCraftAllCompletionCount !=
            this.pluginIntegrationService.CraftAllCompletionCount)
        {
            this.observedCraftAllCompletionCount =
                this.pluginIntegrationService.CraftAllCompletionCount;
            this.integrationMessage = string.Empty;
            this.integrationError = false;
        }

        if (this.observedDreamCompletionSequence !=
            this.gwenDreamService.CompletionSequence)
        {
            this.observedDreamCompletionSequence =
                this.gwenDreamService.CompletionSequence;
            if (this.dreamCraftPending)
            {
                this.dreamCraftPending = false;
                if (this.gwenDreamService.LastRunSucceeded)
                    this.TryStartDreamCraftAll();
            }
        }

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
            var splitterWidth = this.ScaleUi(5f);
            var paneSpacing = ImGui.GetStyle().ItemSpacing.X;
            var maximumSearchWidth = Math.Max(
                this.ScaleUi(150f),
                paneArea.X - splitterWidth - (paneSpacing * 2) - this.ScaleUi(260f));
            var leftWidth = Math.Clamp(
                this.configuration.SearchPaneWidth,
                this.ScaleUi(150f),
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
                    if (ImGui.Button("Clear search", new Vector2(this.ScaleUi(96f), 0)))
                        this.ClearSearch();
                    DrawTooltipIfHovered("Clear the current search results and search text.");
                    if (!string.IsNullOrWhiteSpace(this.resultFilter))
                    {
                        ImGui.SameLine();
                        if (ImGui.Button("Clear filter", new Vector2(this.ScaleUi(92f), 0)))
                            this.resultFilter = string.Empty;
                        DrawTooltipIfHovered("Clear the result filter only.");
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
                    var subtitle = this.showingCraftableRecipes &&
                                   this.craftableAvailability.TryGetValue(
                                       result.RecipeId,
                                       out var availability)
                        ? $"{availability.CraftCount:N0} crafts | {availability.OutputAmount:N0} items"
                        : isSelected
                            ? "Added to plan"
                            : $"Click to add | Yield {result.ResultAmount}";

                    var rowWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
                    var rowHeight = this.ScaleUi(48f);
                    var checkboxSize = this.ScaleUi(16f);
                    var textWidth = rowWidth - this.ScaleUi(14f) - checkboxSize - this.ScaleUi(10f) - this.ScaleUi(14f);
                    var title = WrapTextToWidth(result.ResultName, textWidth);
                    var subtitleText = TrimDisplayTextToWidth(subtitle, textWidth);
                    var titleSize = ImGui.CalcTextSize(title);
                    var subtitleSize = ImGui.CalcTextSize(subtitleText);
                    var renderRowHeight = Math.Max(rowHeight, titleSize.Y + subtitleSize.Y + this.ScaleUi(14f));
                    var rowPos = ImGui.GetCursorScreenPos();
                    var rowColor = isSelected
                        ? WithAlpha(this.configuration.AccentColor, 0.18f)
                        : AdjustColor(this.configuration.WindowBackgroundColor, 0.08f);
                    var drawList = ImGui.GetWindowDrawList();
                    drawList.AddRectFilled(
                        rowPos,
                        rowPos + new Vector2(rowWidth, renderRowHeight),
                        ImGui.GetColorU32(rowColor),
                        this.ScaleUi(14f));

                    if (ImGui.InvisibleButton("##recipe-result-row", new Vector2(rowWidth, renderRowHeight)))
                        this.SetRecipeSelected(result, !isSelected);

                    var checkboxPos = new Vector2(rowPos.X + this.ScaleUi(14f), rowPos.Y + ((renderRowHeight - checkboxSize) / 2f));
                    var checkboxColor = isSelected
                        ? this.configuration.AccentColor
                        : WithAlpha(this.configuration.TextColor, 0.18f);
                    drawList.AddRectFilled(
                        checkboxPos,
                        checkboxPos + new Vector2(checkboxSize, checkboxSize),
                        ImGui.GetColorU32(checkboxColor),
                        this.ScaleUi(5f));
                    drawList.AddRect(
                        checkboxPos,
                        checkboxPos + new Vector2(checkboxSize, checkboxSize),
                        ImGui.GetColorU32(WithAlpha(this.configuration.TextColor, 0.24f)),
                        this.ScaleUi(5f));
                    if (isSelected)
                    {
                        drawList.AddText(
                            checkboxPos + this.ScaleUi(new Vector2(3f, -1f)),
                            ImGui.GetColorU32(this.configuration.WindowBackgroundColor),
                            "✓");
                    }

                    var textLeft = checkboxPos.X + checkboxSize + this.ScaleUi(10f);
                    var textRight = rowPos.X + rowWidth - this.ScaleUi(14f);
                    drawList.PushClipRect(rowPos, rowPos + new Vector2(rowWidth, renderRowHeight), true);
                    DrawStackedTextBlock(
                        drawList,
                        new Vector2(textLeft, rowPos.Y),
                        new Vector2(textRight - textLeft, renderRowHeight),
                        title,
                        subtitleText,
                        ImGui.GetColorU32(ImGuiCol.Text),
                        ImGui.GetColorU32(ImGuiCol.TextDisabled),
                        this.ScaleUi(2f),
                        this.ScaleUi(6f),
                        false);
                    drawList.PopClipRect();

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
                    this.ScaleUi(150f),
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
            ImGui.PopStyleColor(10);
            ImGui.PopStyleVar(5);
        }
    }

    private void DrawHeader()
    {
        ImGui.TextColored(this.configuration.AccentColor, "RECIPE HELPER");
        ImGui.SameLine();
        ImGui.TextDisabled($"Plan | Check | Craft | v{this.versionText}");
        var inventoryStatus =
            $"{this.inventoryService.LastItemStacks} stacks  |  " +
            $"{this.inventoryService.LastScannedContainers} live containers  |  " +
            $"{this.inventoryService.LastStoredRetainers} retainers saved";
        var statusWidth = ImGui.CalcTextSize(inventoryStatus).X;
        var targetX = ImGui.GetWindowContentRegionMax().X - statusWidth;
        ImGui.SameLine();
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), targetX));
        ImGui.TextDisabled(inventoryStatus);
        ImGui.Spacing();

        var buttonPadding = this.ScaleUi(20f);
        var searchButtonWidth = ImGui.CalcTextSize("Search").X + buttonPadding;
        var craftableButtonWidth = ImGui.CalcTextSize("Can craft").X + buttonPadding;
        var showDreamButton = this.gwenDreamService.IsAutoRetainerAvailable;
        var overlayButtonWidth = ImGui.CalcTextSize("Missing Items Overlay").X + buttonPadding;
        var refreshButtonWidth = ImGui.CalcTextSize("Refresh Inventory").X + buttonPadding;
        var settingsButtonWidth = ImGui.CalcTextSize("Settings").X + buttonPadding;
        var searchWidth = MathF.Max(
            this.ScaleUi(150f),
            ImGui.GetContentRegionAvail().X -
            searchButtonWidth -
            craftableButtonWidth -
            overlayButtonWidth -
            refreshButtonWidth -
            settingsButtonWidth -
            this.ScaleUi(36f));

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
        DrawTooltipIfHovered("Search recipes by crafted item name or item ID.");

        ImGui.SameLine();
        if (ImGui.Button("Can craft", new Vector2(craftableButtonWidth, 0)))
            this.RefreshCraftableRecipes(true);
        DrawTooltipIfHovered("Show recipes you can make from your current stored materials.");

        ImGui.SameLine();
        if (ImGui.Button("Missing Items Overlay", new Vector2(overlayButtonWidth, 0)))
            this.rawMaterialsOverlayWindow.IsOpen = true;
        DrawTooltipIfHovered("Open the compact overlay for missing gatherable materials.");

        ImGui.SameLine();
        if (ImGui.Button("Refresh Inventory", new Vector2(refreshButtonWidth, 0)))
            this.RefreshDetails(true);
        DrawTooltipIfHovered("Rescan inventory, saddlebags, and saved retainer stock.");

        ImGui.SameLine();
        if (ImGui.Button("Settings", new Vector2(settingsButtonWidth, 0)))
            this.openSettings();
        DrawTooltipIfHovered("Open Recipe Helper settings.");

        if (showDreamButton &&
            !string.IsNullOrWhiteSpace(this.gwenDreamService.StatusMessage))
        {
            ImGui.TextColored(
                this.gwenDreamService.StatusIsError
                    ? this.configuration.MissingTextColor
                    : this.configuration.SuccessTextColor,
                this.gwenDreamService.StatusMessage);
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
    }

    private void PushModernStyle()
    {
        var accent = this.configuration.AccentColor;
        var buttonColor = this.configuration.ButtonColor;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, this.ScaleUi(5f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, this.ScaleUi(6f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, this.ScaleUi(new Vector2(7f, 4f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, this.ScaleUi(new Vector2(6f, 5f)));
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, this.ScaleUi(new Vector2(6f, 4f)));

        ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(buttonColor, 0.72f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AdjustColor(buttonColor, 0.10f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AdjustColor(buttonColor, -0.08f));
        ImGui.PushStyleColor(ImGuiCol.Header, WithAlpha(accent, 0.30f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, WithAlpha(accent, 0.48f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, WithAlpha(accent, 0.62f));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, WithAlpha(accent, 0.22f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, this.configuration.InputCardColor);
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, AdjustColor(this.configuration.InputCardColor, 0.06f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, AdjustColor(this.configuration.InputCardColor, 0.10f));
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
            ImGui.Dummy(new Vector2(0, this.ScaleUi(24f)));
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
                    "Select, combine, craft, or manage named recipe plans",
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
                "Select, combine, craft, or manage named recipe plans",
                "saved-plans-section"))
            this.DrawSavedPlans();

        if (this.DrawCollapsibleSection(
                "SELECTED RECIPES",
                "Combined totals, quantities, and recipe actions",
                "selected-recipes-section",
                this.selectedRecipes.Count > 0 ? "Clear selected" : null,
                this.selectedRecipes.Count > 0
                    ? () =>
                    {
                        this.selectedRecipes.Clear();
                        this.planName = string.Empty;
                        this.planFolderName = string.Empty;
                        this.planInputNonce++;
                        this.RefreshDetails(false);
                    }
                    : null))
        {
            this.DrawSummary(details);
            ImGui.Spacing();
            this.DrawSelectedRecipes(details);
        }

        var canCraftAll =
            this.artisanCraftQueue.Count > 0 &&
            (details.Recipes.Count > 1 ||
             this.artisanCraftQueue.Any(recipe => recipe.IsIntermediate));
        var hasDreamFeature = this.gwenDreamService.IsAutoRetainerAvailable;
        var canUseDream = hasDreamFeature && this.gwenDreamService.CanUseForSelection(this.recipePlanDetails);
        if (details.Recipes.Count > 0 || !string.IsNullOrWhiteSpace(this.integrationMessage))
        {
            ImGui.Spacing();
            ImGui.BeginDisabled(!canCraftAll);
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                this.configuration.ReadyButtonColor);
            var craftAllClicked = ImGui.Button("Craft all with Artisan", new Vector2(this.ScaleUi(160f), 0));
            ImGui.PopStyleColor();
            ImGui.EndDisabled();
            if (craftAllClicked)
            {
                this.integrationError =
                    !this.pluginIntegrationService.CraftAllWithArtisan(
                        this.artisanCraftQueue,
                        0,
                        out this.integrationMessage);
            }
            DrawTooltipIfHovered("Queue every selected recipe in Artisan, including required pre-crafts.");

            if (hasDreamFeature)
                ImGui.SameLine();

            if (hasDreamFeature)
            {
                ImGui.BeginDisabled(!canUseDream);
                if (ImGui.Button("Gwen's Dream", new Vector2(this.ScaleUi(140f), 0)))
                {
                    if (this.gwenDreamService.TryStart(this.recipePlanDetails))
                        this.dreamCraftPending = true;
                }
                ImGui.EndDisabled();
                DrawTooltipIfHovered("Will automatically withdraw all materials from retainers and craft all pre-crafts and recipes - ultimate laziness!");

                if (!string.IsNullOrWhiteSpace(this.integrationMessage))
                    ImGui.SameLine();
            }
            else if (!string.IsNullOrWhiteSpace(this.integrationMessage))
            {
                ImGui.SameLine();
            }

            if (!string.IsNullOrWhiteSpace(this.integrationMessage))
            {
                ImGui.TextColored(
                    this.integrationError
                        ? this.configuration.MissingTextColor
                        : this.configuration.SuccessTextColor,
                    this.integrationMessage);
            }
        }

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
        var allRawMaterials = details.RawMaterials
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        var allElementalCatalysts = details.RawMaterials
            .Where(IsElementalCatalyst)
            .ToList();
        var obtainedRawCount =
            allRawMaterials.Count(material => material.HasEnough) +
            allElementalCatalysts.Count(material => material.HasEnough);
        var rawMaterials = allRawMaterials
            .Where(material =>
                this.configuration.ShowObtainedRawMaterials ||
                !material.HasEnough)
            .ToList();
        var elementalCatalysts = allElementalCatalysts
            .Where(material =>
                this.configuration.ShowObtainedRawMaterials ||
                !material.HasEnough)
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
                "raw-materials-section",
                obtainedRawCount > 0
                    ? this.configuration.ShowObtainedRawMaterials
                        ? $"Hide obtained ({allRawMaterials.Count(material => material.HasEnough)})"
                        : $"Show obtained ({allRawMaterials.Count(material => material.HasEnough)})"
                    : null,
                obtainedRawCount > 0
                    ? ToggleObtainedRawMaterials
                    : null))
            this.DrawMaterialsTable("raw-materials", rawMaterials);

        if (elementalCatalysts.Count > 0 &&
            this.DrawCollapsibleSection(
                "SHARDS, CRYSTALS & CLUSTERS",
                "Combined elemental catalyst requirements",
                "elemental-catalysts-section",
                obtainedRawCount > 0
                    ? this.configuration.ShowObtainedRawMaterials
                        ? $"Hide obtained ({allElementalCatalysts.Count(material => material.HasEnough)})"
                        : $"Show obtained ({allElementalCatalysts.Count(material => material.HasEnough)})"
                    : null,
                obtainedRawCount > 0
                    ? ToggleObtainedRawMaterials
                    : null))
            this.DrawMaterialsTable("elemental-catalysts", elementalCatalysts);

        this.DrawTravelPopup();
    }

    private void DrawSavePlanControls()
    {
        ImGui.SetNextItemWidth(this.ScaleUi(260f));
        var submitted = ImGui.InputTextWithHint(
            $"##recipe-plan-name-{this.planInputNonce}",
            "Plan name",
            ref this.planName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);
        DrawTooltipIfHovered("Enter a name to save or update this recipe plan.");
        ImGui.SameLine();
        if (ImGui.Button("Save plan") || submitted)
            this.SaveCurrentPlan();
        DrawTooltipIfHovered("Save the current selected recipes as a named plan.");

        ImGui.SetNextItemWidth(this.ScaleUi(220f));
        ImGui.InputTextWithHint(
            $"##recipe-plan-folder-{this.planInputNonce}",
            "Folder (optional)",
            ref this.planFolderName,
            80);
        DrawTooltipIfHovered("Optional folder name for this saved plan.");

        this.DrawPlanMessage();
    }

    private void TryStartDreamCraftAll()
    {
        if (this.artisanCraftQueue.Count == 0)
        {
            this.integrationError = true;
            this.integrationMessage = string.IsNullOrWhiteSpace(this.artisanCraftQueueError)
                ? "Gwen's Dream finished, but there are no recipes ready to send to Artisan."
                : this.artisanCraftQueueError;
            return;
        }

        this.integrationError =
            !this.pluginIntegrationService.CraftAllWithArtisan(
                this.artisanCraftQueue,
                this.recipePlanDetails?.Recipes.Count ?? 0,
                out this.integrationMessage);
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
        SavedRecipePlan? planToMove = null;
        string? folderToRename = null;
        var loadSelectedPlans = false;
        var craftSelectedPlans = false;
        var moveSelectedPlans = false;
        this.selectedSavedPlans.RemoveWhere(plan =>
            !this.configuration.SavedRecipePlans.Contains(plan));
        var displayedPlans = this.configuration.SavedRecipePlans
            .OrderBy(plan => NormalizeFolderName(plan.FolderName))
            .ThenBy(plan => plan.Name)
            .ToList();
        foreach (var folderGroup in displayedPlans.GroupBy(plan => NormalizeFolderName(plan.FolderName)))
        {
            var folderName = folderGroup.Key;
            ImGui.PushID($"saved-folder-{folderName}");
            var folderHeaderColor = this.configuration.UseAccentForFolderHeaders
                ? this.configuration.AccentColor
                : this.configuration.FolderHeaderColor;
            ImGui.PushStyleColor(ImGuiCol.Header, folderHeaderColor);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, AdjustColor(folderHeaderColor, 0.06f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, AdjustColor(folderHeaderColor, -0.04f));
            ImGui.PushStyleColor(
                ImGuiCol.Text,
                this.configuration.UseAccentForFolderHeaders
                    ? this.configuration.TextColor
                    : this.configuration.FolderHeaderTextColor);
            var isFolderOpen = ImGui.CollapsingHeader(
                $"{GetFolderDisplayName(folderName)} ({folderGroup.Count()})##folder-group",
                ImGuiTreeNodeFlags.DefaultOpen);
            ImGui.PopStyleColor(4);
            if (!string.IsNullOrWhiteSpace(folderName))
            {
                var buttonWidth = ImGui.CalcTextSize("Rename folder").X +
                    (ImGui.GetStyle().FramePadding.X * 2f);
                var targetX = ImGui.GetWindowContentRegionMax().X - buttonWidth;
                ImGui.SameLine();
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), targetX));
                if (ImGui.SmallButton("Rename folder"))
                    folderToRename = folderName;
                DrawTooltipIfHovered("Rename this folder and keep all plans inside it.");
            }
            if (isFolderOpen)
            {
                foreach (var savedPlan in folderGroup)
                {
                    ImGui.PushID($"saved-plan-{savedPlan.Name}");
                    var rowWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
                    var rowHeight = this.ScaleUi(52f);
                    var rowPos = ImGui.GetCursorScreenPos();
                    var rowColor = this.selectedSavedPlans.Contains(savedPlan)
                        ? WithAlpha(this.configuration.AccentColor, 0.18f)
                        : AdjustColor(this.configuration.WindowBackgroundColor, 0.08f);
                    ImGui.GetWindowDrawList().AddRectFilled(
                        rowPos,
                        rowPos + new Vector2(rowWidth, rowHeight),
                        ImGui.GetColorU32(rowColor),
                        this.ScaleUi(14f));
                    ImGui.Dummy(new Vector2(rowWidth, rowHeight));

                    var cursor = ImGui.GetCursorPos();
                    ImGui.SetCursorScreenPos(rowPos + this.ScaleUi(new Vector2(14f, 14f)));
                    var isSelected = this.selectedSavedPlans.Contains(savedPlan);
                    if (ImGui.Checkbox("##select-saved-plan", ref isSelected))
                    {
                        if (isSelected)
                            this.selectedSavedPlans.Add(savedPlan);
                        else
                            this.selectedSavedPlans.Remove(savedPlan);
                    }

                    ImGui.SetCursorScreenPos(rowPos + this.ScaleUi(new Vector2(44f, 8f)));
                    ImGui.TextUnformatted(savedPlan.Name);
                    ImGui.SetCursorScreenPos(rowPos + this.ScaleUi(new Vector2(44f, 25f)));
                    ImGui.TextDisabled($"{savedPlan.Recipes.Count} recipes");

                    var actionX = rowPos.X + rowWidth - this.ScaleUi(314f);
                    ImGui.SetCursorScreenPos(new Vector2(actionX, rowPos.Y + this.ScaleUi(12f)));
                    if (ImGui.Button("Load", new Vector2(this.ScaleUi(48f), 0)))
                        planToLoad = savedPlan;
                    DrawTooltipIfHovered("Load this saved plan into the current recipe list.");
                    ImGui.SameLine();
                    if (ImGui.Button("Move", new Vector2(this.ScaleUi(48f), 0)))
                        planToMove = savedPlan;
                    DrawTooltipIfHovered("Move this saved plan to another folder.");
                    ImGui.SameLine();
                    if (ImGui.Button("Duplicate", new Vector2(this.ScaleUi(72f), 0)))
                        planToDuplicate = savedPlan;
                    DrawTooltipIfHovered("Create a copy of this saved plan.");
                    ImGui.SameLine();
                    if (ImGui.Button("Rename", new Vector2(this.ScaleUi(62f), 0)))
                        planToRename = savedPlan;
                    DrawTooltipIfHovered("Rename this saved plan.");
                    ImGui.SameLine();
                    if (ImGui.Button("Delete", new Vector2(this.ScaleUi(54f), 0)))
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

                    ImGui.SetCursorPos(cursor);
                    ImGui.Spacing();
                    ImGui.PopID();
                }
            }
            ImGui.PopID();
        }

        if (this.selectedSavedPlans.Count > 0)
        {
            if (ImGui.Button($"Load selected plans ({this.selectedSavedPlans.Count})"))
                loadSelectedPlans = true;
            DrawTooltipIfHovered("Load all checked plans into one combined recipe list.");

            ImGui.SameLine();
            ImGui.PushStyleColor(
                ImGuiCol.Button,
                this.configuration.ReadyButtonColor);
            craftSelectedPlans =
                ImGui.Button($"Craft selected plans ({this.selectedSavedPlans.Count})");
            ImGui.PopStyleColor();
            DrawTooltipIfHovered("Queue all checked plans in Artisan, including required pre-crafts.");

            ImGui.SameLine();
            if (ImGui.Button($"Move selected ({this.selectedSavedPlans.Count})"))
                moveSelectedPlans = true;
            DrawTooltipIfHovered("Move all checked plans into a folder.");

            ImGui.SameLine();
            if (ImGui.Button($"Export selected ({this.selectedSavedPlans.Count})"))
                this.ExportSavedPlansToClipboard(this.selectedSavedPlans);
            DrawTooltipIfHovered("Copy the checked saved plans to the clipboard, including folder names.");
        }
        else if (this.configuration.SavedRecipePlans.Count > 0)
        {
            if (ImGui.Button("Export all saved plans"))
                this.ExportSavedPlansToClipboard(this.configuration.SavedRecipePlans);
            DrawTooltipIfHovered("Copy every saved plan to the clipboard.");
        }

        if (this.configuration.SavedRecipePlans.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Import plans from clipboard"))
                this.ImportSavedPlansFromClipboard();
            DrawTooltipIfHovered("Import saved plans from copied Recipe Helper plan data.");
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

        if (planToMove is not null)
        {
            this.movingPlan = planToMove;
            this.movePlanFolderName = planToMove.FolderName;
            this.isMovePlanPopupOpen = true;
            this.movePlanPopupRequested = true;
        }

        if (moveSelectedPlans)
        {
            this.movingSelectedPlans = this.selectedSavedPlans
                .Where(plan => this.configuration.SavedRecipePlans.Contains(plan))
                .ToList();
            this.movePlanFolderName = string.Empty;
            this.isMoveSelectedPlansPopupOpen = true;
            this.moveSelectedPlansPopupRequested = true;
        }

        if (folderToRename is not null)
        {
            this.renamingFolderSource = folderToRename;
            this.renameFolderName = folderToRename;
            this.renameFolderError = string.Empty;
            this.isRenameFolderPopupOpen = true;
            this.renameFolderPopupRequested = true;
        }

        if (planToDelete is not null)
        {
            this.selectedSavedPlans.Remove(planToDelete);
            this.configuration.SavedRecipePlans.Remove(planToDelete);
            this.saveConfiguration();
            this.ShowPlanMessage($"Deleted plan '{planToDelete.Name}'.", false);
        }

        if (loadSelectedPlans)
            this.LoadSavedPlans(this.selectedSavedPlans, false);
        else if (craftSelectedPlans)
            this.LoadSavedPlans(this.selectedSavedPlans, true);

        this.DrawRenamePlanPopup();
        this.DrawMovePlanPopup();
        this.DrawMoveSelectedPlansPopup();
        this.DrawRenameFolderPopup();
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
            FolderName = source.FolderName,
            Recipes = source.Recipes.Select(CloneSavedRecipe).ToList(),
        });
        this.saveConfiguration();
        this.ShowPlanMessage($"Duplicated plan as '{newName}'.", false);
    }

    private void DrawMovePlanPopup()
    {
        if (this.movePlanPopupRequested)
        {
            ImGui.OpenPopup("Move saved plan");
            this.movePlanPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Move saved plan",
                ref this.isMovePlanPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);

        this.DrawFolderSelectionControls();
        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "Folder",
            ref this.movePlanFolderName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextDisabled("Leave blank to keep the plan unfiled.");

        if (ImGui.Button("Move") || submitted)
            this.TryMoveSavedPlan();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            this.isMovePlanPopupOpen = false;
            this.movingPlan = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawMoveSelectedPlansPopup()
    {
        if (this.moveSelectedPlansPopupRequested)
        {
            ImGui.OpenPopup("Move selected plans");
            this.moveSelectedPlansPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Move selected plans",
                ref this.isMoveSelectedPlansPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);

        this.DrawFolderSelectionControls();
        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "Folder",
            ref this.movePlanFolderName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextDisabled($"Move {this.movingSelectedPlans.Count} selected plan{(this.movingSelectedPlans.Count == 1 ? string.Empty : "s")} into this folder.");

        if (ImGui.Button("Move all") || submitted)
            this.TryMoveSelectedPlans();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            this.isMoveSelectedPlansPopupOpen = false;
            this.movingSelectedPlans = [];
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawFolderSelectionControls()
    {
        var folders = GetSavedPlanFolders(this.configuration.SavedRecipePlans);
        if (folders.Count == 0)
            return;

        var selectedFolder = NormalizeFolderName(this.movePlanFolderName);
        var selectedIndex = folders.FindIndex(folder =>
            string.Equals(folder, selectedFolder, StringComparison.OrdinalIgnoreCase));
        var preview = selectedIndex >= 0 ? GetFolderDisplayName(folders[selectedIndex]) : "Choose existing folder";

        ImGui.SetNextItemWidth(280);
        if (!ImGui.BeginCombo("Existing folders", preview))
            return;

        var useUnfiled = string.IsNullOrWhiteSpace(selectedFolder);
        if (ImGui.Selectable("Unfiled", useUnfiled))
            this.movePlanFolderName = string.Empty;

        foreach (var folder in folders)
        {
            var isSelected = string.Equals(folder, selectedFolder, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(folder, isSelected))
                this.movePlanFolderName = folder;
        }

        ImGui.EndCombo();
        ImGui.TextDisabled("Choose an existing folder or type a new one below.");
    }

    private void TryMoveSavedPlan()
    {
        if (this.movingPlan is null)
            return;

        this.selectedSavedPlans.Remove(this.movingPlan);
        this.movingPlan.FolderName = this.movePlanFolderName.Trim();
        this.saveConfiguration();
        this.ShowPlanMessage($"Moved plan '{this.movingPlan.Name}'.", false);
        this.isMovePlanPopupOpen = false;
        this.movingPlan = null;
        ImGui.CloseCurrentPopup();
    }

    private void TryMoveSelectedPlans()
    {
        var folderName = this.movePlanFolderName.Trim();
        var plans = this.movingSelectedPlans
            .Where(plan => this.configuration.SavedRecipePlans.Contains(plan))
            .ToList();
        if (plans.Count == 0)
        {
            this.ShowPlanMessage("Select at least one saved plan.", true);
            return;
        }

        foreach (var plan in plans)
        {
            plan.FolderName = folderName;
            this.selectedSavedPlans.Remove(plan);
        }

        this.saveConfiguration();
        this.ShowPlanMessage($"Moved {plans.Count} saved plan{(plans.Count == 1 ? string.Empty : "s")}.", false);
        this.isMoveSelectedPlansPopupOpen = false;
        this.movingSelectedPlans = [];
        ImGui.CloseCurrentPopup();
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
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);

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

    private void DrawRenameFolderPopup()
    {
        if (this.renameFolderPopupRequested)
        {
            ImGui.OpenPopup("Rename folder");
            this.renameFolderPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Rename folder",
                ref this.isRenameFolderPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);

        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "New folder name",
            ref this.renameFolderName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);

        if (!string.IsNullOrWhiteSpace(this.renameFolderError))
            ImGui.TextColored(this.configuration.MissingTextColor, this.renameFolderError);

        if (ImGui.Button("Rename") || submitted)
            this.TryRenameFolder();

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            this.isRenameFolderPopupOpen = false;
            this.renamingFolderSource = string.Empty;
            this.renameFolderError = string.Empty;
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

    private void TryRenameFolder()
    {
        var cleanName = this.renameFolderName.Trim();
        if (cleanName.Length == 0)
        {
            this.renameFolderError = "Enter a folder name.";
            return;
        }

        if (string.Equals(cleanName, this.renamingFolderSource, StringComparison.OrdinalIgnoreCase))
        {
            this.isRenameFolderPopupOpen = false;
            this.renamingFolderSource = string.Empty;
            this.renameFolderError = string.Empty;
            ImGui.CloseCurrentPopup();
            return;
        }

        foreach (var plan in this.configuration.SavedRecipePlans
                     .Where(plan => string.Equals(
                         NormalizeFolderName(plan.FolderName),
                         NormalizeFolderName(this.renamingFolderSource),
                         StringComparison.OrdinalIgnoreCase)))
        {
            plan.FolderName = cleanName;
        }

        this.saveConfiguration();
        this.ShowPlanMessage($"Renamed folder to '{cleanName}'.", false);
        this.isRenameFolderPopupOpen = false;
        this.renamingFolderSource = string.Empty;
        this.renameFolderError = string.Empty;
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

    private static string NormalizeFolderName(string? folderName) =>
        string.IsNullOrWhiteSpace(folderName) ? string.Empty : folderName.Trim();

    private static List<string> GetSavedPlanFolders(IEnumerable<SavedRecipePlan> plans) =>
        plans.Select(plan => NormalizeFolderName(plan.FolderName))
            .Where(folderName => !string.IsNullOrWhiteSpace(folderName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folderName => folderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string GetFolderDisplayName(string folderName) =>
        string.IsNullOrWhiteSpace(folderName) ? "Unfiled" : folderName;

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
            FolderName = this.planFolderName.Trim(),
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
        this.planFolderName = string.Empty;
        this.planInputNonce++;
        this.selectedRecipes.Clear();
        this.integrationMessage = string.Empty;
        this.RefreshDetails(false);
    }

    private void ExportSavedPlansToClipboard(IEnumerable<SavedRecipePlan> plans)
    {
        var exportPlans = plans
            .Where(plan => this.configuration.SavedRecipePlans.Contains(plan))
            .Select(plan => new SavedRecipePlan
            {
                Name = plan.Name,
                FolderName = plan.FolderName,
                Recipes = plan.Recipes.Select(CloneSavedRecipe).ToList(),
            })
            .ToList();
        if (exportPlans.Count == 0)
        {
            this.ShowPlanMessage("Select at least one saved plan to export.", true);
            return;
        }

        ImGui.SetClipboardText(JsonSerializer.Serialize(exportPlans, new JsonSerializerOptions
        {
            WriteIndented = true,
        }));
        this.ShowPlanMessage($"Copied {exportPlans.Count} saved plan{(exportPlans.Count == 1 ? string.Empty : "s")} to the clipboard.", false);
    }

    private void ImportSavedPlansFromClipboard()
    {
        var clipboard = ImGui.GetClipboardText();
        if (string.IsNullOrWhiteSpace(clipboard))
        {
            this.ShowPlanMessage("Clipboard is empty.", true);
            return;
        }

        try
        {
            var importedPlans = JsonSerializer.Deserialize<List<SavedRecipePlan>>(clipboard) ?? [];
            if (importedPlans.Count == 0)
            {
                this.ShowPlanMessage("No Recipe Helper plans were found in the clipboard.", true);
                return;
            }

            foreach (var importedPlan in importedPlans.Where(plan => !string.IsNullOrWhiteSpace(plan.Name)))
            {
                var existingIndex = this.configuration.SavedRecipePlans.FindIndex(existing =>
                    string.Equals(existing.Name, importedPlan.Name, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                    this.configuration.SavedRecipePlans[existingIndex] = importedPlan;
                else
                    this.configuration.SavedRecipePlans.Add(importedPlan);
            }

            this.saveConfiguration();
            this.ShowPlanMessage($"Imported {importedPlans.Count} saved plan{(importedPlans.Count == 1 ? string.Empty : "s")}.", false);
        }
        catch
        {
            this.ShowPlanMessage("Clipboard data is not valid Recipe Helper plan data.", true);
        }
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

        this.planName = string.Empty;
        this.planFolderName = string.Empty;
        this.planInputNonce++;
        this.ShowPlanMessage($"Loaded plan '{savedPlan.Name}'.", false);
        this.searchText = string.Empty;
        this.searchResults = [];
        this.RefreshDetails(true);
    }

    private void LoadSavedPlans(
        IReadOnlyCollection<SavedRecipePlan> savedPlans,
        bool craftImmediately)
    {
        var plans = savedPlans
            .Where(plan => this.configuration.SavedRecipePlans.Contains(plan))
            .ToList();
        if (plans.Count == 0)
        {
            this.ShowPlanMessage("Select at least one saved plan.", true);
            return;
        }

        var combinedRecipes = plans
            .SelectMany(plan => plan.Recipes)
            .GroupBy(recipe => recipe.RecipeId)
            .Select(group =>
            {
                var first = group.First();
                var desiredAmount = (uint)Math.Min(
                    group.Aggregate(
                        0UL,
                        (total, recipe) => total + Math.Max(1U, recipe.DesiredAmount)),
                    uint.MaxValue);
                return new RecipePlanSelection(
                    new RecipeMatch(
                        first.RecipeId,
                        first.ResultItemId,
                        first.ResultName,
                        Math.Max(1U, first.ResultAmount)),
                    desiredAmount);
            })
            .OrderBy(selection => selection.Recipe.ResultName)
            .ToList();
        if (combinedRecipes.Count == 0)
        {
            this.ShowPlanMessage("The selected plans do not contain any recipes.", true);
            return;
        }

        this.selectedRecipes.Clear();
        this.selectedRecipes.AddRange(combinedRecipes);
        this.planName = string.Empty;
        this.planFolderName = string.Empty;
        this.planInputNonce++;
        this.searchText = string.Empty;
        this.searchResults = [];
        this.RefreshDetails(true);
        this.ShowPlanMessage(
            $"Loaded {plans.Count} saved plan{(plans.Count == 1 ? string.Empty : "s")}.",
            false);

        if (!craftImmediately)
            return;

        if (this.artisanCraftQueue.Count == 0)
        {
            this.integrationError = true;
            this.integrationMessage = string.IsNullOrWhiteSpace(this.artisanCraftQueueError)
                ? "The selected plans cannot be crafted from the available materials."
                : this.artisanCraftQueueError;
            return;
        }

        this.integrationError =
            !this.pluginIntegrationService.CraftAllWithArtisan(
                this.artisanCraftQueue,
                plans.Count,
                out this.integrationMessage);
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
                ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthFixed, this.ScaleUi(250f));
            ImGui.TableSetupColumn("Quantity", ImGuiTableColumnFlags.WidthFixed, this.ScaleUi(100f));
            ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, this.ScaleUi(74f));
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 1);

            ImGui.TableNextRow(ImGuiTableRowFlags.None, this.ScaleUi(36f));
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Recipe");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Qty");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Crafts");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Actions");

            foreach (var recipe in details.Recipes)
            {
                var selectionIndex = this.selectedRecipes.FindIndex(
                    selection => selection.Recipe.RecipeId == recipe.RecipeId);
                if (selectionIndex < 0)
                    continue;

                var selectedRecipeRowHeight = this.ScaleUi(36f);
                ImGui.TableNextRow(ImGuiTableRowFlags.None, this.ScaleUi(44f));
                ImGui.TableNextColumn();
                this.DrawInfoCard(
                    $"recipe-name-{recipe.RecipeId}",
                    new Vector2(-1, selectedRecipeRowHeight),
                    recipe.ResultName,
                    $"Yield {recipe.ResultAmount}",
                    AdjustColor(this.configuration.WindowBackgroundColor, 0.16f),
                    this.configuration.TextColor);

                ImGui.TableNextColumn();
                var quantityCursor = ImGui.GetCursorPos();
                this.DrawDecorativeCardBackground(
                    new Vector2(-1, selectedRecipeRowHeight),
                    this.configuration.InputCardColor);
                var inputHeight = ImGui.GetFrameHeight();
                ImGui.SetCursorPos(quantityCursor + new Vector2(
                    this.ScaleUi(10f),
                    MathF.Max(0f, (selectedRecipeRowHeight - inputHeight) / 2f)));
                ImGui.SetNextItemWidth(-1);
                ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
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
                ImGui.PopStyleColor(3);

                ImGui.TableNextColumn();
                this.DrawValueCard(
                    $"recipe-crafts-{recipe.RecipeId}",
                    new Vector2(-1, selectedRecipeRowHeight),
                    recipe.CraftCount.ToString(),
                    WithAlpha(this.configuration.AccentColor, 0.14f),
                    this.configuration.TextColor);

                ImGui.TableNextColumn();
                var actionWidth = Math.Max(this.ScaleUi(212f), ImGui.GetContentRegionAvail().X);
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                var craftButtonWidth = this.ScaleUi(84f);
                var teamcraftButtonWidth = this.ScaleUi(78f);
                var removeButtonWidth = this.ScaleUi(60f);
                var totalButtonWidth =
                    craftButtonWidth +
                    teamcraftButtonWidth +
                    removeButtonWidth +
                    (spacing * 2f);
                var actionCursor = ImGui.GetCursorPos();
                this.DrawDecorativeCardBackground(
                    new Vector2(actionWidth, selectedRecipeRowHeight),
                    AdjustColor(this.configuration.WindowBackgroundColor, 0.09f));
                var buttonHeight = ImGui.GetFrameHeight();
                ImGui.SetCursorPos(new Vector2(
                    actionCursor.X + MathF.Max(0f, (actionWidth - totalButtonWidth) / 2f),
                    actionCursor.Y + MathF.Max(0f, (selectedRecipeRowHeight - buttonHeight) / 2f)));
                if (ImGui.Button($"Craft Items##plan-artisan-{recipe.RecipeId}", new Vector2(craftButtonWidth, 0)))
                {
                    this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                        recipe.RecipeId,
                        recipe.CraftCount,
                        out this.integrationMessage);
                }
                DrawTooltipIfHovered("Open this recipe in Artisan with the required craft count.");

                ImGui.SameLine();
                if (ImGui.Button($"Teamcraft##plan-teamcraft-{recipe.RecipeId}", new Vector2(teamcraftButtonWidth, 0)))
                {
                    this.integrationError = !this.pluginIntegrationService.OpenInTeamcraft(
                        recipe.ResultItemId,
                        recipe.DesiredAmount,
                        out this.integrationMessage);
                }
                DrawTooltipIfHovered("Open this recipe as a Teamcraft import list.");

                ImGui.SameLine();
                if (ImGui.Button($"Remove##plan-remove-{recipe.RecipeId}", new Vector2(removeButtonWidth, 0)))
                    recipeToRemove = recipe.RecipeId;
                DrawTooltipIfHovered("Remove this recipe from the current plan.");
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
        var cardWidth = Math.Max(this.ScaleUi(92f), (ImGui.GetContentRegionAvail().X - this.ScaleUi(12f)) / 3f);
        this.DrawSummaryCard("Recipes", details.Recipes.Count.ToString(), cardWidth);
        ImGui.SameLine();
        this.DrawSummaryCard("Total Crafts", totalCrafts.ToString(), cardWidth);
        ImGui.SameLine();
        this.DrawSummaryCard("Raw Items", details.RawMaterials.Count.ToString(), cardWidth);
    }

    private void ToggleObtainedRawMaterials()
    {
        this.configuration.ShowObtainedRawMaterials = !this.configuration.ShowObtainedRawMaterials;
        this.saveConfiguration();
    }

    private bool DrawCollapsibleSection(
        string title,
        string subtitle,
        string sectionId,
        string? actionLabel = null,
        Action? action = null)
    {
        ImGui.Spacing();
        var isOpen = ImGui.CollapsingHeader(
            $"{title}##{sectionId}",
            ImGuiTreeNodeFlags.DefaultOpen);

        if (!isOpen)
            return false;

        ImGui.TextDisabled(subtitle);
        if (!string.IsNullOrWhiteSpace(actionLabel) && action is not null)
        {
            if (ImGui.SmallButton(actionLabel))
                action();
        }
        ImGui.Spacing();
        return true;
    }

    private static bool IsElementalCatalyst(IngredientNeed material) =>
        material.ItemId is >= 2 and <= 19;

    private IReadOnlyList<IngredientNeed> OrderMaterialsForDisplay(IEnumerable<IngredientNeed> materials) =>
        materials
            .Select(material =>
            {
                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(material.ReductionSources);
                var availabilityText = GetIngredientAvailabilityText(material, reductionSource, this.aetherialReductionService);
                var canGather = this.CanGatherIngredient(material, reductionSource, availabilityText);
                return new
                {
                    Material = material,
                    SortCategory = GetIngredientSortCategory(canGather, availabilityText, IsVendorSource(material)),
                    WaitSeconds = GetGenericAvailabilityWaitSeconds(availabilityText),
                };
            })
            .OrderBy(entry => entry.SortCategory)
            .ThenBy(entry => entry.WaitSeconds)
            .ThenBy(entry => entry.Material.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Material)
            .ToList();

    private void DrawMaterialsTable(string tableId, IReadOnlyList<IngredientNeed> materials)
    {
        var displayedMaterials = this.OrderMaterialsForDisplay(
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
        var showStock = displayedMaterials.Any(material => material.OwnedNq > 0 || material.OwnedHq > 0);
        var showFoundIn = displayedMaterials.Any(material => material.Locations.Count > 0);
        var showRawCraftStatus =
            isDirectIngredients &&
            displayedMaterials.Any(material => material.CanCraftMissingFromRaw.HasValue);
        const bool showMissing = true;
        var columnCount =
            1 +
            (showMissing ? 1 : 0) +
            1 +
            (showTravel ? 1 : 0) +
            (showAvailable ? 1 : 0) +
            (showStock ? 1 : 0) +
            (showFoundIn ? 1 : 0) +
            (showRawCraftStatus ? 1 : 0);
        var tableFlags =
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingFixedFit;
        var requestedTableWidth =
            this.ScaleUi(220f) +
            this.ScaleUi(45f) +
            (showTravel ? this.ScaleUi(68f) : 0f) +
            (showAvailable ? this.ScaleUi(115f) : 0f) +
            (showStock ? this.ScaleUi(88f) : 0f) +
            (showFoundIn ? this.ScaleUi(150f) : 0f) +
            (showRawCraftStatus ? this.ScaleUi(96f) : 0f) +
            (showMissing ? this.ScaleUi(75f) : 0f) +
            (ImGui.GetStyle().CellPadding.X * 2 * columnCount) +
            2f;
        var availableTableWidth = ImGui.GetContentRegionAvail().X;
        var needsHorizontalScroll = requestedTableWidth > availableTableWidth;
        if (needsHorizontalScroll)
            tableFlags |= ImGuiTableFlags.ScrollX;

        void SetupColumn(string label, string key, float width)
        {
            ImGui.TableSetupColumn(
                label,
                ImGuiTableColumnFlags.WidthFixed,
                width);
        }

        var tableHeight =
            (this.ScaleUi(44f) * displayedMaterials.Count) +
            this.ScaleUi(36f) +
            (needsHorizontalScroll ? ImGui.GetStyle().ScrollbarSize : 0f) +
            2f;
        if (ImGui.BeginTable(
                tableId,
                columnCount,
                tableFlags,
                new Vector2(Math.Max(1f, availableTableWidth), tableHeight)))
        {
            SetupColumn("Ingredient", "ingredient", this.ScaleUi(220f));
            SetupColumn("Need", "need", this.ScaleUi(45f));
            if (showMissing)
                SetupColumn("Missing", "missing", this.ScaleUi(75f));
            if (showTravel)
                SetupColumn("Travel", "travel", this.ScaleUi(68f));
            if (showAvailable)
                SetupColumn("Available", "available", this.ScaleUi(115f));
            if (showStock)
                SetupColumn("Stock", "stock", this.ScaleUi(88f));
            if (showFoundIn)
                SetupColumn("Found in", "found", this.ScaleUi(150f));
            if (showRawCraftStatus)
                SetupColumn("From raw", "raw", this.ScaleUi(96f));
            ImGui.TableNextRow(ImGuiTableRowFlags.None, this.ScaleUi(36f));
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Ingredient");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Need");
            if (showMissing)
            {
                ImGui.TableNextColumn();
                this.DrawHeaderCard("Missing");
            }
            if (showTravel)
            {
                ImGui.TableNextColumn();
                this.DrawHeaderCard("Travel");
            }
            if (showAvailable)
            {
                ImGui.TableNextColumn();
                this.DrawHeaderCard("Available");
            }
            if (showStock)
            {
                ImGui.TableNextColumn();
                this.DrawHeaderCard("Stock");
            }
            if (showFoundIn)
            {
                ImGui.TableNextColumn();
                this.DrawHeaderCard("Found In");
            }
            if (showRawCraftStatus)
            {
                ImGui.TableNextColumn();
                this.DrawHeaderCard("From Raw");
            }

            foreach (var ingredient in displayedMaterials)
            {
                ImGui.TableNextRow(ImGuiTableRowFlags.None, this.ScaleUi(44f));
                var hasTimedWindow = this.aetherialReductionService.HasTimedWindow(
                    ingredient.ItemId,
                    ingredient.ReductionSources);
                var rowColor = ingredient.HasEnough
                    ? this.configuration.EnoughRowColor
                    : showRawCraftStatus && ingredient.CanCraftMissingFromRaw is true
                        ? WithAlpha(this.configuration.WarningTextColor, 0.18f)
                        : WithAlpha(this.configuration.MissingTextColor, 0.18f);

                ImGui.TableNextColumn();
                this.DrawInfoCard(
                    $"{tableId}-ingredient-{ingredient.ItemId}",
                    new Vector2(-1, this.ScaleUi(36f)),
                    ingredient.Name,
                    ingredient.Source,
                    rowColor,
                    this.configuration.TextColor);
                MaterialUsageTooltip.Draw(
                    this.marketboardPriceService,
                    this.configuration,
                    ingredient);

                ImGui.TableNextColumn();
                var needBackground = ingredient.HasEnough
                    ? this.configuration.EnoughRowColor
                    : WithAlpha(this.configuration.AccentColor, 0.14f);
                this.DrawValueCard(
                    $"{tableId}-need-{ingredient.ItemId}",
                    new Vector2(-1, this.ScaleUi(36f)),
                    ingredient.Required.ToString(),
                    needBackground,
                    this.configuration.TextColor);

                if (showMissing)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.HasEnough)
                    {
                        this.DrawValueCard(
                            $"{tableId}-missing-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            "Ready",
                            WithAlpha(this.configuration.SuccessTextColor, 0.18f),
                            this.configuration.SuccessTextColor);
                    }
                    else if (showRawCraftStatus && ingredient.CanCraftMissingFromRaw is true)
                    {
                        this.DrawValueCard(
                            $"{tableId}-missing-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            "Craftable",
                            WithAlpha(this.configuration.WarningTextColor, 0.18f),
                            this.configuration.WarningTextColor);
                    }
                    else
                    {
                        this.DrawValueCard(
                            $"{tableId}-missing-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            ingredient.Missing.ToString(),
                            WithAlpha(this.configuration.MissingTextColor, 0.18f),
                            this.configuration.MissingTextColor);
                    }
                }

                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(ingredient.ReductionSources);
                if (showTravel)
                {
                    ImGui.TableNextColumn();
                    if (this.CanGatherIngredient(ingredient, reductionSource))
                    {
                        var buttonWidth = this.ScaleUi(64f);
                        var columnWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
                        this.DrawDecorativeCardBackground(
                            new Vector2(columnWidth, this.ScaleUi(36f)),
                            WithAlpha(this.configuration.AccentColor, 0.12f));
                        var buttonCursor = ImGui.GetCursorPos();
                        ImGui.SetCursorPos(new Vector2(
                            buttonCursor.X + MathF.Max(0f, (columnWidth - buttonWidth) / 2f),
                            buttonCursor.Y - this.ScaleUi(34f)));
                        if (ImGui.Button($"Gather##{tableId}-{ingredient.ItemId}", new Vector2(buttonWidth, 0)))
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

                        DrawTooltipIfHovered("Teleport to closest Aetheryte");
                    }
                    else
                    {
                        this.DrawValueCard(
                            $"{tableId}-travel-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            "-",
                            AdjustColor(this.configuration.WindowBackgroundColor, 0.05f),
                            AdjustColor(this.configuration.TextColor, -0.30f));
                    }
                }

                if (showAvailable)
                {
                    ImGui.TableNextColumn();
                    this.DrawAvailabilityCard(tableId, ingredient, reductionSource);
                }

                if (showStock)
                {
                    ImGui.TableNextColumn();
                    this.DrawValueCard(
                        $"{tableId}-stock-{ingredient.ItemId}",
                        new Vector2(-1, this.ScaleUi(36f)),
                        FormatStockDisplay(ingredient),
                        WithAlpha(this.configuration.AccentColor, 0.10f),
                        this.configuration.TextColor);
                }

                if (showFoundIn)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.Locations.Count > 0)
                    {
                        var locationsText = string.Join(", ", ingredient.Locations);
                        this.DrawValueCard(
                            $"{tableId}-found-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            TrimDisplayText(locationsText, 42),
                            AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                            this.configuration.TextColor);
                        if (locationsText.Length > 42 && ImGui.IsItemHovered())
                            ImGui.SetTooltip(locationsText);
                    }
                    else
                    {
                        this.DrawValueCard(
                            $"{tableId}-found-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            "-",
                            AdjustColor(this.configuration.WindowBackgroundColor, 0.05f),
                            AdjustColor(this.configuration.TextColor, -0.30f));
                    }
                }

                if (showRawCraftStatus)
                {
                    ImGui.TableNextColumn();
                    if (ingredient.HasEnough)
                    {
                        this.DrawValueCard(
                            $"{tableId}-raw-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            "Owned",
                            WithAlpha(this.configuration.SuccessTextColor, 0.18f),
                            this.configuration.SuccessTextColor);
                    }
                    else if (ingredient.CanCraftMissingFromRaw is true &&
                             ingredient.RawCraftRecipeId is { } rawCraftRecipeId)
                    {
                        var cardSize = this.ResolveCardSize(new Vector2(-1, this.ScaleUi(36f)), 42f);
                        this.DrawDecorativeCardBackground(
                            cardSize,
                            WithAlpha(this.configuration.ReadyButtonColor, 0.16f));
                        var cardMin = ImGui.GetItemRectMin();
                        var buttonSize = this.ScaleUi(new Vector2(96f, 26f));
                        ImGui.SetCursorScreenPos(new Vector2(
                            cardMin.X + MathF.Max(0f, (cardSize.X - buttonSize.X) / 2f),
                            cardMin.Y + MathF.Max(0f, (cardSize.Y - buttonSize.Y) / 2f)));
                        ImGui.PushStyleColor(ImGuiCol.Button, this.configuration.ReadyButtonColor);
                        var readyToCraftClicked =
                            ImGui.Button($"Ready to craft##{tableId}-raw-{ingredient.ItemId}", buttonSize);
                        ImGui.PopStyleColor();
                        if (readyToCraftClicked)
                        {
                            this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                                rawCraftRecipeId,
                                ingredient.RawCraftCount,
                                out this.integrationMessage);
                        }
                        DrawTooltipIfHovered("Open Artisan to craft the missing amount from raw materials.");
                    }
                    else
                    {
                        this.DrawValueCard(
                            $"{tableId}-raw-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            "-",
                            AdjustColor(this.configuration.WindowBackgroundColor, 0.05f),
                            AdjustColor(this.configuration.TextColor, -0.30f));
                    }
                }

            }

            ImGui.EndTable();
        }
    }

    private void DrawSummaryCard(string label, string value, float width)
    {
        var size = new Vector2(width, this.ScaleUi(48f));
        var position = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        var topColor = ImGui.GetColorU32(WithAlpha(AdjustColor(this.configuration.AccentColor, 0.08f), 0.92f));
        var bottomColor = ImGui.GetColorU32(WithAlpha(AdjustColor(this.configuration.AccentColor, -0.04f), 0.74f));
        drawList.AddRectFilledMultiColor(
            position,
            position + size,
            topColor,
            topColor,
            bottomColor,
            bottomColor);
        drawList.AddRectFilled(
            position,
            position + size,
            ImGui.GetColorU32(WithAlpha(this.configuration.WindowBackgroundColor, 0.18f)),
            this.ScaleUi(14f));
        var labelSize = ImGui.CalcTextSize(label);
        var valueSize = ImGui.CalcTextSize(value);
        drawList.AddText(
            position + new Vector2((size.X - labelSize.X) / 2f, this.ScaleUi(7f)),
            ImGui.GetColorU32(AdjustColor(this.configuration.TextColor, -0.12f)),
            label);
        drawList.AddText(
            position + new Vector2((size.X - valueSize.X) / 2f, this.ScaleUi(23f)),
            ImGui.GetColorU32(this.configuration.TextColor),
            value);
        ImGui.Dummy(size);
    }

    private void DrawHeaderCard(string label)
    {
        var size = new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), this.ScaleUi(28f));
        var position = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilledMultiColor(
            position,
            position + size,
            ImGui.GetColorU32(WithAlpha(AdjustColor(this.configuration.AccentColor, 0.15f), 0.92f)),
            ImGui.GetColorU32(WithAlpha(AdjustColor(this.configuration.AccentColor, 0.04f), 0.92f)),
            ImGui.GetColorU32(WithAlpha(AdjustColor(this.configuration.AccentColor, -0.02f), 0.78f)),
            ImGui.GetColorU32(WithAlpha(AdjustColor(this.configuration.AccentColor, -0.10f), 0.78f)));
        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText(
            position + new Vector2((size.X - textSize.X) / 2f, this.ScaleUi(7f)),
            ImGui.GetColorU32(this.configuration.TextColor),
            label);
        ImGui.Dummy(size);
    }

    private void DrawInfoCard(
        string id,
        Vector2 size,
        string title,
        string subtitle,
        Vector4 backgroundColor,
        Vector4 textColor)
    {
        var resolvedSize = this.ResolveCardSize(size, 42f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, resolvedSize);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + resolvedSize, ImGui.GetColorU32(backgroundColor), this.ScaleUi(12f));

        var textWidth = Math.Max(1f, resolvedSize.X - this.ScaleUi(28f));
        var safeTitle = TrimDisplayTextToWidth(title, textWidth);
        var safeSubtitle = TrimDisplayTextToWidth(subtitle, textWidth);
        DrawStackedTextBlock(
            drawList,
            position + new Vector2(this.ScaleUi(14f), 0f),
            new Vector2(textWidth, resolvedSize.Y),
            safeTitle,
            safeSubtitle,
            ImGui.GetColorU32(textColor),
            ImGui.GetColorU32(AdjustColor(textColor, -0.24f)),
            this.ScaleUi(2f),
            0f,
            false);
        if ((title.Length > safeTitle.Length || subtitle.Length > safeSubtitle.Length) && ImGui.IsItemHovered())
            ImGui.SetTooltip($"{title}\n{subtitle}");
    }

    private void DrawValueCard(
        string id,
        Vector2 size,
        string value,
        Vector4 backgroundColor,
        Vector4 textColor)
    {
        var resolvedSize = this.ResolveCardSize(size, 42f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, resolvedSize);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + resolvedSize, ImGui.GetColorU32(backgroundColor), this.ScaleUi(12f));

        var displayValue = TrimDisplayText(value, 28);
        var valueSize = ImGui.CalcTextSize(displayValue);
        drawList.AddText(
            position + new Vector2((resolvedSize.X - valueSize.X) / 2f, (resolvedSize.Y - valueSize.Y) / 2f),
            ImGui.GetColorU32(textColor),
            displayValue);
        if (value.Length > displayValue.Length && ImGui.IsItemHovered())
            ImGui.SetTooltip(value);
    }

    private void DrawDecorativeCardBackground(Vector2 size, Vector4 backgroundColor)
    {
        var resolvedSize = this.ResolveCardSize(size, 42f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.Dummy(resolvedSize);
        ImGui.GetWindowDrawList().AddRectFilled(
            position,
            position + resolvedSize,
            ImGui.GetColorU32(backgroundColor),
            this.ScaleUi(12f));
    }

    private Vector2 ResolveCardSize(Vector2 size, float defaultHeight)
    {
        var width = size.X <= 0f ? Math.Max(1f, ImGui.GetContentRegionAvail().X) : size.X;
        var height = size.Y <= 0f ? this.ScaleUi(defaultHeight) : size.Y;
        return new Vector2(width, height);
    }

    private static string TrimDisplayText(string text, int maxLength) =>
        string.IsNullOrWhiteSpace(text) || text.Length <= maxLength
            ? text
            : $"{text[..Math.Max(0, maxLength - 3)]}...";

    private static string TrimDisplayTextToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        for (var length = text.Length - 1; length > 0; length--)
        {
            var trimmed = text[..length].TrimEnd() + ellipsis;
            if (ImGui.CalcTextSize(trimmed).X <= maxWidth)
                return trimmed;
        }

        return ellipsis;
    }

    private static string WrapTextToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text) || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return text;

        var lines = new List<string>();
        var current = words[0];
        for (var i = 1; i < words.Length; i++)
        {
            var candidate = $"{current} {words[i]}";
            if (ImGui.CalcTextSize(candidate).X <= maxWidth)
                current = candidate;
            else
            {
                lines.Add(current);
                current = words[i];
            }
        }

        lines.Add(current);
        return string.Join('\n', lines);
    }

    private static void DrawStackedTextBlock(
        ImDrawListPtr drawList,
        Vector2 position,
        Vector2 size,
        string title,
        string subtitle,
        uint titleColor,
        uint subtitleColor,
        float gap,
        float minVerticalPadding,
        bool centeredHorizontally)
    {
        var titleSize = ImGui.CalcTextSize(title);
        var subtitleSize = ImGui.CalcTextSize(subtitle);
        var totalHeight = titleSize.Y + gap + subtitleSize.Y;
        var textTop = position.Y + MathF.Max(minVerticalPadding, (size.Y - totalHeight) / 2f);
        var titleX = centeredHorizontally
            ? position.X + MathF.Max(0f, (size.X - titleSize.X) / 2f)
            : position.X;
        var subtitleX = centeredHorizontally
            ? position.X + MathF.Max(0f, (size.X - subtitleSize.X) / 2f)
            : position.X;
        drawList.AddText(new Vector2(titleX, textTop), titleColor, title);
        drawList.AddText(new Vector2(subtitleX, textTop + titleSize.Y + gap), subtitleColor, subtitle);
    }

    private void DrawAvailabilityCard(
        string tableId,
        IngredientNeed ingredient,
        AetherialReductionSource? reductionSource)
    {
        var timerText = reductionSource is not null
            ? this.aetherialReductionService.GetTimerText(reductionSource)
            : this.aetherialReductionService.GetGatheringTimerText(ingredient.ItemId);
        if (string.IsNullOrWhiteSpace(timerText))
        {
            this.DrawValueCard(
                $"{tableId}-available-{ingredient.ItemId}",
                new Vector2(-1, this.ScaleUi(36f)),
                "-",
                AdjustColor(this.configuration.WindowBackgroundColor, 0.05f),
                AdjustColor(this.configuration.TextColor, -0.30f));
            return;
        }

        var displayText = FormatAvailabilityDisplay(timerText, out var tooltip, out var isAvailableNow);
        var waitSeconds = GetAvailabilityWaitSeconds(timerText);
        var isImminent = !isAvailableNow && waitSeconds is >= 0 and <= 60;
        var size = this.ResolveCardSize(new Vector2(-1, this.ScaleUi(36f)), 36f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"{tableId}-available-{ingredient.ItemId}", size);
        var drawList = ImGui.GetWindowDrawList();
        var backgroundColor = isAvailableNow
            ? WithAlpha(this.configuration.SuccessTextColor, 0.20f)
            : isImminent
                ? WithAlpha(this.configuration.WarningTextColor, 0.22f)
                : WithAlpha(this.configuration.AccentColor, 0.16f);
        var textColor = isAvailableNow
            ? this.configuration.SuccessTextColor
            : isImminent
                ? this.configuration.WarningTextColor
                : this.configuration.TextColor;
        drawList.AddRectFilled(position, position + size, ImGui.GetColorU32(backgroundColor), this.ScaleUi(12f));
        var displaySize = ImGui.CalcTextSize(displayText);
        drawList.AddText(
            position + new Vector2((size.X - displaySize.X) / 2f, (size.Y - displaySize.Y) / 2f),
            ImGui.GetColorU32(textColor),
            displayText);

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private static string FormatStockDisplay(IngredientNeed ingredient) => ingredient switch
    {
        { OwnedNq: > 0, OwnedHq: > 0 } => $"{ingredient.OwnedNq} | HQ {ingredient.OwnedHq}",
        { OwnedHq: > 0 } => $"HQ {ingredient.OwnedHq}",
        _ => ingredient.OwnedNq.ToString(),
    };

    private static string FormatAvailabilityDisplay(
        string timerText,
        out string tooltip,
        out bool isAvailableNow)
    {
        var normalized = timerText.Replace("Â·", "-").Replace("·", "-").Trim();
        isAvailableNow = normalized.StartsWith("Now", StringComparison.OrdinalIgnoreCase);
        tooltip = normalized;
        if (isAvailableNow)
            return "NOW";

        var durationText = normalized.StartsWith("In ", StringComparison.OrdinalIgnoreCase)
            ? normalized[3..].Trim()
            : normalized;
        var parts = durationText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hours = parts.Where(part => part.EndsWith('h')).Select(part => ParseTimePart(part, 'h')).DefaultIfEmpty(0).First();
        var minutes = parts.Where(part => part.EndsWith('m')).Select(part => ParseTimePart(part, 'm')).DefaultIfEmpty(0).First();
        var seconds = parts.Where(part => part.EndsWith('s')).Select(part => ParseTimePart(part, 's')).DefaultIfEmpty(0).First();
        if (hours == 0 && minutes == 0 && seconds == 0)
            return TrimDisplayText(normalized, 12);
        return hours > 0
            ? $"{hours:D2}:{minutes:D2}:{seconds:D2}"
            : $"{minutes:D2}:{seconds:D2}";
    }

    private static int GetAvailabilityWaitSeconds(string timerText)
    {
        var normalized = timerText.Replace("Ã‚Â·", "-").Replace("Â·", "-").Trim();
        if (normalized.StartsWith("Now", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (!normalized.StartsWith("In ", StringComparison.OrdinalIgnoreCase))
            return -1;

        var durationText = normalized[3..].Trim();
        var parts = durationText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hours = parts.Where(part => part.EndsWith('h')).Select(part => ParseTimePart(part, 'h')).DefaultIfEmpty(0).First();
        var minutes = parts.Where(part => part.EndsWith('m')).Select(part => ParseTimePart(part, 'm')).DefaultIfEmpty(0).First();
        var seconds = parts.Where(part => part.EndsWith('s')).Select(part => ParseTimePart(part, 's')).DefaultIfEmpty(0).First();
        return (hours * 3600) + (minutes * 60) + seconds;
    }

    private static int ParseTimePart(string text, char suffix)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var cleaned = text.Replace(suffix.ToString(), string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return int.TryParse(cleaned, out var value) ? value : 0;
    }

    private bool CanGatherIngredient(
        IngredientNeed ingredient,
        AetherialReductionSource? reductionSource,
        string? availabilityText = null) =>
        ingredient.IsGatherable ||
        HasGatherableSourceLabel(ingredient.Source) ||
        reductionSource is not null ||
        HasTimedAvailability(availabilityText);

    private static string? GetIngredientAvailabilityText(
        IngredientNeed ingredient,
        AetherialReductionSource? reductionSource,
        AetherialReductionService aetherialReductionService) =>
        reductionSource is not null
            ? aetherialReductionService.GetTimerText(reductionSource)
            : aetherialReductionService.GetGatheringTimerText(ingredient.ItemId);

    private static bool HasTimedAvailability(string? availabilityText) =>
        !string.IsNullOrWhiteSpace(availabilityText) &&
        (availabilityText.StartsWith("Now", StringComparison.OrdinalIgnoreCase) ||
         availabilityText.StartsWith("In ", StringComparison.OrdinalIgnoreCase));

    private static bool IsVendorSource(IngredientNeed ingredient) =>
        ingredient.Source.Contains("Vendor", StringComparison.OrdinalIgnoreCase);

    private static bool HasGatherableSourceLabel(string source) =>
        source.Contains("Gatherable", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("Aetherial reduction", StringComparison.OrdinalIgnoreCase) ||
        source.Contains("Fishing", StringComparison.OrdinalIgnoreCase);

    private static int GetIngredientSortCategory(bool canGather, string? availabilityText, bool isVendor)
    {
        if (canGather && HasTimedAvailability(availabilityText))
            return 0;
        if (canGather)
            return 1;
        if (isVendor)
            return 3;
        return 2;
    }

    private static int GetGenericAvailabilityWaitSeconds(string? availabilityText)
    {
        if (string.IsNullOrWhiteSpace(availabilityText))
            return int.MaxValue;
        if (availabilityText.StartsWith("Now", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (!availabilityText.StartsWith("In ", StringComparison.OrdinalIgnoreCase))
            return int.MaxValue;

        var durationText = availabilityText[3..].Trim();
        var parts = durationText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hours = parts.Where(part => part.EndsWith('h')).Select(part => ParseTimePart(part, 'h')).DefaultIfEmpty(0).First();
        var minutes = parts.Where(part => part.EndsWith('m')).Select(part => ParseTimePart(part, 'm')).DefaultIfEmpty(0).First();
        var seconds = parts.Where(part => part.EndsWith('s')).Select(part => ParseTimePart(part, 's')).DefaultIfEmpty(0).First();
        return (hours * 3600) + (minutes * 60) + seconds;
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
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);

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

    private void DrawTooltipIfHovered(string text)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }

    private void ApplyMainWindowScaleIfNeeded()
    {
        var scaledPercent = WindowTheme.GetMainWindowScalePercent(this.configuration);
        if (scaledPercent == this.appliedMainWindowScalePercent)
            return;

        var currentSize = ImGui.GetWindowSize();
        if (currentSize.X <= 0 || currentSize.Y <= 0)
            currentSize = this.ScaleMainWindowSize(BaseWindowSize, this.appliedMainWindowScalePercent);

        var ratio = scaledPercent / (float)this.appliedMainWindowScalePercent;
        this.appliedMainWindowScalePercent = scaledPercent;
        this.ApplyMainWindowConstraints();

        var minimumSize = this.ScaleMainWindowSize(BaseMinimumWindowSize, scaledPercent);
        var resized = new Vector2(
            Math.Max(currentSize.X * ratio, minimumSize.X),
            Math.Max(currentSize.Y * ratio, minimumSize.Y));
        ImGui.SetWindowSize(resized, ImGuiCond.Always);
    }

    private void ApplyMainWindowConstraints()
    {
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = this.ScaleMainWindowSize(BaseMinimumWindowSize, this.appliedMainWindowScalePercent),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    private Vector2 ScaleMainWindowSize(Vector2 size, int scalePercent) =>
        size * (scalePercent / 100f);

    private float ScaleUi(float value) =>
        value * WindowTheme.GetMainInterfaceScale(this.configuration);

    private Vector2 ScaleUi(Vector2 value) =>
        value * WindowTheme.GetMainInterfaceScale(this.configuration);
}
