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
    private static readonly IReadOnlyList<string> DefaultJobFilters =
    [
        "CRP",
        "BSM",
        "ARM",
        "GSM",
        "LTW",
        "WVR",
        "ALC",
        "CUL",
        "MIN",
        "BTN",
        "FSH",
    ];

    private static readonly IReadOnlyList<string> GathererJobFilters =
    [
        "MIN",
        "BTN",
        "FSH",
    ];

    private sealed class SavedPlanFolderNode
    {
        public required string Name { get; init; }

        public required string FullPath { get; init; }

        public List<SavedRecipePlan> Plans { get; } = [];

        public List<SavedPlanFolderNode> Children { get; } = [];

        public int TotalPlanCount => this.Plans.Count + this.Children.Sum(child => child.TotalPlanCount);
    }

    private sealed class SupplementalItemSelection
    {
        public required RecipeMatch Item { get; init; }

        public uint DesiredAmount { get; set; }
    }

    private enum PlanSaveScope
    {
        Recipe,
        Gatherable,
        Collectable,
        DirectIngredient,
        RawMaterial,
    }

    private sealed class PlanSaveDraft
    {
        public string Name = string.Empty;

        public string FolderName = string.Empty;

        public int InputNonce;
    }

    private sealed record SupplementalItemRow(
        RecipeMatch Item,
        IngredientNeed Need,
        CollectibleRewardInfo? RewardInfo,
        string Subtitle,
        uint DisplayQuantity,
        bool IsEditable,
        bool IsRecipeCollectable);

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
    private readonly ArtisanCraftOverlayWindow artisanCraftOverlayWindow;
    private readonly string versionText;
    private IReadOnlyList<RecipeMatch> searchResults = [];
    private IReadOnlyDictionary<uint, CraftableRecipeAvailability> craftableAvailability =
        new Dictionary<uint, CraftableRecipeAvailability>();
    private bool showingCraftableRecipes;
    private readonly List<RecipePlanSelection> selectedRecipes = [];
    private readonly List<SupplementalItemSelection> selectedGatherables = [];
    private readonly List<SupplementalItemSelection> selectedCollectables = [];
    private readonly List<SavedSupplementalPlanEntry> selectedDirectIngredients = [];
    private readonly List<SavedSupplementalPlanEntry> selectedRawMaterials = [];
    private readonly HashSet<SavedRecipePlan> selectedSavedPlans = [];
    private readonly HashSet<string> autoCloseSectionIds =
    [
        "saved-plans-section",
        "gatherables-section",
        "collectables-section",
    ];
    private readonly HashSet<string> savedPlanFoldersToClose = new(StringComparer.OrdinalIgnoreCase);
    private bool savedPlanCraftAvailabilityDirty = true;
    private bool canCraftSelectedSavedPlans;
    private SavedRecipePlan? loadedSavedPlan;
    private IReadOnlyList<ArtisanCraftQueueEntry> artisanCraftQueue = [];
    private string artisanCraftQueueError = string.Empty;
    private bool canCraftAllFromLiveInventory;
    private string searchText = string.Empty;
    private string resultFilter = string.Empty;
    private string selectedJobFilter = string.Empty;
    private string selectedSearchTypeFilter = string.Empty;
    private string selectedScripFilter = string.Empty;
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
    private uint observedCraftAllStopCount;
    private uint observedDreamCompletionSequence;
    private bool observedArtisanProgressActive;
    private bool dreamCraftPending;
    private bool inventoryRefreshRequested;
    private readonly PlanSaveDraft recipePlanDraft = new();
    private readonly PlanSaveDraft gatherablePlanDraft = new();
    private readonly PlanSaveDraft collectablePlanDraft = new();
    private readonly PlanSaveDraft directIngredientPlanDraft = new();
    private readonly PlanSaveDraft rawMaterialPlanDraft = new();
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
    private string movingFolderSource = string.Empty;
    private string moveFolderParentName = string.Empty;
    private string moveFolderError = string.Empty;
    private bool moveFolderPopupRequested;
    private bool isMoveFolderPopupOpen;
    private string createFolderName = string.Empty;
    private string createFolderError = string.Empty;
    private bool createFolderPopupRequested;
    private bool isCreateFolderPopupOpen;
    private int appliedMainWindowScalePercent;
    private Vector2 lastMainWindowPosition = new(60f, 60f);
    private Vector2 lastMainWindowSize = BaseWindowSize;
    private readonly HashSet<string> autoOpenSectionIds = [];

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
        RawMaterialsOverlayWindow rawMaterialsOverlayWindow,
        ArtisanCraftOverlayWindow artisanCraftOverlayWindow)
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
        this.observedCraftAllStopCount =
            pluginIntegrationService.CraftAllStopCount;
        this.observedDreamCompletionSequence =
            gwenDreamService.CompletionSequence;
        this.aetherialReductionService = aetherialReductionService;
        this.configuration = configuration;
        this.versionText = typeof(RecipeWindow).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
        this.openSettings = openSettings;
        this.saveConfiguration = saveConfiguration;
        this.rawMaterialsOverlayWindow = rawMaterialsOverlayWindow;
        this.artisanCraftOverlayWindow = artisanCraftOverlayWindow;
        this.inventoryService.InventoryChanged += this.OnInventoryChanged;
        this.observedArtisanProgressActive =
            this.pluginIntegrationService.GetCraftAllProgressSnapshot().IsActive;
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
        this.lastMainWindowPosition = ImGui.GetWindowPos();
        this.lastMainWindowSize = ImGui.GetWindowSize();

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
                this.RefreshSearchResultsForVisibleFilters();
                var availableJobFilters = this.GetAvailableJobFilters();
                if (!string.IsNullOrWhiteSpace(this.selectedJobFilter) &&
                    !availableJobFilters.Contains(this.selectedJobFilter, StringComparer.OrdinalIgnoreCase))
                    this.selectedJobFilter = string.Empty;

                var displayedResults = this.searchResults
                    .Where(result =>
                        (string.IsNullOrWhiteSpace(this.resultFilter) ||
                         result.ResultName.Contains(
                             this.resultFilter.Trim(),
                             StringComparison.CurrentCultureIgnoreCase)) &&
                        this.MatchesSearchTypeFilter(result) &&
                        this.MatchesScripFilter(result) &&
                        this.MatchesJobFilter(result))
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

                ImGui.SetNextItemWidth(-1);
                ImGui.InputTextWithHint(
                    "##result-filter",
                    "Filter these results",
                    ref this.resultFilter,
                    128);
                if (WindowTheme.ShadowedButton("Clear search", new Vector2(this.ScaleUi(96f), 0)))
                    this.ClearSearch();
                DrawTooltipIfHovered("Clear the current search results and search text.");
                if (!string.IsNullOrWhiteSpace(this.resultFilter))
                {
                    ImGui.SameLine();
                    if (WindowTheme.ShadowedButton("Clear filter", new Vector2(this.ScaleUi(92f), 0)))
                        this.resultFilter = string.Empty;
                    DrawTooltipIfHovered("Clear the result filter only.");
                }

                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo(
                        "##job-filter",
                        string.IsNullOrWhiteSpace(this.selectedJobFilter)
                            ? "All jobs"
                            : this.selectedJobFilter))
                {
                    var showAllJobs = string.IsNullOrWhiteSpace(this.selectedJobFilter);
                    if (ImGui.Selectable("All jobs", showAllJobs))
                        this.selectedJobFilter = string.Empty;

                    foreach (var job in availableJobFilters)
                    {
                        var isSelectedJob = string.Equals(
                            this.selectedJobFilter,
                            job,
                            StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable(job, isSelectedJob))
                            this.selectedJobFilter = job;
                    }

                    ImGui.EndCombo();
                }

                DrawTooltipIfHovered("Filter these results by job.");

                if (!this.showingCraftableRecipes)
                {
                    ImGui.SetNextItemWidth(-1);
                    var selectedTypeLabel = string.IsNullOrWhiteSpace(this.selectedSearchTypeFilter)
                        ? "All types"
                        : this.selectedSearchTypeFilter;
                    if (ImGui.BeginCombo("##type-filter", selectedTypeLabel))
                    {
                        var showAllTypes = string.IsNullOrWhiteSpace(this.selectedSearchTypeFilter);
                        if (ImGui.Selectable("All types", showAllTypes))
                            this.selectedSearchTypeFilter = string.Empty;

                        var showGatherables = string.Equals(this.selectedSearchTypeFilter, "Gatherables", StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable("Gatherables", showGatherables))
                        {
                            this.selectedSearchTypeFilter = "Gatherables";
                            this.selectedScripFilter = string.Empty;
                        }

                        var showCollectables = string.Equals(this.selectedSearchTypeFilter, "Collectables", StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable("Collectables", showCollectables))
                            this.selectedSearchTypeFilter = "Collectables";

                        ImGui.EndCombo();
                    }

                    DrawTooltipIfHovered("Filter these results by gatherable or collectable type.");

                    ImGui.SetNextItemWidth(-1);
                    var scripFilterDisabled = string.Equals(
                        this.selectedSearchTypeFilter,
                        "Gatherables",
                        StringComparison.OrdinalIgnoreCase);
                    var selectedScripLabel = string.IsNullOrWhiteSpace(this.selectedScripFilter)
                        ? "All scrips"
                        : this.selectedScripFilter;
                    ImGui.BeginDisabled(scripFilterDisabled);
                    if (ImGui.BeginCombo("##scrip-filter", selectedScripLabel))
                    {
                        var showAllScrips = string.IsNullOrWhiteSpace(this.selectedScripFilter);
                        if (ImGui.Selectable("All scrips", showAllScrips))
                            this.selectedScripFilter = string.Empty;

                        var showPurple = string.Equals(this.selectedScripFilter, "Purple", StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable("Purple", showPurple))
                            this.selectedScripFilter = "Purple";

                        var showOrange = string.Equals(this.selectedScripFilter, "Orange", StringComparison.OrdinalIgnoreCase);
                        if (ImGui.Selectable("Orange", showOrange))
                            this.selectedScripFilter = "Orange";

                        ImGui.EndCombo();
                    }
                    ImGui.EndDisabled();

                    DrawTooltipIfHovered(
                        scripFilterDisabled
                            ? "Scrip filtering only applies to collectables."
                            : "Filter collectables by purple or orange scrips.");
                }

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
                        ImGui.TextDisabled("Search for a recipe,");
                        ImGui.TextDisabled("gatherable, or collectible item.");
                    }
                }
                else if (displayedResults.Count == 0)
                {
                    ImGui.TextDisabled("No results match this filter.");
                }

                foreach (var result in displayedResults)
                {
                    ImGui.PushID($"{result.ResultKind}-{result.RecipeId}");
                    var isSelected = this.IsSearchResultSelected(result);
                    var subtitleLabel = isSelected
                        ? this.GetSelectedSearchResultLabel(result)
                        : this.GetUnselectedSearchResultLabel(result);
                    var subtitle = this.showingCraftableRecipes &&
                                   this.craftableAvailability.TryGetValue(
                                       result.RecipeId,
                                       out var availability)
                        ? BuildSearchResultSubtitle(
                            result,
                            $"{availability.CraftCount:N0} crafts | {availability.OutputAmount:N0} items")
                        : BuildSearchResultSubtitle(result, subtitleLabel);

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
                        this.ToggleSearchResultSelected(result, !isSelected);

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

                    var rewardInfo = this.recipeService.GetCollectibleRewardInfo(result.ResultItemId);
                    if (rewardInfo is not null)
                    {
                        this.DrawCollectibleRewardTooltip(
                            result.ResultName,
                            rewardInfo,
                            this.GetSearchResultUnlockTooltipLines(result),
                            1,
                            this.recipeService.GetFishTooltipInfo(result.ResultItemId),
                            this.recipeService.GetCosmicExplorationTooltipInfo(result.ResultItemId),
                            this.recipeService.GetQuestTooltipInfo(result.ResultItemId),
                            result.ResultKind == SearchResultKind.CraftedRecipe
                                ? this.recipeService.GetRecipeLogStatusTooltipInfo(result.RecipeId, result.ResultItemId)
                                : this.recipeService.GetItemLogStatusTooltipInfo(result.ResultItemId));
                    }
                    else
                    {
                        MaterialUsageTooltip.Draw(
                            this.marketboardPriceService,
                            this.configuration,
                            result.ResultItemId,
                            result.ResultName,
                            this.GetSearchResultTooltip(result),
                            detailLines: this.GetSearchResultUnlockTooltipLines(result),
                            specialContentTooltipInfo: this.GetSpecialContentTooltipInfo(result.ResultItemId),
                            fishTooltipInfo: this.recipeService.GetFishTooltipInfo(result.ResultItemId),
                            societyQuestTooltipInfo: this.recipeService.GetSocietyQuestTooltipInfo(result.ResultItemId),
                            cosmicExplorationTooltipInfo: this.recipeService.GetCosmicExplorationTooltipInfo(result.ResultItemId),
                            questTooltipInfo: this.recipeService.GetQuestTooltipInfo(result.ResultItemId),
                            logStatusTooltipInfo: result.ResultKind == SearchResultKind.CraftedRecipe
                                ? this.recipeService.GetRecipeLogStatusTooltipInfo(result.RecipeId, result.ResultItemId)
                                : this.recipeService.GetItemLogStatusTooltipInfo(result.ResultItemId),
                            aetherialReductionSources: this.recipeService.GetAetherialReductionSources(result.ResultItemId),
                            isMarketboardAvailable: this.recipeService.IsMarketboardAvailable(result.ResultItemId));
                    }

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
            WindowTheme.ShadowedButton(
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

    public void ProcessBackgroundState()
    {
        var artisanProgressActive =
            this.pluginIntegrationService.GetCraftAllProgressSnapshot().IsActive;
        if (!this.observedArtisanProgressActive && artisanProgressActive)
            this.OpenArtisanPopup();

        this.observedArtisanProgressActive = artisanProgressActive;

        if (this.observedCraftAllCompletionCount !=
            this.pluginIntegrationService.CraftAllCompletionCount)
        {
            this.observedCraftAllCompletionCount =
                this.pluginIntegrationService.CraftAllCompletionCount;
            this.integrationMessage = string.Empty;
            this.integrationError = false;
        }

        if (this.observedCraftAllStopCount !=
            this.pluginIntegrationService.CraftAllStopCount)
        {
            this.observedCraftAllStopCount =
                this.pluginIntegrationService.CraftAllStopCount;
            this.integrationMessage = "Stopped Artisan after the current craft.";
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

        if (!this.inventoryRefreshRequested)
            return;

        this.inventoryRefreshRequested = false;
        this.RefreshDetails(true);
    }

    private void OpenArtisanPopup()
    {
        this.artisanCraftOverlayWindow.OpenBesideMainWindow(
            this.lastMainWindowPosition,
            this.lastMainWindowSize.X > 0f && this.lastMainWindowSize.Y > 0f
                ? this.lastMainWindowSize
                : this.ScaleMainWindowSize(BaseWindowSize, this.appliedMainWindowScalePercent));
        this.IsOpen = false;
    }

    private void DrawHeader()
    {
        ImGui.TextColored(this.configuration.AccentTextColor, "RECIPE HELPER");
        ImGui.SameLine();
        ImGui.TextDisabled($"Plan | Check | Gather | Craft | v{this.versionText}");
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
        if (WindowTheme.ShadowedButton("Search", new Vector2(searchButtonWidth, 0)) ||
            searchChanged && this.searchText.Length >= 3)
            this.RefreshSearch();
        DrawTooltipIfHovered("Search recipes by crafted item name or item ID.");

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Can craft", new Vector2(craftableButtonWidth, 0)))
            this.RefreshCraftableRecipes(true);
        DrawTooltipIfHovered("Show recipes you can make from your current stored materials.");

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Missing Items Overlay", new Vector2(overlayButtonWidth, 0)))
            this.rawMaterialsOverlayWindow.IsOpen = true;
        DrawTooltipIfHovered("Open the compact overlay for missing gatherable materials.");

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Refresh Inventory", new Vector2(refreshButtonWidth, 0)))
            this.RefreshDetails(true);
        DrawTooltipIfHovered("Rescan inventory, saddlebags, and saved retainer stock.");

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Settings", new Vector2(settingsButtonWidth, 0)))
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
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AdjustColor(buttonColor, 0.18f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AdjustColor(buttonColor, -0.04f));
        ImGui.PushStyleColor(ImGuiCol.Header, AdjustColor(WithAlpha(accent, 0.82f), -0.04f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, AdjustColor(WithAlpha(accent, 0.94f), 0.04f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, AdjustColor(WithAlpha(accent, 0.98f), -0.08f));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, AdjustColor(WithAlpha(accent, 0.90f), -0.06f));
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
        this.selectedJobFilter = string.Empty;
        this.selectedSearchTypeFilter = string.Empty;
        this.selectedScripFilter = string.Empty;
        this.craftableAvailability =
            new Dictionary<uint, CraftableRecipeAvailability>();
        this.searchResults = this.recipeService.Search(this.searchText);
    }

    private void RefreshSearchResultsForVisibleFilters()
    {
        if (this.showingCraftableRecipes)
            return;

        var hasBrowseFilters =
            !string.IsNullOrWhiteSpace(this.selectedJobFilter) ||
            !string.IsNullOrWhiteSpace(this.selectedSearchTypeFilter) ||
            !string.IsNullOrWhiteSpace(this.selectedScripFilter);

        if (!string.IsNullOrWhiteSpace(this.searchText))
            return;

        this.searchResults = hasBrowseFilters
            ? this.recipeService.BrowseAllSearchResults()
            : [];
    }

    private void RefreshCraftableRecipes(bool scanInventory)
    {
        if (scanInventory)
            this.ownedItems = this.inventoryService.GetOwnedItems();

        this.showingCraftableRecipes = true;
        if (scanInventory)
            this.resultFilter = string.Empty;
        if (scanInventory)
            this.selectedJobFilter = string.Empty;
        if (scanInventory)
            this.selectedSearchTypeFilter = string.Empty;
        if (scanInventory)
            this.selectedScripFilter = string.Empty;
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
        if (!recipe.CanAddToPlan)
            return;

        if (this.selectedRecipes.Any(
                selection => selection.Recipe.RecipeId == recipe.RecipeId))
            return;

        this.selectedRecipes.Add(new RecipePlanSelection(
            recipe,
            Math.Max(1, recipe.ResultAmount)));
        this.OpenSelectedRecipesSection();
        this.OpenCollectablesSectionIfRecipeCollectable(recipe);
        this.RefreshDetails(true);
    }

    private void SetRecipeSelected(RecipeMatch recipe, bool selected)
    {
        if (!recipe.CanAddToPlan)
            return;

        if (selected)
        {
            this.AddRecipe(recipe);
            return;
        }

        if (this.selectedRecipes.RemoveAll(
                selection => selection.Recipe.RecipeId == recipe.RecipeId) > 0)
            this.RefreshDetails(false);
    }

    private bool IsSearchResultSelected(RecipeMatch result) =>
        result.ResultKind switch
        {
            SearchResultKind.CraftedRecipe => this.selectedRecipes.Any(
                selection => selection.Recipe.RecipeId == result.RecipeId),
            SearchResultKind.CollectibleItem => this.selectedCollectables.Any(
                selection => selection.Item.ResultItemId == result.ResultItemId),
            SearchResultKind.GatherableItem => this.selectedGatherables.Any(
                selection => selection.Item.ResultItemId == result.ResultItemId),
            _ => false,
        };

    private void ToggleSearchResultSelected(RecipeMatch result, bool selected)
    {
        switch (result.ResultKind)
        {
            case SearchResultKind.CraftedRecipe:
                this.SetRecipeSelected(result, selected);
                break;
            case SearchResultKind.CollectibleItem:
                this.SetSupplementalItemSelected(this.selectedCollectables, result, selected);
                break;
            case SearchResultKind.GatherableItem:
                this.SetSupplementalItemSelected(this.selectedGatherables, result, selected);
                break;
        }
    }

    private void SetSupplementalItemSelected(
        List<SupplementalItemSelection> selections,
        RecipeMatch result,
        bool selected)
    {
        var existingIndex = selections.FindIndex(entry => entry.Item.ResultItemId == result.ResultItemId);
        if (selected)
        {
            if (existingIndex >= 0)
                return;

            selections.Add(new SupplementalItemSelection
            {
                Item = result,
                DesiredAmount = Math.Max(1, result.ResultAmount),
            });
            this.autoOpenSectionIds.Add(
                result.ResultKind == SearchResultKind.CollectibleItem
                    ? "collectables-section"
                    : "gatherables-section");
            this.UpdateOverlayMaterials();
            return;
        }

        if (existingIndex >= 0)
            selections.RemoveAt(existingIndex);
        this.UpdateOverlayMaterials();
    }

    private void OpenSelectedRecipesSection() =>
        this.autoOpenSectionIds.Add("selected-recipes-section");

    private void OpenCollectablesSectionIfRecipeCollectable(RecipeMatch recipe)
    {
        if (this.recipeService.GetCollectibleRewardInfo(recipe.ResultItemId) is not null)
            this.autoOpenSectionIds.Add("collectables-section");
    }

    private void OpenCollectablesSectionForSelectedRecipes()
    {
        if (this.selectedRecipes.Any(
                selection => this.recipeService.GetCollectibleRewardInfo(selection.Recipe.ResultItemId) is not null))
            this.autoOpenSectionIds.Add("collectables-section");
    }

    private void OpenSelectedSupplementalSections()
    {
        if (this.selectedGatherables.Count > 0)
            this.autoOpenSectionIds.Add("gatherables-section");
        if (this.selectedCollectables.Count > 0)
            this.autoOpenSectionIds.Add("collectables-section");
    }

    private string GetSelectedSearchResultLabel(RecipeMatch result) =>
        result.ResultKind switch
        {
            SearchResultKind.CraftedRecipe => "Added to plan",
            SearchResultKind.CollectibleItem => "Added",
            SearchResultKind.GatherableItem => "Added",
            _ => "Selected",
        };

    private string GetUnselectedSearchResultLabel(RecipeMatch result) =>
        result.ResultKind switch
        {
            SearchResultKind.CraftedRecipe => $"Yield {result.ResultAmount}",
            SearchResultKind.CollectibleItem => string.Empty,
            SearchResultKind.GatherableItem => string.Empty,
            _ => string.Empty,
        };

    private string GetSearchResultTooltip(RecipeMatch result) =>
        result.ResultKind switch
        {
            SearchResultKind.CraftedRecipe when !string.IsNullOrWhiteSpace(result.SearchMetadata) =>
                $"Base collectible hand-in value: {result.SearchMetadata}",
            SearchResultKind.CollectibleItem =>
                "Adds this gatherable collectable to the Collectables section below.",
            _ => string.Empty,
        };

    private IReadOnlyList<string> GetSearchResultUnlockTooltipLines(RecipeMatch result) =>
        result.ResultKind switch
        {
            SearchResultKind.CraftedRecipe => this.BuildUnlockTooltipLines(
                masterRecipeBookInfo: this.recipeService.GetMasterRecipeBookInfo(result.RecipeId)),
            SearchResultKind.CollectibleItem or SearchResultKind.GatherableItem => this.BuildUnlockTooltipLines(
                folkloreBookInfo: this.recipeService.GetFolkloreBookInfo(result.ResultItemId),
                requiredItemInfo: this.recipeService.GetRequiredItemInfo(result.ResultItemId)),
            _ => [],
        };

    private IReadOnlyList<string> GetItemUnlockTooltipLines(uint itemId) =>
        this.BuildUnlockTooltipLines(
            folkloreBookInfo: this.recipeService.GetFolkloreBookInfo(itemId),
            requiredItemInfo: this.recipeService.GetRequiredItemInfo(itemId));

    private IReadOnlyList<string> GetRecipeUnlockTooltipLines(uint recipeId) =>
        this.BuildUnlockTooltipLines(
            masterRecipeBookInfo: this.recipeService.GetMasterRecipeBookInfo(recipeId));

    private SpecialContentTooltipInfo? GetSpecialContentTooltipInfo(uint itemId) =>
        this.recipeService.GetSpecialContentTooltipInfo(itemId);

    private IReadOnlyList<string> BuildUnlockTooltipLines(
        FolkloreBookInfo? folkloreBookInfo = null,
        MasterRecipeBookInfo? masterRecipeBookInfo = null,
        RequiredItemInfo? requiredItemInfo = null)
    {
        var lines = new List<string>();
        if (masterRecipeBookInfo is not null)
        {
            lines.Add("Master recipe unlock");
            lines.Add($"Book: {masterRecipeBookInfo.BookName}");
        }

        if (folkloreBookInfo is not null)
        {
            lines.Add("Folklore unlock");
            lines.Add($"Book: {folkloreBookInfo.BookName}");
            lines.Add($"Sold by: {folkloreBookInfo.ExchangeName}");
            lines.Add($"Cost: {folkloreBookInfo.CostLabel}");
        }

        if (requiredItemInfo is not null)
        {
            lines.Add(requiredItemInfo.IsTool
                ? "Requires tool"
                : "Requires item");
            lines.Add(requiredItemInfo.ItemName);
        }

        return lines;
    }

    private string GetSearchResultJobLabel(RecipeMatch result) =>
        result.ResultKind switch
        {
            SearchResultKind.CraftedRecipe =>
                this.recipeService.GetRecipeJobDisplayLabel(
                    result.RecipeId,
                    result.JobAbbreviations),
            SearchResultKind.CollectibleItem or SearchResultKind.GatherableItem =>
                this.recipeService.GetGatheringJobDisplayLabel(
                    result.ResultItemId,
                    result.JobAbbreviations),
            _ => result.JobAbbreviations,
        };

    private void ClearSearch()
    {
        this.searchText = string.Empty;
        this.resultFilter = string.Empty;
        this.selectedJobFilter = string.Empty;
        this.selectedSearchTypeFilter = string.Empty;
        this.selectedScripFilter = string.Empty;
        this.searchResults = [];
        this.showingCraftableRecipes = false;
        this.craftableAvailability =
            new Dictionary<uint, CraftableRecipeAvailability>();
    }

    private IReadOnlyList<string> GetAvailableJobFilters() =>
        this.showingCraftableRecipes
            ? this.searchResults
                .SelectMany(result => SplitJobAbbreviations(result.JobAbbreviations))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static job => job)
                .ToList()
            : this.selectedSearchTypeFilter.Equals("Gatherables", StringComparison.OrdinalIgnoreCase)
                ? GathererJobFilters
                : DefaultJobFilters;

    private bool MatchesJobFilter(RecipeMatch result) =>
        string.IsNullOrWhiteSpace(this.selectedJobFilter) ||
        SplitJobAbbreviations(result.JobAbbreviations)
            .Any(job => string.Equals(job, this.selectedJobFilter, StringComparison.OrdinalIgnoreCase));

    private bool MatchesSearchTypeFilter(RecipeMatch result) =>
        this.selectedSearchTypeFilter switch
        {
            "Gatherables" => result.ResultKind == SearchResultKind.GatherableItem,
            "Collectables" => result.ResultKind == SearchResultKind.CollectibleItem ||
                              this.IsCollectableRecipeResult(result),
            _ => true,
        };

    private bool MatchesScripFilter(RecipeMatch result) =>
        string.Equals(this.selectedSearchTypeFilter, "Gatherables", StringComparison.OrdinalIgnoreCase)
            ? true
            : this.selectedScripFilter switch
        {
            "Purple" => this.IsCollectableRecipeResult(result) &&
                        result.SearchMetadata.Contains("Purple", StringComparison.OrdinalIgnoreCase),
            "Orange" => this.IsCollectableRecipeResult(result) &&
                        result.SearchMetadata.Contains("Orange", StringComparison.OrdinalIgnoreCase),
            _ => true,
        };

    private bool IsCollectableRecipeResult(RecipeMatch result) =>
        !string.IsNullOrWhiteSpace(result.SearchMetadata) &&
        (result.ResultKind == SearchResultKind.CollectibleItem ||
         result.ResultKind == SearchResultKind.CraftedRecipe);

    private static IReadOnlyList<string> SplitJobAbbreviations(string jobAbbreviations) =>
        string.IsNullOrWhiteSpace(jobAbbreviations)
            ? []
            : jobAbbreviations
                .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private string BuildSearchResultSubtitle(RecipeMatch result, string label)
    {
        var parts = new List<string>();
        var jobLabel = this.GetSearchResultJobLabel(result);
        if (!string.IsNullOrWhiteSpace(jobLabel))
            parts.Add(jobLabel);
        if (!string.IsNullOrWhiteSpace(result.SearchMetadata))
            parts.Add(result.SearchMetadata);
        if (!string.IsNullOrWhiteSpace(label))
            parts.Add(label);
        return string.Join(" | ", parts);
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
            this.canCraftAllFromLiveInventory = false;
            this.UpdateOverlayMaterials();
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

            this.canCraftAllFromLiveInventory =
                this.recipeService.TryBuildArtisanCraftQueue(
                    details.Recipes,
                    this.inventoryService.GetImmediatelyUsableItems(),
                    out _,
                    out _);

            this.rawMaterialsOverlayWindow.SetMaterials(
                this.BuildOverlaySourceNames(details),
                this.BuildOverlayMaterials(details));
        }
        else
        {
            this.canCraftAllFromLiveInventory = false;
            this.UpdateOverlayMaterials();
        }
    }

    private void UpdateOverlayMaterials()
    {
        var details = this.recipePlanDetails;
        this.rawMaterialsOverlayWindow.SetMaterials(
            this.BuildOverlaySourceNames(details),
            this.BuildOverlayMaterials(details));
    }

    private IReadOnlyList<string> BuildOverlaySourceNames(RecipePlanDetails? details)
    {
        var names = new List<string>();
        if (details is not null)
            names.AddRange(details.Recipes.Select(recipe => recipe.ResultName));

        names.AddRange(this.selectedGatherables.Select(selection => $"{selection.Item.ResultName} (Gatherable)"));
        names.AddRange(this.selectedCollectables.Select(selection => $"{selection.Item.ResultName} (Collectable)"));
        if (this.selectedDirectIngredients.Count > 0)
            names.Add("Direct Ingredients");
        if (this.selectedRawMaterials.Count > 0)
            names.Add("Raw Materials");
        return names;
    }

    private IReadOnlyList<IngredientNeed> BuildOverlayMaterials(RecipePlanDetails? details)
    {
        var combinedAmounts = new Dictionary<uint, uint>();

        if (details is not null)
        {
            foreach (var material in details.RawMaterials)
                AddOverlayAmount(combinedAmounts, material.ItemId, material.Required);
        }

        foreach (var selection in this.selectedGatherables)
            AddOverlayAmount(combinedAmounts, selection.Item.ResultItemId, selection.DesiredAmount);
        foreach (var selection in this.selectedCollectables)
            AddOverlayAmount(combinedAmounts, selection.Item.ResultItemId, selection.DesiredAmount);
        foreach (var ingredient in this.selectedDirectIngredients)
            AddOverlayAmount(combinedAmounts, ingredient.ResultItemId, ingredient.DesiredAmount);
        foreach (var ingredient in this.selectedRawMaterials)
            AddOverlayAmount(combinedAmounts, ingredient.ResultItemId, ingredient.DesiredAmount);

        return combinedAmounts
            .OrderBy(entry => entry.Key)
            .Select(entry => this.recipeService.GetStandaloneIngredientNeed(
                entry.Key,
                entry.Value,
                this.ownedItems))
            .Where(need => need is not null)
            .Select(need => need!)
            .ToList();
    }

    private IReadOnlyList<IngredientNeed> GetStandaloneIngredientNeeds(
        IEnumerable<SavedSupplementalPlanEntry> ingredients) =>
        ingredients
            .Where(ingredient => ingredient.ResultItemId != 0 && ingredient.DesiredAmount > 0)
            .GroupBy(ingredient => ingredient.ResultItemId)
            .Select(group => this.recipeService.GetStandaloneIngredientNeed(
                group.Key,
                (uint)Math.Min(
                    group.Aggregate(0UL, (total, ingredient) => total + ingredient.DesiredAmount),
                    uint.MaxValue),
                this.ownedItems))
            .Where(ingredient => ingredient is not null)
            .Select(ingredient => ingredient!)
            .ToList();

    private IReadOnlyList<IngredientNeed> GetDirectIngredientPlanNeeds(RecipePlanDetails? details) =>
        this.CombineIngredientNeeds(
            (details?.Ingredients ?? [])
            .Concat(this.GetStandaloneIngredientNeeds(this.selectedDirectIngredients)));

    private IReadOnlyList<IngredientNeed> GetRawMaterialPlanNeeds(RecipePlanDetails? details) =>
        this.CombineIngredientNeeds(
            (details?.RawMaterials ?? [])
            .Concat(this.GetStandaloneIngredientNeeds(this.selectedRawMaterials)));

    private IReadOnlyList<IngredientNeed> CombineIngredientNeeds(IEnumerable<IngredientNeed> ingredients) =>
        ingredients
            .GroupBy(ingredient => ingredient.ItemId)
            .Select(group => this.recipeService.GetStandaloneIngredientNeed(
                group.Key,
                (uint)Math.Min(
                    group.Aggregate(0UL, (total, ingredient) => total + ingredient.Required),
                    uint.MaxValue),
                this.ownedItems))
            .Where(ingredient => ingredient is not null)
            .Select(ingredient => ingredient!)
            .OrderBy(ingredient => ingredient.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static void AddOverlayAmount(IDictionary<uint, uint> amounts, uint itemId, uint amount)
    {
        if (itemId == 0 || amount == 0)
            return;

        amounts.TryGetValue(itemId, out var existing);
        amounts[itemId] = (uint)Math.Min((ulong)existing + amount, uint.MaxValue);
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
                    "saved-plans-section",
                    defaultOpen: false))
                this.DrawSavedPlans();
            this.DrawStandaloneIngredientPlanSections();
            this.DrawSupplementalGatheringSections();
            this.DrawTravelPopup();
            return;
        }

        var details = this.recipePlanDetails;
        if (this.configuration.SavedRecipePlans.Count > 0 &&
            this.DrawCollapsibleSection(
                "SAVED PLANS",
                "Select, combine, craft, or manage named recipe plans",
                "saved-plans-section",
                defaultOpen: false))
            this.DrawSavedPlans();

        var canShowCraftAll =
            this.artisanCraftQueue.Count > 0 &&
            (details.Recipes.Count > 1 ||
             this.artisanCraftQueue.Any(recipe => recipe.IsIntermediate));
        var craftAllTooltip = this.canCraftAllFromLiveInventory
            ? "Crafts all pre-crafts and recipes with Artisan."
            : "Only activates when all items are already in inventory. Use Refresh Inventory if needed.";
        var hasDreamFeature = this.gwenDreamService.IsAutoRetainerAvailable;
        var canUseDreamWithdrawals = hasDreamFeature && this.gwenDreamService.CanUseForSelection(this.recipePlanDetails);
        var canUseDream = hasDreamFeature && (canUseDreamWithdrawals || canShowCraftAll);
        var dreamTooltip = canUseDream
            ? "Will automatically withdraw all materials from retainers and craft all pre-crafts and recipes - ultimate laziness!"
            : "Only activates when all required raw materials and/or pre-crafts are available.";

        if (this.DrawCollapsibleSection(
                "SELECTED RECIPES",
                "Combined totals, quantities, and recipe actions",
                "selected-recipes-section",
                this.selectedRecipes.Count > 0 ? "Clear selected" : null,
                this.selectedRecipes.Count > 0
                    ? this.ClearSelectedPlansAndCurrentPlan
                    : null))
        {
            this.DrawSavePlanControls(PlanSaveScope.Recipe, "selected-recipes");
            ImGui.Spacing();
            this.DrawSelectedRecipes(details);
            ImGui.Spacing();
            if (details.Recipes.Count > 0 || !string.IsNullOrWhiteSpace(this.integrationMessage))
            {
                var craftAllClicked = false;
                if (canShowCraftAll)
                {
                    ImGui.BeginDisabled(!this.canCraftAllFromLiveInventory);
                    craftAllClicked = WindowTheme.ShadowedButton("Craft all with Artisan", new Vector2(this.ScaleUi(160f), 0));
                    ImGui.EndDisabled();
                }
                if (craftAllClicked)
                {
                    this.integrationError =
                        !this.pluginIntegrationService.CraftAllWithArtisan(
                            this.artisanCraftQueue,
                            0,
                            false,
                            out this.integrationMessage);
                }
                if (canShowCraftAll)
                    DrawTooltipIfHovered(craftAllTooltip, allowWhenDisabled: true);

                if (hasDreamFeature)
                {
                    if (canShowCraftAll)
                        ImGui.SameLine();
                }

                if (hasDreamFeature)
                    ImGui.SameLine();

                if (hasDreamFeature)
                {
                    ImGui.BeginDisabled(!canUseDream);
                    if (WindowTheme.ShadowedButton("Gwen's Dream", new Vector2(this.ScaleUi(140f), 0)))
                    {
                        if (canUseDreamWithdrawals)
                        {
                            if (this.gwenDreamService.TryStart(this.recipePlanDetails))
                                this.dreamCraftPending = true;
                        }
                        else
                        {
                            this.TryStartDreamCraftAll();
                        }
                    }
                    ImGui.EndDisabled();
                    DrawTooltipIfHovered(dreamTooltip, allowWhenDisabled: true);

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
            this.DrawSupplementalGatheringSections();
            this.DrawTravelPopup();
            return;
        }

        var directIngredients = this.GetDirectIngredientPlanNeeds(details)
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        var allRawMaterials = this.GetRawMaterialPlanNeeds(details)
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        var allElementalCatalysts = details.RawMaterials
            .Where(IsElementalCatalyst)
            .ToList();
        var obtainedRawCount = allRawMaterials.Count(material => material.HasEnough);
        var obtainedCatalystCount = allElementalCatalysts.Count(material => material.HasEnough);
        var rawMaterials = allRawMaterials
            .Where(material =>
                this.configuration.ShowObtainedRawMaterials ||
                !material.HasEnough)
            .ToList();
        var elementalCatalysts = allElementalCatalysts
            .Where(material =>
                this.configuration.ShowObtainedElementalCatalysts ||
                !material.HasEnough)
            .ToList();

        if (directIngredients.Count > 0 &&
            this.DrawCollapsibleSection(
                "DIRECT INGREDIENTS",
                "Combined items used by the selected recipes",
                "direct-ingredients-section"))
        {
            this.DrawSavePlanControls(PlanSaveScope.DirectIngredient, "direct-ingredients");
            ImGui.Spacing();
            this.DrawMaterialsTable("ingredients", directIngredients);
        }

        if (allRawMaterials.Count > 0 &&
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
        {
            this.DrawSavePlanControls(PlanSaveScope.RawMaterial, "raw-materials");
            ImGui.Spacing();
            if (rawMaterials.Count > 0)
                this.DrawMaterialsTable("raw-materials", rawMaterials);
            else
                ImGui.TextDisabled("All obtained raw materials are currently hidden.");
        }

        if (allElementalCatalysts.Count > 0 &&
            this.DrawCollapsibleSection(
                "SHARDS, CRYSTALS & CLUSTERS",
                "Combined elemental catalyst requirements",
                "elemental-catalysts-section",
                obtainedCatalystCount > 0
                    ? this.configuration.ShowObtainedElementalCatalysts
                        ? $"Hide obtained ({allElementalCatalysts.Count(material => material.HasEnough)})"
                        : $"Show obtained ({allElementalCatalysts.Count(material => material.HasEnough)})"
                    : null,
                obtainedCatalystCount > 0
                    ? ToggleObtainedElementalCatalysts
                    : null))
        {
            if (elementalCatalysts.Count > 0)
                this.DrawMaterialsTable("elemental-catalysts", elementalCatalysts);
            else
                ImGui.TextDisabled("All obtained shards, crystals, and clusters are currently hidden.");
        }

        this.DrawSupplementalGatheringSections();
        this.DrawTravelPopup();
    }

    private void DrawSupplementalGatheringSections()
    {
        var gatherableRows = this.BuildSupplementalRows(this.selectedGatherables);
        if (this.DrawCollapsibleSection(
                "GATHERABLES",
                "Standalone gathering targets you want to collect",
                "gatherables-section",
                actionLabel: this.selectedGatherables.Count > 0 ? "Clear all" : null,
                action: this.selectedGatherables.Count > 0
                    ? () =>
                    {
                        this.selectedGatherables.Clear();
                        this.UpdateOverlayMaterials();
                    }
                    : null,
                defaultOpen: false))
        {
            if (gatherableRows.Count == 0)
            {
                ImGui.TextDisabled("Search for a gatherable item above.");
                ImGui.TextDisabled("Click a result to add it here.");
            }
            else
            {
                this.DrawSavePlanControls(PlanSaveScope.Gatherable);
                ImGui.Spacing();
                this.DrawSupplementalMaterialsTable(
                    "gatherables",
                    "Gatherables",
                    this.selectedGatherables,
                    gatherableRows,
                    showStock: true,
                    showFoundIn: true,
                    showHandInValue: false);
            }
        }

        var collectableRows = this.BuildCollectableRows();
        if (this.DrawCollapsibleSection(
                "COLLECTABLES",
                "Recipe collectables and gatherable collectables with their hand-in value",
                "collectables-section",
                actionLabel: this.selectedCollectables.Count > 0 ? "Clear added" : null,
                action: this.selectedCollectables.Count > 0
                    ? () =>
                    {
                        this.selectedCollectables.Clear();
                        this.UpdateOverlayMaterials();
                    }
                    : null,
                defaultOpen: false))
        {
            if (collectableRows.Count == 0)
            {
                ImGui.TextDisabled("Search for a gatherable collectable above,");
                ImGui.TextDisabled("or add a collectable recipe to the plan.");
            }
            else
            {
                this.DrawSavePlanControls(PlanSaveScope.Collectable);
                ImGui.Spacing();
                this.DrawSupplementalMaterialsTable(
                    "collectables",
                    "Collectable",
                    this.selectedCollectables,
                    collectableRows,
                    showStock: false,
                    showFoundIn: false,
                    showHandInValue: true);
            }
        }
    }

    private void DrawStandaloneIngredientPlanSections()
    {
        var directIngredients = this.GetDirectIngredientPlanNeeds(null)
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        if (directIngredients.Count > 0 &&
            this.DrawCollapsibleSection(
                "DIRECT INGREDIENTS",
                "Standalone direct ingredient targets",
                "direct-ingredients-section",
                "Clear all",
                () =>
                {
                    this.selectedDirectIngredients.Clear();
                    this.UpdateOverlayMaterials();
                },
                defaultOpen: false))
        {
            this.DrawSavePlanControls(PlanSaveScope.DirectIngredient, "standalone-direct-ingredients");
            ImGui.Spacing();
            this.DrawMaterialsTable("standalone-direct-ingredients", directIngredients);
        }

        var rawMaterials = this.GetRawMaterialPlanNeeds(null)
            .Where(material => !IsElementalCatalyst(material))
            .ToList();
        if (rawMaterials.Count > 0 &&
            this.DrawCollapsibleSection(
                "RAW MATERIALS",
                "Standalone raw material targets",
                "raw-materials-section",
                "Clear all",
                () =>
                {
                    this.selectedRawMaterials.Clear();
                    this.UpdateOverlayMaterials();
                },
                defaultOpen: false))
        {
            this.DrawSavePlanControls(PlanSaveScope.RawMaterial, "standalone-raw-materials");
            ImGui.Spacing();
            this.DrawMaterialsTable("standalone-raw-materials", rawMaterials);
        }
    }

    private IReadOnlyList<SupplementalItemRow> BuildSupplementalRows(
        IEnumerable<SupplementalItemSelection> selections) =>
        this.OrderSupplementalRowsForDisplay(
            selections
                .Select(selection =>
                {
                    var need = this.recipeService.GetStandaloneIngredientNeed(
                        selection.Item.ResultItemId,
                        selection.DesiredAmount,
                        this.ownedItems);
                    return need is null
                        ? null
                        : new SupplementalItemRow(
                            selection.Item,
                            need,
                            this.recipeService.GetCollectibleRewardInfo(selection.Item.ResultItemId),
                            this.BuildSupplementalItemSubtitle(selection.Item, need.Source),
                            selection.DesiredAmount,
                            true,
                            false);
                })
                .Where(row => row is not null)
                .Select(row => row!));

    private IReadOnlyList<SupplementalItemRow> BuildCollectableRows()
    {
        var standaloneRows = this.BuildSupplementalRows(this.selectedCollectables);
        var recipeRows = this.recipePlanDetails is null
            ? []
            : this.BuildRecipeCollectableRows(this.recipePlanDetails);
        return this.OrderSupplementalRowsForDisplay(standaloneRows.Concat(recipeRows));
    }

    private IReadOnlyList<SupplementalItemRow> BuildRecipeCollectableRows(RecipePlanDetails details) =>
        details.Recipes
            .Select(recipe =>
            {
                var rewardInfo = this.recipeService.GetCollectibleRewardInfo(recipe.ResultItemId);
                if (rewardInfo is null)
                    return null;

                var need = this.recipeService.GetStandaloneIngredientNeed(
                    recipe.ResultItemId,
                    recipe.DesiredAmount,
                    this.ownedItems);
                if (need is null)
                    return null;

                var sourceRecipe = this.selectedRecipes.FirstOrDefault(
                    selection => selection.Recipe.RecipeId == recipe.RecipeId)?.Recipe;
                var item = sourceRecipe ?? new RecipeMatch(
                    recipe.RecipeId,
                    recipe.ResultItemId,
                    recipe.ResultName,
                    recipe.ResultAmount,
                    string.Empty);
                return new SupplementalItemRow(
                    item,
                    need,
                    rewardInfo,
                    BuildRecipeCollectableSubtitle(item.JobAbbreviations),
                    recipe.DesiredAmount,
                    false,
                    true);
            })
            .Where(row => row is not null)
            .Select(row => row!)
            .ToList();

    private IReadOnlyList<SupplementalItemRow> OrderSupplementalRowsForDisplay(
        IEnumerable<SupplementalItemRow> rows) =>
        rows
            .Select(row =>
            {
                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(row.Need.ReductionSources);
                var availabilityText = this.GetIngredientAvailabilityText(row.Need, reductionSource);
                var canGather = this.CanGatherIngredient(row.Need, reductionSource, availabilityText);
                return new
                {
                    Row = row,
                    RecipeSort = row.IsRecipeCollectable ? 0 : 1,
                    SortCategory = GetIngredientSortCategory(canGather, availabilityText, IsVendorSource(row.Need)),
                    WaitSeconds = GetGenericAvailabilityWaitSeconds(availabilityText),
                };
            })
            .OrderBy(entry => entry.RecipeSort)
            .ThenBy(entry => entry.SortCategory)
            .ThenBy(entry => entry.WaitSeconds)
            .ThenBy(entry => entry.Row.Need.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Row)
            .ToList();

    private void DrawSupplementalMaterialsTable(
        string tableId,
        string itemHeader,
        List<SupplementalItemSelection> selections,
        IReadOnlyList<SupplementalItemRow> rows,
        bool showStock,
        bool showFoundIn,
        bool showHandInValue)
    {
        if (rows.Count == 0)
            return;

        var columnCount =
            5 +
            (showStock ? 1 : 0) +
            (showFoundIn ? 1 : 0) +
            (showHandInValue ? 1 : 0);
        var tableFlags =
            ImGuiTableFlags.PadOuterX |
            ImGuiTableFlags.Resizable |
            ImGuiTableFlags.SizingFixedFit;
        var requestedTableWidth =
            this.ScaleUi(240f) +
            this.ScaleUi(60f) +
            this.ScaleUi(75f) +
            this.ScaleUi(68f) +
            this.ScaleUi(115f) +
            (showStock ? this.ScaleUi(88f) : 0f) +
            (showFoundIn ? this.ScaleUi(210f) : 0f) +
            (showHandInValue ? this.ScaleUi(160f) : 0f) +
            (ImGui.GetStyle().CellPadding.X * 2 * columnCount) +
            2f;
        var availableTableWidth = ImGui.GetContentRegionAvail().X;
        var needsHorizontalScroll = requestedTableWidth > availableTableWidth;
        if (needsHorizontalScroll)
            tableFlags |= ImGuiTableFlags.ScrollX;

        void SetupColumn(string label, float width) =>
            ImGui.TableSetupColumn(
                label,
                ImGuiTableColumnFlags.WidthFixed,
                width);

        var tableHeight =
            (this.ScaleUi(44f) * rows.Count) +
            this.ScaleUi(36f) +
            (needsHorizontalScroll ? ImGui.GetStyle().ScrollbarSize : 0f) +
            2f;
        var removeItemIds = new HashSet<uint>();
        var updatedOverlay = false;
        if (ImGui.BeginTable(
                tableId,
                columnCount,
                tableFlags,
                new Vector2(Math.Max(1f, availableTableWidth), tableHeight)))
        {
            SetupColumn(itemHeader, this.ScaleUi(240f));
            SetupColumn("Qty", this.ScaleUi(60f));
            SetupColumn("Missing", this.ScaleUi(75f));
            SetupColumn("Travel", this.ScaleUi(68f));
            SetupColumn("Available", this.ScaleUi(115f));
            if (showStock)
                SetupColumn("Stock", this.ScaleUi(88f));
            if (showFoundIn)
                SetupColumn("Found In", this.ScaleUi(210f));
            if (showHandInValue)
                SetupColumn("Base Hand-In Value", this.ScaleUi(160f));

            ImGui.TableNextRow(ImGuiTableRowFlags.None, this.ScaleUi(36f));
            ImGui.TableNextColumn();
            this.DrawHeaderCard(itemHeader);
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Qty");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Missing");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Travel");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Available");
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
            if (showHandInValue)
            {
                ImGui.TableNextColumn();
                this.DrawHeaderCard("Base Hand-In Value");
            }

            foreach (var row in rows)
            {
                var ingredient = row.Need;
                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(ingredient.ReductionSources);
                var availabilityText = this.GetIngredientAvailabilityText(ingredient, reductionSource);
                var rowColor = ingredient.HasEnough
                    ? this.configuration.EnoughRowColor
                    : WithAlpha(this.configuration.MissingTextColor, 0.18f);

                ImGui.TableNextRow(ImGuiTableRowFlags.None, this.ScaleUi(44f));
                ImGui.TableNextColumn();
                this.DrawInfoCard(
                    $"{tableId}-item-{ingredient.ItemId}",
                    new Vector2(-1, this.ScaleUi(36f)),
                    ingredient.Name,
                    row.Subtitle,
                    rowColor,
                    this.configuration.TextColor);
                if (showHandInValue && row.RewardInfo is { } itemRewardInfo)
                    this.DrawCollectibleRewardTooltip(
                        ingredient.Name,
                        itemRewardInfo,
                        this.GetItemUnlockTooltipLines(ingredient.ItemId),
                        row.DisplayQuantity,
                        this.recipeService.GetFishTooltipInfo(ingredient.ItemId),
                        this.recipeService.GetCosmicExplorationTooltipInfo(ingredient.ItemId),
                        this.recipeService.GetQuestTooltipInfo(ingredient.ItemId),
                        this.recipeService.GetItemLogStatusTooltipInfo(ingredient.ItemId));
                else
                    MaterialUsageTooltip.Draw(
                        this.marketboardPriceService,
                        this.configuration,
                        ingredient.ItemId,
                        ingredient.Name,
                        detailLines: this.GetItemUnlockTooltipLines(ingredient.ItemId),
                        specialContentTooltipInfo: this.GetSpecialContentTooltipInfo(ingredient.ItemId),
                        fishTooltipInfo: this.recipeService.GetFishTooltipInfo(ingredient.ItemId),
                        societyQuestTooltipInfo: this.recipeService.GetSocietyQuestTooltipInfo(ingredient.ItemId),
                        cosmicExplorationTooltipInfo: this.recipeService.GetCosmicExplorationTooltipInfo(ingredient.ItemId),
                        questTooltipInfo: this.recipeService.GetQuestTooltipInfo(ingredient.ItemId),
                        logStatusTooltipInfo: this.recipeService.GetItemLogStatusTooltipInfo(ingredient.ItemId),
                        aetherialReductionSources: this.recipeService.GetAetherialReductionSources(ingredient.ItemId),
                        isMarketboardAvailable: this.recipeService.IsMarketboardAvailable(ingredient.ItemId));

                ImGui.TableNextColumn();
                var quantityCursor = ImGui.GetCursorPos();
                if (row.IsEditable)
                {
                    this.DrawDecorativeCardBackground(
                        new Vector2(-1, this.ScaleUi(36f)),
                        this.configuration.InputCardColor);
                    var inputHeight = ImGui.GetFrameHeight();
                    ImGui.SetCursorPos(quantityCursor + new Vector2(
                        this.ScaleUi(10f),
                        MathF.Max(0f, (this.ScaleUi(36f) - inputHeight) / 2f)));
                    ImGui.SetNextItemWidth(-1);
                    ImGui.PushStyleColor(ImGuiCol.FrameBg, Vector4.Zero);
                    ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Vector4.Zero);
                    ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Vector4.Zero);
                    var desiredAmount = (int)row.DisplayQuantity;
                    if (ImGui.InputInt($"##{tableId}-amount-{ingredient.ItemId}", ref desiredAmount))
                    {
                        var clampedAmount = Math.Clamp(desiredAmount, 0, 9999);
                        if (clampedAmount == 0)
                        {
                            removeItemIds.Add(ingredient.ItemId);
                        }
                        else
                        {
                            var selection = selections.FirstOrDefault(
                                entry => entry.Item.ResultItemId == ingredient.ItemId);
                            if (selection is not null)
                                selection.DesiredAmount = (uint)clampedAmount;
                        }

                        updatedOverlay = true;
                    }

                    ImGui.PopStyleColor(3);
                    DrawTooltipIfHovered("Set to 0 to remove this entry.");
                }
                else
                {
                    this.DrawValueCard(
                        $"{tableId}-amount-{ingredient.ItemId}",
                        new Vector2(-1, this.ScaleUi(36f)),
                        row.DisplayQuantity.ToString(),
                        this.configuration.InputCardColor,
                        this.configuration.TextColor);
                    DrawTooltipIfHovered("Quantity comes from the selected recipe plan.");
                }

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
                else
                {
                    this.DrawValueCard(
                        $"{tableId}-missing-{ingredient.ItemId}",
                        new Vector2(-1, this.ScaleUi(36f)),
                        ingredient.Missing.ToString(),
                        WithAlpha(this.configuration.MissingTextColor, 0.18f),
                        this.configuration.MissingTextColor);
                }

                ImGui.TableNextColumn();
                if (row.IsRecipeCollectable)
                {
                    this.DrawValueCard(
                        $"{tableId}-travel-{ingredient.ItemId}",
                        new Vector2(-1, this.ScaleUi(36f)),
                        "-",
                        AdjustColor(this.configuration.WindowBackgroundColor, 0.05f),
                        AdjustColor(this.configuration.TextColor, -0.30f));
                }
                else if (this.CanGatherIngredient(ingredient, reductionSource))
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
                    if (WindowTheme.ShadowedButton($"Gather##{tableId}-{ingredient.ItemId}", new Vector2(buttonWidth, 0)))
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

                ImGui.TableNextColumn();
                if (row.IsRecipeCollectable)
                {
                    this.DrawValueCard(
                        $"{tableId}-available-{ingredient.ItemId}",
                        new Vector2(-1, this.ScaleUi(36f)),
                        "Crafted",
                        WithAlpha(this.configuration.AccentColor, 0.10f),
                        this.configuration.TextColor);
                }
                else
                {
                    this.DrawAvailabilityCard(tableId, ingredient, availabilityText);
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
                        this.DrawValueCard(
                            $"{tableId}-found-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            string.Join(", ", ingredient.Locations),
                            AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                            this.configuration.TextColor);
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

                if (showHandInValue)
                {
                    ImGui.TableNextColumn();
                    if (row.RewardInfo is { } rewardInfo)
                    {
                        this.DrawValueCard(
                            $"{tableId}-handin-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            rewardInfo.FormatTotal(row.DisplayQuantity),
                            WithAlpha(this.configuration.AccentColor, 0.10f),
                            this.configuration.TextColor);
                        DrawTooltipIfHovered(rewardInfo.GetTotalTooltipText(row.DisplayQuantity));
                    }
                    else
                    {
                        this.DrawValueCard(
                            $"{tableId}-handin-{ingredient.ItemId}",
                            new Vector2(-1, this.ScaleUi(36f)),
                            "-",
                            AdjustColor(this.configuration.WindowBackgroundColor, 0.05f),
                            AdjustColor(this.configuration.TextColor, -0.30f));
                    }
                }
            }

            ImGui.EndTable();
        }

        if (removeItemIds.Count > 0)
        {
            selections.RemoveAll(selection => removeItemIds.Contains(selection.Item.ResultItemId));
            updatedOverlay = true;
        }

        if (updatedOverlay)
            this.UpdateOverlayMaterials();
    }

    private string BuildSupplementalItemSubtitle(RecipeMatch item, string source)
    {
        var parts = new List<string>();
        var jobLabel = this.GetSearchResultJobLabel(item);
        if (!string.IsNullOrWhiteSpace(jobLabel))
            parts.Add(jobLabel);
        if (item.ResultKind == SearchResultKind.CollectibleItem)
            source = RemoveSourceLabel(source, "Vendor");
        if (!string.IsNullOrWhiteSpace(source))
            parts.Add(source);
        return parts.Count == 0
            ? string.Empty
            : string.Join(" | ", parts);
    }

    private static string BuildRecipeCollectableSubtitle(string jobAbbreviations)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(jobAbbreviations))
            parts.Add(jobAbbreviations);
        parts.Add("Recipe plan");
        return string.Join(" | ", parts);
    }

    private static string RemoveSourceLabel(string source, string label) =>
        string.Join(
            ", ",
            source
                .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(part => !string.Equals(part, label, StringComparison.OrdinalIgnoreCase)));

    private void DrawCollectibleRewardTooltip(
        string itemName,
        CollectibleRewardInfo rewardInfo,
        IReadOnlyList<string>? unlockTooltipLines,
        uint quantity,
        FishTooltipInfo? fishTooltipInfo = null,
        CosmicExplorationTooltipInfo? cosmicExplorationTooltipInfo = null,
        QuestTooltipInfo? questTooltipInfo = null,
        LogStatusTooltipInfo? logStatusTooltipInfo = null,
        bool showTotalValue = false)
    {
        if (!ImGui.IsItemHovered())
            return;

        MaterialUsageTooltip.BeginStyledTooltip(this.configuration);
        ImGui.TextColored(this.configuration.AccentTextColor, itemName);
        ImGui.Separator();
        var rewardText = rewardInfo.GetTooltipText();
        if (showTotalValue && quantity > 1)
        {
            ImGui.TextDisabled($"Scaled for quantity: {quantity:N0}");
            ImGui.Spacing();
            rewardText = rewardInfo.GetTotalTooltipText(quantity);
        }
        foreach (var rewardLine in rewardText.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            MaterialUsageTooltip.DrawDetailLine(this.configuration, rewardLine);

        if (unlockTooltipLines is { Count: > 0 })
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            foreach (var unlockTooltipLine in unlockTooltipLines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                if (unlockTooltipLine.Contains(':'))
                    MaterialUsageTooltip.DrawDetailLine(this.configuration, unlockTooltipLine);
                else
                    MaterialUsageTooltip.DrawDetailHeader(this.configuration, unlockTooltipLine);
            }
            ImGui.PopTextWrapPos();
        }

        this.DrawFishTooltipDetails(fishTooltipInfo);
        MaterialUsageTooltip.DrawLogStatusTooltipDetails(this.configuration, logStatusTooltipInfo);
        this.DrawCosmicExplorationTooltipDetails(cosmicExplorationTooltipInfo);
        this.DrawQuestTooltipDetails(questTooltipInfo);

        MaterialUsageTooltip.EndStyledTooltip(this.configuration);
    }

    private void DrawFishTooltipDetails(FishTooltipInfo? fishTooltipInfo)
    {
        if (fishTooltipInfo is null)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(this.configuration.AccentTextColor, "Fishing details");
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        MaterialUsageTooltip.DrawDetailRow(this.configuration, "Bait", fishTooltipInfo.BaitName);
        MaterialUsageTooltip.DrawDetailRow(this.configuration, "Fish type", fishTooltipInfo.FishType);
        MaterialUsageTooltip.DrawDetailRow(this.configuration, "Best zone", fishTooltipInfo.BestZone);
        MaterialUsageTooltip.DrawDetailRow(this.configuration, "Best spot", fishTooltipInfo.BestSpot);
        ImGui.PopTextWrapPos();
    }

    private void DrawCosmicExplorationTooltipDetails(CosmicExplorationTooltipInfo? cosmicExplorationTooltipInfo)
    {
        if (cosmicExplorationTooltipInfo is null)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(this.configuration.AccentTextColor, "Cosmic Exploration");
        MaterialUsageTooltip.DrawDetailLine(
            this.configuration,
            string.IsNullOrWhiteSpace(cosmicExplorationTooltipInfo.MissionName)
                ? "Cosmic Exploration item"
                : $"Mission: {cosmicExplorationTooltipInfo.MissionName}");
    }

    private void DrawQuestTooltipDetails(QuestTooltipInfo? questTooltipInfo)
    {
        if (questTooltipInfo is null)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(this.configuration.AccentTextColor, "Quest item");
        foreach (var line in questTooltipInfo.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
            MaterialUsageTooltip.DrawDetailLine(this.configuration, line);
    }

    private void DrawSavePlanControls(PlanSaveScope scope, string controlId = "")
    {
        var draft = this.GetPlanSaveDraft(scope);
        var planLabel = GetPlanSaveScopeLabel(scope);
        var selectedCount = this.GetPlanSaveScopeSelectionCount(scope);
        var idSuffix = string.IsNullOrWhiteSpace(controlId) ? string.Empty : $"-{controlId}";
        ImGui.TextColored(this.configuration.AccentColor, $"{planLabel.ToUpperInvariant()} PLAN");
        ImGui.SameLine();
        ImGui.TextDisabled($"{selectedCount} selected");

        ImGui.SetNextItemWidth(this.ScaleUi(260f));
        var submitted = ImGui.InputTextWithHint(
            $"##{planLabel.ToLowerInvariant()}-plan-name-{draft.InputNonce}{idSuffix}",
            $"{planLabel} plan name",
            ref draft.Name,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);
        DrawTooltipIfHovered($"Enter a name for a new {planLabel.ToLowerInvariant()} plan.");
        ImGui.SameLine();
        if (WindowTheme.ShadowedButton($"Save new plan##save-new-plan{idSuffix}") || submitted)
            this.SaveCurrentPlan(scope, saveAsNew: true);
        DrawTooltipIfHovered($"Save the selected {planLabel.ToLowerInvariant()} targets as a new named plan.");

        var hasLoadedPlan = this.loadedSavedPlan is not null &&
                            this.configuration.SavedRecipePlans.Contains(this.loadedSavedPlan);
        ImGui.SameLine();
        ImGui.BeginDisabled(!hasLoadedPlan);
        if (WindowTheme.ShadowedButton($"Update plan##update-plan{idSuffix}") && hasLoadedPlan)
            this.UpdateLoadedSavedPlan(scope);
        ImGui.EndDisabled();
        DrawTooltipIfHovered(hasLoadedPlan
            ? $"Update '{this.loadedSavedPlan!.Name}' with the current {planLabel.ToLowerInvariant()} targets."
            : "Load a saved plan to update it with the current targets.");

        ImGui.SetNextItemWidth(this.ScaleUi(220f));
        ImGui.InputTextWithHint(
            $"##{planLabel.ToLowerInvariant()}-plan-folder-{draft.InputNonce}{idSuffix}",
            "Folder (optional, use / for subfolders)",
            ref draft.FolderName,
            80);
        DrawTooltipIfHovered($"Optional folder name for this {planLabel.ToLowerInvariant()} plan. Use / to create subfolders.");

        this.DrawPlanMessage();
    }

    private PlanSaveDraft GetPlanSaveDraft(PlanSaveScope scope) => scope switch
    {
        PlanSaveScope.Recipe => this.recipePlanDraft,
        PlanSaveScope.Gatherable => this.gatherablePlanDraft,
        PlanSaveScope.Collectable => this.collectablePlanDraft,
        PlanSaveScope.DirectIngredient => this.directIngredientPlanDraft,
        PlanSaveScope.RawMaterial => this.rawMaterialPlanDraft,
        _ => this.recipePlanDraft,
    };

    private int GetPlanSaveScopeSelectionCount(PlanSaveScope scope) => scope switch
    {
        PlanSaveScope.Recipe => this.selectedRecipes.Count,
        PlanSaveScope.Gatherable => this.selectedGatherables.Count,
        PlanSaveScope.Collectable => this.selectedCollectables.Count,
        PlanSaveScope.DirectIngredient => this.GetDirectIngredientPlanNeeds(this.recipePlanDetails).Count(material => !IsElementalCatalyst(material)),
        PlanSaveScope.RawMaterial => this.GetRawMaterialPlanNeeds(this.recipePlanDetails).Count(material => !IsElementalCatalyst(material)),
        _ => 0,
    };

    private static string GetPlanSaveScopeLabel(PlanSaveScope scope) => scope switch
    {
        PlanSaveScope.Recipe => "Recipe",
        PlanSaveScope.Gatherable => "Gatherable",
        PlanSaveScope.Collectable => "Collectable",
        PlanSaveScope.DirectIngredient => "Direct Ingredients",
        PlanSaveScope.RawMaterial => "Raw Materials",
        _ => "Recipe",
    };

    private void ClearPlanSaveDraft(PlanSaveScope scope)
    {
        var draft = this.GetPlanSaveDraft(scope);
        draft.Name = string.Empty;
        draft.FolderName = string.Empty;
        draft.InputNonce++;
    }

    private void ClearAllPlanSaveDrafts()
    {
        this.ClearPlanSaveDraft(PlanSaveScope.Recipe);
        this.ClearPlanSaveDraft(PlanSaveScope.Gatherable);
        this.ClearPlanSaveDraft(PlanSaveScope.Collectable);
        this.ClearPlanSaveDraft(PlanSaveScope.DirectIngredient);
        this.ClearPlanSaveDraft(PlanSaveScope.RawMaterial);
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
                true,
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
        SavedRecipePlan? planToUpdate = null;
        SavedRecipePlan? planToDuplicate = null;
        SavedRecipePlan? planToRename = null;
        SavedRecipePlan? planToDelete = null;
        SavedRecipePlan? planToMove = null;
        string? folderToRename = null;
        string? folderToMove = null;
        string? folderToDelete = null;
        var loadSelectedPlans = false;
        var craftSelectedPlans = false;
        var moveSelectedPlans = false;
        var createFolder = false;
        if (this.selectedSavedPlans.RemoveWhere(plan =>
                !this.configuration.SavedRecipePlans.Contains(plan)) > 0)
            this.savedPlanCraftAvailabilityDirty = true;
        this.NormalizeSavedPlanFolders();
        var displayedPlans = this.configuration.SavedRecipePlans
            .OrderBy(plan => NormalizeFolderName(plan.FolderName))
            .ThenBy(plan => plan.Name)
            .ToList();
        var folderTree = BuildSavedPlanFolderTree(this.configuration.SavedPlanFolders, displayedPlans);

        void DrawSavedPlanRow(SavedRecipePlan savedPlan)
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
                this.savedPlanCraftAvailabilityDirty = true;
            }

            ImGui.SetCursorScreenPos(rowPos + this.ScaleUi(new Vector2(44f, 8f)));
            ImGui.PushStyleColor(ImGuiCol.Text, this.configuration.SavedPlanTextColor);
            ImGui.TextUnformatted(savedPlan.Name);
            ImGui.PopStyleColor();
            ImGui.SetCursorScreenPos(rowPos + this.ScaleUi(new Vector2(44f, 25f)));
            ImGui.TextColored(
                WithAlpha(this.configuration.SavedPlanTextColor, 0.72f),
                GetSavedPlanContentsLabel(savedPlan));

            var actionX = rowPos.X + rowWidth - this.ScaleUi(384f);
            ImGui.SetCursorScreenPos(new Vector2(actionX, rowPos.Y + this.ScaleUi(12f)));
            if (WindowTheme.ShadowedButton("Load", new Vector2(this.ScaleUi(48f), 0)))
                planToLoad = savedPlan;
            DrawTooltipIfHovered("Load this saved plan into the current recipe list.");
            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Update", new Vector2(this.ScaleUi(58f), 0)))
                planToUpdate = savedPlan;
            DrawTooltipIfHovered("Replace this saved plan with the current Recipe Plan, Gatherables, and Collectables.");
            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Move", new Vector2(this.ScaleUi(48f), 0)))
                planToMove = savedPlan;
            DrawTooltipIfHovered("Move this saved plan to another folder.");
            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Duplicate", new Vector2(this.ScaleUi(72f), 0)))
                planToDuplicate = savedPlan;
            DrawTooltipIfHovered("Create a copy of this saved plan.");
            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Rename", new Vector2(this.ScaleUi(62f), 0)))
                planToRename = savedPlan;
            DrawTooltipIfHovered("Rename this saved plan.");
            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Delete", new Vector2(this.ScaleUi(54f), 0)))
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

        void DrawFolderHeader(SavedPlanFolderNode folderNode, int depth)
        {
            ImGui.PushID($"saved-folder-{folderNode.FullPath}");
            var folderIndent = this.ScaleUi(18f);
            var planIndent = this.ScaleUi(12f);
            if (depth > 0)
                ImGui.Indent(folderIndent);
            var folderHeaderColor = depth == 0 && this.configuration.UseAccentForFolderHeaders
                ? this.configuration.AccentColor
                : depth == 0
                    ? this.configuration.FolderHeaderColor
                    : this.configuration.SubfolderHeaderColor;
            ImGui.PushStyleColor(ImGuiCol.Header, folderHeaderColor);
            ImGui.PushStyleColor(ImGuiCol.HeaderHovered, AdjustColor(folderHeaderColor, 0.06f));
            ImGui.PushStyleColor(ImGuiCol.HeaderActive, AdjustColor(folderHeaderColor, -0.04f));
            ImGui.PushStyleColor(
                ImGuiCol.Text,
                depth == 0
                    ? this.configuration.FolderHeaderTextColor
                    : this.configuration.SubfolderHeaderTextColor);
            if (this.savedPlanFoldersToClose.Remove(folderNode.FullPath))
            {
                ImGui.SetNextItemOpen(false, ImGuiCond.Always);
            }
            else
            {
                ImGui.SetNextItemOpen(false, ImGuiCond.Appearing);
            }
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, this.ScaleUi(10f));
            var isFolderOpen = ImGui.CollapsingHeader(
                $"{GetFolderDisplayName(folderNode.Name)} ({folderNode.TotalPlanCount})##folder-group",
                ImGuiTreeNodeFlags.AllowItemOverlap);
            ImGui.PopStyleVar();
            ImGui.PopStyleColor(4);
            if (ImGui.IsItemHovered())
                this.DrawFolderRecipeTooltip(folderNode);

            if (!string.IsNullOrWhiteSpace(folderNode.FullPath))
            {
                var renameButtonWidth = Math.Max(
                    this.ScaleUi(68f),
                    ImGui.CalcTextSize("Rename").X + (ImGui.GetStyle().FramePadding.X * 2f));
                var moveButtonWidth = Math.Max(
                    this.ScaleUi(58f),
                    ImGui.CalcTextSize("Move").X + (ImGui.GetStyle().FramePadding.X * 2f));
                var deleteButtonWidth = Math.Max(
                    this.ScaleUi(64f),
                    ImGui.CalcTextSize("Delete").X + (ImGui.GetStyle().FramePadding.X * 2f));
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                var totalButtonWidth = moveButtonWidth + renameButtonWidth + deleteButtonWidth + (spacing * 2);
                var targetX = ImGui.GetWindowContentRegionMax().X - totalButtonWidth;
                ImGui.SameLine();
                ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), targetX));
                if (WindowTheme.ShadowedButton("Move", new Vector2(moveButtonWidth, 0)))
                    folderToMove = folderNode.FullPath;
                DrawTooltipIfHovered("Move this folder into another parent folder.");
                ImGui.SameLine();
                if (WindowTheme.ShadowedButton("Rename", new Vector2(renameButtonWidth, 0)))
                    folderToRename = folderNode.FullPath;
                DrawTooltipIfHovered("Rename this folder and keep all plans and subfolders inside it.");
                ImGui.SameLine();
                if (WindowTheme.ShadowedButton("Delete", new Vector2(deleteButtonWidth, 0)))
                {
                    if (ImGui.GetIO().KeyCtrl)
                        folderToDelete = folderNode.FullPath;
                    else
                        this.ShowPlanMessage("Hold Ctrl while clicking Delete. Plans will move to Unfiled.", true);
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Hold Ctrl and click to delete this folder and its subfolders. Plans will move to Unfiled.");
                    ImGui.EndTooltip();
                }
            }

            if (isFolderOpen)
            {
                foreach (var childFolder in folderNode.Children)
                    DrawFolderHeader(childFolder, depth + 1);

                foreach (var savedPlan in folderNode.Plans)
                {
                    if (depth > 0)
                        ImGui.Indent(planIndent);
                    DrawSavedPlanRow(savedPlan);
                    if (depth > 0)
                        ImGui.Unindent(planIndent);
                }
            }

            if (depth > 0)
                ImGui.Unindent(folderIndent);
            ImGui.PopID();
        }

        if (folderTree.Plans.Count > 0)
        {
            var unfiledNode = new SavedPlanFolderNode
            {
                Name = string.Empty,
                FullPath = string.Empty,
            };
            unfiledNode.Plans.AddRange(folderTree.Plans);
            DrawFolderHeader(unfiledNode, 0);
        }

        foreach (var childFolder in folderTree.Children)
            DrawFolderHeader(childFolder, 0);

        if (this.selectedSavedPlans.Count > 0)
        {
            if (WindowTheme.ShadowedButton($"Load selected plans ({this.selectedSavedPlans.Count})"))
                loadSelectedPlans = true;
            DrawTooltipIfHovered("Load all checked plans into one combined recipe list.");

            ImGui.SameLine();
            this.RefreshSelectedSavedPlanCraftAvailability();
            ImGui.BeginDisabled(!this.canCraftSelectedSavedPlans);
            craftSelectedPlans =
                WindowTheme.ShadowedButton($"Craft selected plans ({this.selectedSavedPlans.Count})");
            ImGui.EndDisabled();
            DrawTooltipIfHovered(
                this.canCraftSelectedSavedPlans
                    ? "Queue all checked plans in Artisan, including required pre-crafts."
                    : "Only activates when every selected recipe's materials are already in inventory. Use Refresh Inventory if needed.",
                allowWhenDisabled: true);

            ImGui.SameLine();
            if (WindowTheme.ShadowedButton($"Move selected ({this.selectedSavedPlans.Count})"))
                moveSelectedPlans = true;
            DrawTooltipIfHovered("Move all checked plans into a folder.");

            ImGui.SameLine();
            if (WindowTheme.ShadowedButton($"Export selected ({this.selectedSavedPlans.Count})"))
                this.ExportSavedPlansToClipboard(this.selectedSavedPlans);
            DrawTooltipIfHovered("Copy the checked saved plans to the clipboard, including folder names.");

            ImGui.SameLine();
            if (WindowTheme.ShadowedButton($"Clear selected ({this.selectedSavedPlans.Count})"))
                this.ClearSelectedPlansAndCurrentPlan();
            DrawTooltipIfHovered("Clear the checked plans and the current Recipe Plan without deleting any saved plans.");
        }
        else if (this.configuration.SavedRecipePlans.Count > 0)
        {
            if (WindowTheme.ShadowedButton("Export all saved plans"))
                this.ExportSavedPlansToClipboard(this.configuration.SavedRecipePlans);
            DrawTooltipIfHovered("Copy every saved plan to the clipboard.");
        }

        if (this.configuration.SavedRecipePlans.Count > 0 || this.configuration.SavedPlanFolders.Count > 0)
        {
            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Create folder"))
                createFolder = true;
            DrawTooltipIfHovered("Create a saved-plan folder. Use / to create nested folders.");

            ImGui.SameLine();
            if (WindowTheme.ShadowedButton("Import plans from clipboard"))
                this.ImportSavedPlansFromClipboard();
            DrawTooltipIfHovered("Import saved plans from copied Recipe Helper plan data.");
        }

        if (planToLoad is not null)
            this.LoadSavedPlan(planToLoad);

        if (planToUpdate is not null)
            this.UpdateSavedPlan(planToUpdate);

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
            this.renameFolderName = GetFolderDisplayName(folderToRename);
            this.renameFolderError = string.Empty;
            this.isRenameFolderPopupOpen = true;
            this.renameFolderPopupRequested = true;
        }

        if (folderToMove is not null)
        {
            this.movingFolderSource = folderToMove;
            this.moveFolderParentName = GetParentFolderPath(folderToMove);
            this.moveFolderError = string.Empty;
            this.isMoveFolderPopupOpen = true;
            this.moveFolderPopupRequested = true;
        }

        if (folderToDelete is not null)
            this.DeleteSavedPlanFolder(folderToDelete);

        if (createFolder)
        {
            this.createFolderName = string.Empty;
            this.createFolderError = string.Empty;
            this.isCreateFolderPopupOpen = true;
            this.createFolderPopupRequested = true;
        }

        if (planToDelete is not null)
        {
            this.selectedSavedPlans.Remove(planToDelete);
            this.savedPlanCraftAvailabilityDirty = true;
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
        this.DrawMoveFolderPopup();
        this.DrawCreateFolderPopup();
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
            Gatherables = source.Gatherables.Select(CloneSavedSupplemental).ToList(),
            Collectables = source.Collectables.Select(CloneSavedSupplemental).ToList(),
            DirectIngredients = source.DirectIngredients.Select(CloneSavedSupplemental).ToList(),
            RawMaterials = source.RawMaterials.Select(CloneSavedSupplemental).ToList(),
        });
        this.saveConfiguration();
        this.ShowPlanMessage($"Duplicated plan as '{newName}'.", false);
    }

    private void UpdateSavedPlan(SavedRecipePlan savedPlan)
    {
        if (this.selectedRecipes.Count == 0 &&
            this.selectedGatherables.Count == 0 &&
            this.selectedCollectables.Count == 0 &&
            this.selectedDirectIngredients.Count == 0 &&
            this.selectedRawMaterials.Count == 0)
        {
            this.ShowPlanMessage("Add recipes, ingredients, gatherables, or collectables before updating a saved plan.", true);
            return;
        }

        savedPlan.Recipes = this.selectedRecipes
            .Select(selection => new SavedRecipePlanEntry
            {
                RecipeId = selection.Recipe.RecipeId,
                ResultItemId = selection.Recipe.ResultItemId,
                ResultName = selection.Recipe.ResultName,
                ResultAmount = selection.Recipe.ResultAmount,
                JobAbbreviations = selection.Recipe.JobAbbreviations,
                DesiredAmount = selection.DesiredAmount,
            })
            .ToList();
        savedPlan.Gatherables = this.selectedGatherables
            .Select(CreateSavedSupplementalEntry)
            .ToList();
        savedPlan.Collectables = this.selectedCollectables
            .Select(CreateSavedSupplementalEntry)
            .ToList();
        savedPlan.DirectIngredients = CreateSavedIngredientEntries(
            this.GetDirectIngredientPlanNeeds(this.recipePlanDetails)
                .Where(material => !IsElementalCatalyst(material)));
        savedPlan.RawMaterials = CreateSavedIngredientEntries(
            this.GetRawMaterialPlanNeeds(this.recipePlanDetails)
                .Where(material => !IsElementalCatalyst(material)));
        this.loadedSavedPlan = savedPlan;
        this.savedPlanCraftAvailabilityDirty = true;
        this.saveConfiguration();
        this.ShowPlanMessage($"Updated plan '{savedPlan.Name}'.", false);
    }

    private void UpdateLoadedSavedPlan(PlanSaveScope scope)
    {
        if (this.loadedSavedPlan is null ||
            !this.configuration.SavedRecipePlans.Contains(this.loadedSavedPlan))
        {
            this.ShowPlanMessage("Load a saved plan before using Update plan.", true);
            return;
        }

        var planLabel = GetPlanSaveScopeLabel(scope);
        if (this.GetPlanSaveScopeSelectionCount(scope) == 0)
        {
            this.ShowPlanMessage($"Add at least one {planLabel.ToLowerInvariant()} before updating.", true);
            return;
        }

        switch (scope)
        {
            case PlanSaveScope.Recipe:
                this.loadedSavedPlan.Recipes = this.selectedRecipes
                    .Select(selection => new SavedRecipePlanEntry
                    {
                        RecipeId = selection.Recipe.RecipeId,
                        ResultItemId = selection.Recipe.ResultItemId,
                        ResultName = selection.Recipe.ResultName,
                        ResultAmount = selection.Recipe.ResultAmount,
                        JobAbbreviations = selection.Recipe.JobAbbreviations,
                        DesiredAmount = selection.DesiredAmount,
                    })
                    .ToList();
                break;
            case PlanSaveScope.Gatherable:
                this.loadedSavedPlan.Gatherables = this.selectedGatherables
                    .Select(CreateSavedSupplementalEntry)
                    .ToList();
                break;
            case PlanSaveScope.Collectable:
                this.loadedSavedPlan.Collectables = this.selectedCollectables
                    .Select(CreateSavedSupplementalEntry)
                    .ToList();
                break;
            case PlanSaveScope.DirectIngredient:
                this.loadedSavedPlan.DirectIngredients = CreateSavedIngredientEntries(
                    this.GetDirectIngredientPlanNeeds(this.recipePlanDetails)
                        .Where(material => !IsElementalCatalyst(material)));
                break;
            case PlanSaveScope.RawMaterial:
                this.loadedSavedPlan.RawMaterials = CreateSavedIngredientEntries(
                    this.GetRawMaterialPlanNeeds(this.recipePlanDetails)
                        .Where(material => !IsElementalCatalyst(material)));
                break;
        }

        this.savedPlanCraftAvailabilityDirty = true;
        this.saveConfiguration();
        this.ShowPlanMessage($"Updated plan '{this.loadedSavedPlan.Name}'.", false);
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

        this.DrawFolderSelectionControls(ref this.movePlanFolderName, includeUnfiled: true);
        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "Folder",
            ref this.movePlanFolderName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextDisabled("Leave blank to keep the plan unfiled. Use / to create subfolders.");

        if (WindowTheme.ShadowedButton("Move") || submitted)
            this.TryMoveSavedPlan();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Cancel"))
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

        this.DrawFolderSelectionControls(ref this.movePlanFolderName, includeUnfiled: true);
        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "Folder",
            ref this.movePlanFolderName,
            80,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextDisabled($"Move {this.movingSelectedPlans.Count} selected plan{(this.movingSelectedPlans.Count == 1 ? string.Empty : "s")} into this folder. Use / to create subfolders.");

        if (WindowTheme.ShadowedButton("Move all") || submitted)
            this.TryMoveSelectedPlans();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Cancel"))
        {
            this.isMoveSelectedPlansPopupOpen = false;
            this.movingSelectedPlans = [];
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawMoveFolderPopup()
    {
        if (this.moveFolderPopupRequested)
        {
            ImGui.OpenPopup("Move folder");
            this.moveFolderPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Move folder",
                ref this.isMoveFolderPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);

        ImGui.TextDisabled($"Moving folder: {GetFolderDisplayName(this.movingFolderSource)}");
        this.DrawFolderSelectionControls(ref this.moveFolderParentName, includeUnfiled: true);
        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "Parent folder",
            ref this.moveFolderParentName,
            120,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextDisabled("Leave blank to move it to the top level. Use / to create subfolders.");

        if (!string.IsNullOrWhiteSpace(this.moveFolderError))
            ImGui.TextColored(this.configuration.MissingTextColor, this.moveFolderError);

        if (WindowTheme.ShadowedButton("Move folder") || submitted)
            this.TryMoveFolder();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Cancel"))
        {
            this.isMoveFolderPopupOpen = false;
            this.movingFolderSource = string.Empty;
            this.moveFolderParentName = string.Empty;
            this.moveFolderError = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawCreateFolderPopup()
    {
        if (this.createFolderPopupRequested)
        {
            ImGui.OpenPopup("Create folder");
            this.createFolderPopupRequested = false;
        }

        if (!ImGui.BeginPopupModal(
                "Create folder",
                ref this.isCreateFolderPopupOpen,
                ImGuiWindowFlags.AlwaysAutoResize))
            return;
        WindowTheme.ApplyTextScale(this.configuration, includeMainWindowScale: true);

        ImGui.TextDisabled($"Parent: {GetFolderDisplayName(GetParentFolderPath(this.renamingFolderSource))}");
        ImGui.SetNextItemWidth(280);
        var submitted = ImGui.InputText(
            "Folder",
            ref this.createFolderName,
            120,
            ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextDisabled("Use / to create nested folders.");

        if (!string.IsNullOrWhiteSpace(this.createFolderError))
            ImGui.TextColored(this.configuration.MissingTextColor, this.createFolderError);

        if (WindowTheme.ShadowedButton("Create folder") || submitted)
            this.TryCreateFolder();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Cancel"))
        {
            this.isCreateFolderPopupOpen = false;
            this.createFolderName = string.Empty;
            this.createFolderError = string.Empty;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void DrawFolderSelectionControls(ref string folderValue, bool includeUnfiled = false)
    {
        var folders = GetSavedPlanFolders(this.configuration.SavedPlanFolders, this.configuration.SavedRecipePlans);
        if (folders.Count == 0)
            return;

        var selectedFolder = NormalizeFolderName(folderValue);
        var selectedIndex = folders.FindIndex(folder =>
            string.Equals(folder, selectedFolder, StringComparison.OrdinalIgnoreCase));
        var preview = selectedIndex >= 0 ? GetFolderDisplayName(folders[selectedIndex]) : "Choose existing folder";

        ImGui.SetNextItemWidth(280);
        if (!ImGui.BeginCombo("Existing folders", preview))
            return;

        var useUnfiled = string.IsNullOrWhiteSpace(selectedFolder);
        if (includeUnfiled && ImGui.Selectable("Unfiled", useUnfiled))
            folderValue = string.Empty;

        foreach (var folder in folders)
        {
            var isSelected = string.Equals(folder, selectedFolder, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(folder, isSelected))
                folderValue = folder;
        }

        ImGui.EndCombo();
        ImGui.TextDisabled("Choose an existing folder or type a new one below. Use / to create subfolders.");
    }

    private void TryMoveSavedPlan()
    {
        if (this.movingPlan is null)
            return;

        this.selectedSavedPlans.Remove(this.movingPlan);
        this.movingPlan.FolderName = NormalizeFolderName(this.movePlanFolderName);
        this.EnsureSavedPlanFolderHierarchy(this.movingPlan.FolderName);
        this.saveConfiguration();
        this.ShowPlanMessage($"Moved plan '{this.movingPlan.Name}'.", false);
        this.isMovePlanPopupOpen = false;
        this.movingPlan = null;
        ImGui.CloseCurrentPopup();
    }

    private void TryMoveSelectedPlans()
    {
        var folderName = NormalizeFolderName(this.movePlanFolderName);
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
            this.EnsureSavedPlanFolderHierarchy(plan.FolderName);
            this.selectedSavedPlans.Remove(plan);
        }

        this.saveConfiguration();
        this.ShowPlanMessage($"Moved {plans.Count} saved plan{(plans.Count == 1 ? string.Empty : "s")}.", false);
        this.isMoveSelectedPlansPopupOpen = false;
        this.movingSelectedPlans = [];
        ImGui.CloseCurrentPopup();
    }

    private void TryCreateFolder()
    {
        var folderName = NormalizeFolderName(this.createFolderName);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            this.createFolderError = "Enter a folder name.";
            return;
        }

        if (this.configuration.SavedPlanFolders.Contains(folderName, StringComparer.OrdinalIgnoreCase))
        {
            this.createFolderError = "That folder already exists.";
            return;
        }

        this.EnsureSavedPlanFolderHierarchy(folderName);
        this.saveConfiguration();
        this.ShowPlanMessage($"Created folder '{GetFolderDisplayName(folderName)}'.", false);
        this.isCreateFolderPopupOpen = false;
        this.createFolderName = string.Empty;
        this.createFolderError = string.Empty;
        ImGui.CloseCurrentPopup();
    }

    private void DeleteSavedPlanFolder(string folderPath)
    {
        var normalizedFolder = NormalizeFolderName(folderPath);
        if (string.IsNullOrWhiteSpace(normalizedFolder))
            return;

        var movedPlanCount = 0;
        foreach (var plan in this.configuration.SavedRecipePlans)
        {
            if (!FolderMatchesOrContains(NormalizeFolderName(plan.FolderName), normalizedFolder))
                continue;

            plan.FolderName = string.Empty;
            movedPlanCount++;
        }

        this.configuration.SavedPlanFolders = this.configuration.SavedPlanFolders
            .Where(folder => !FolderMatchesOrContains(NormalizeFolderName(folder), normalizedFolder))
            .ToList();
        this.NormalizeSavedPlanFolders();
        this.saveConfiguration();
        this.ShowPlanMessage(
            movedPlanCount == 0
                ? $"Deleted folder '{GetFolderDisplayName(normalizedFolder)}'."
                : $"Deleted folder '{GetFolderDisplayName(normalizedFolder)}'; moved {movedPlanCount} plan{(movedPlanCount == 1 ? string.Empty : "s")} to Unfiled.",
            false);
    }

    private void TryMoveFolder()
    {
        var sourceFolder = NormalizeFolderName(this.movingFolderSource);
        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            this.moveFolderError = "Choose a folder to move.";
            return;
        }

        var destinationParent = NormalizeFolderName(this.moveFolderParentName);
        if (FolderMatchesOrContains(destinationParent, sourceFolder))
        {
            this.moveFolderError = "Choose a parent folder outside the folder you are moving.";
            return;
        }

        var folderName = GetFolderDisplayName(sourceFolder);
        var targetFolder = CombineFolderPath(destinationParent, folderName);
        if (string.Equals(targetFolder, sourceFolder, StringComparison.OrdinalIgnoreCase))
        {
            this.isMoveFolderPopupOpen = false;
            this.movingFolderSource = string.Empty;
            this.moveFolderParentName = string.Empty;
            this.moveFolderError = string.Empty;
            ImGui.CloseCurrentPopup();
            return;
        }

        foreach (var plan in this.configuration.SavedRecipePlans)
        {
            var planFolder = NormalizeFolderName(plan.FolderName);
            if (!FolderMatchesOrContains(planFolder, sourceFolder))
                continue;

            var suffix = planFolder.Length == sourceFolder.Length
                ? string.Empty
                : planFolder[sourceFolder.Length..];
            plan.FolderName = NormalizeFolderName($"{targetFolder}{suffix}");
        }

        this.MoveSavedPlanFolderPaths(sourceFolder, targetFolder);

        this.saveConfiguration();
        this.ShowPlanMessage($"Moved folder '{folderName}'.", false);
        this.isMoveFolderPopupOpen = false;
        this.movingFolderSource = string.Empty;
        this.moveFolderParentName = string.Empty;
        this.moveFolderError = string.Empty;
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

        if (WindowTheme.ShadowedButton("Rename") || submitted)
            this.TryRenameSavedPlan();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Cancel"))
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

        if (WindowTheme.ShadowedButton("Rename") || submitted)
            this.TryRenameFolder();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Cancel"))
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
        var cleanName = NormalizeFolderName(this.renameFolderName);
        if (cleanName.Length == 0)
        {
            this.renameFolderError = "Enter a folder name.";
            return;
        }

        if (cleanName.Contains('/'))
        {
            this.renameFolderError = "Enter one folder name. Use Move to change its parent folder.";
            return;
        }

        var sourceFolder = NormalizeFolderName(this.renamingFolderSource);
        var targetFolder = CombineFolderPath(GetParentFolderPath(sourceFolder), cleanName);
        if (string.Equals(targetFolder, sourceFolder, StringComparison.OrdinalIgnoreCase))
        {
            this.isRenameFolderPopupOpen = false;
            this.renamingFolderSource = string.Empty;
            this.renameFolderError = string.Empty;
            ImGui.CloseCurrentPopup();
            return;
        }

        var folderExists = GetSavedPlanFolders(
                this.configuration.SavedPlanFolders,
                this.configuration.SavedRecipePlans)
            .Any(folder => string.Equals(folder, targetFolder, StringComparison.OrdinalIgnoreCase));
        if (folderExists)
        {
            this.renameFolderError = "A folder with that name already exists in this parent folder.";
            return;
        }

        foreach (var plan in this.configuration.SavedRecipePlans)
        {
            var planFolder = NormalizeFolderName(plan.FolderName);
            if (!FolderMatchesOrContains(planFolder, sourceFolder))
                continue;

            var suffix = planFolder.Length == sourceFolder.Length
                ? string.Empty
                : planFolder[sourceFolder.Length..];
            plan.FolderName = $"{targetFolder}{suffix}";
        }

        this.MoveSavedPlanFolderPaths(sourceFolder, targetFolder);

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
            JobAbbreviations = recipe.JobAbbreviations,
            DesiredAmount = recipe.DesiredAmount,
        };

    private static SavedSupplementalPlanEntry CreateSavedSupplementalEntry(SupplementalItemSelection selection) =>
        new()
        {
            ResultItemId = selection.Item.ResultItemId,
            ResultName = selection.Item.ResultName,
            JobAbbreviations = selection.Item.JobAbbreviations,
            SearchMetadata = selection.Item.SearchMetadata,
            DesiredAmount = selection.DesiredAmount,
        };

    private static List<SavedSupplementalPlanEntry> CreateSavedIngredientEntries(
        IEnumerable<IngredientNeed> ingredients) =>
        ingredients
            .Where(ingredient => ingredient.ItemId != 0 && ingredient.Required > 0)
            .GroupBy(ingredient => ingredient.ItemId)
            .Select(group =>
            {
                var first = group.First();
                return new SavedSupplementalPlanEntry
                {
                    ResultItemId = first.ItemId,
                    ResultName = first.Name,
                    DesiredAmount = (uint)Math.Min(
                        group.Aggregate(0UL, (total, ingredient) => total + ingredient.Required),
                        uint.MaxValue),
                };
            })
            .OrderBy(ingredient => ingredient.ResultName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static SavedSupplementalPlanEntry CloneSavedSupplemental(SavedSupplementalPlanEntry item) =>
        new()
        {
            ResultItemId = item.ResultItemId,
            ResultName = item.ResultName,
            JobAbbreviations = item.JobAbbreviations,
            SearchMetadata = item.SearchMetadata,
            DesiredAmount = item.DesiredAmount,
        };

    private static string GetSavedPlanContentsLabel(SavedRecipePlan savedPlan)
    {
        var parts = new List<string>();
        if (savedPlan.Recipes.Count > 0)
            parts.Add($"{savedPlan.Recipes.Count} recipe{(savedPlan.Recipes.Count == 1 ? string.Empty : "s")}");
        if (savedPlan.Gatherables.Count > 0)
            parts.Add($"{savedPlan.Gatherables.Count} gatherable{(savedPlan.Gatherables.Count == 1 ? string.Empty : "s")}");
        if (savedPlan.Collectables.Count > 0)
            parts.Add($"{savedPlan.Collectables.Count} collectable{(savedPlan.Collectables.Count == 1 ? string.Empty : "s")}");
        if (savedPlan.DirectIngredients.Count > 0)
            parts.Add($"{savedPlan.DirectIngredients.Count} direct ingredient{(savedPlan.DirectIngredients.Count == 1 ? string.Empty : "s")}");
        if (savedPlan.RawMaterials.Count > 0)
            parts.Add($"{savedPlan.RawMaterials.Count} raw material{(savedPlan.RawMaterials.Count == 1 ? string.Empty : "s")}");

        return parts.Count == 0 ? "No saved items" : string.Join(" | ", parts);
    }

    private void DrawFolderRecipeTooltip(SavedPlanFolderNode folderNode)
    {
        MaterialUsageTooltip.BeginStyledTooltip(this.configuration);
        if (folderNode.Children.Count > 0)
        {
            ImGui.TextColored(this.configuration.AccentColor, "Subfolders");
            foreach (var childFolder in folderNode.Children
                         .OrderBy(child => child.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                ImGui.BulletText(
                    $"{GetFolderDisplayName(childFolder.Name)} ({childFolder.TotalPlanCount})");
            }
        }
        else
        {
            var recipeNames = folderNode.Plans
                .SelectMany(plan => plan.Recipes)
                .Select(recipe => recipe.ResultName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ImGui.TextColored(this.configuration.AccentColor, "Recipes in this folder");
            if (recipeNames.Count == 0)
            {
                ImGui.TextDisabled("No recipe plans are stored here.");
            }
            else if (recipeNames.Count > 8 &&
                     ImGui.BeginTable(
                         "##folder-recipes",
                         2,
                         ImGuiTableFlags.SizingStretchSame))
            {
                var splitIndex = (recipeNames.Count + 1) / 2;
                ImGui.TableNextColumn();
                foreach (var recipeName in recipeNames.Take(splitIndex))
                    ImGui.BulletText(recipeName);

                ImGui.TableNextColumn();
                foreach (var recipeName in recipeNames.Skip(splitIndex))
                    ImGui.BulletText(recipeName);
                ImGui.EndTable();
            }
            else
            {
                foreach (var recipeName in recipeNames)
                    ImGui.BulletText(recipeName);
            }
        }
        MaterialUsageTooltip.EndStyledTooltip(this.configuration);
    }

    private static List<SupplementalItemSelection> CreateSavedSupplementalSelections(
        IEnumerable<SavedSupplementalPlanEntry> items,
        SearchResultKind kind) =>
        items
            .Where(item => item.ResultItemId != 0 && !string.IsNullOrWhiteSpace(item.ResultName))
            .GroupBy(item => item.ResultItemId)
            .Select(group =>
            {
                var first = group.First();
                var desiredAmount = (uint)Math.Min(
                    group.Aggregate(
                        0UL,
                        (total, item) => total + Math.Max(1U, item.DesiredAmount)),
                    uint.MaxValue);
                return new SupplementalItemSelection
                {
                    Item = new RecipeMatch(
                        CreateSavedSupplementalSearchId(first.ResultItemId, kind),
                        first.ResultItemId,
                        first.ResultName,
                        1,
                        first.JobAbbreviations,
                        kind,
                        first.SearchMetadata,
                        false),
                    DesiredAmount = desiredAmount,
                };
            })
            .OrderBy(selection => selection.Item.ResultName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static List<SavedSupplementalPlanEntry> CreateCombinedSavedIngredientEntries(
        IEnumerable<SavedSupplementalPlanEntry> ingredients) =>
        ingredients
            .Where(ingredient => ingredient.ResultItemId != 0 && ingredient.DesiredAmount > 0)
            .GroupBy(ingredient => ingredient.ResultItemId)
            .Select(group =>
            {
                var first = group.First();
                return new SavedSupplementalPlanEntry
                {
                    ResultItemId = first.ResultItemId,
                    ResultName = first.ResultName,
                    DesiredAmount = (uint)Math.Min(
                        group.Aggregate(0UL, (total, ingredient) => total + ingredient.DesiredAmount),
                        uint.MaxValue),
                };
            })
            .OrderBy(ingredient => ingredient.ResultName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    private static uint CreateSavedSupplementalSearchId(uint itemId, SearchResultKind kind) =>
        kind == SearchResultKind.CollectibleItem
            ? 4_000_000_000u + itemId
            : 3_000_000_000u + itemId;

    private static string NormalizeFolderName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return string.Empty;

        var segments = folderName
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        return segments.Length == 0 ? string.Empty : string.Join('/', segments);
    }

    private void NormalizeSavedPlanFolders()
    {
        var normalizedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in this.configuration.SavedPlanFolders)
        {
            foreach (var path in ExpandFolderHierarchy(NormalizeFolderName(folder)))
                normalizedFolders.Add(path);
        }

        foreach (var plan in this.configuration.SavedRecipePlans)
        {
            foreach (var path in ExpandFolderHierarchy(NormalizeFolderName(plan.FolderName)))
                normalizedFolders.Add(path);
        }

        this.configuration.SavedPlanFolders = normalizedFolders
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void EnsureSavedPlanFolderHierarchy(string? folderPath)
    {
        foreach (var path in ExpandFolderHierarchy(NormalizeFolderName(folderPath)))
        {
            if (!this.configuration.SavedPlanFolders.Contains(path, StringComparer.OrdinalIgnoreCase))
                this.configuration.SavedPlanFolders.Add(path);
        }

        this.configuration.SavedPlanFolders = this.configuration.SavedPlanFolders
            .Select(NormalizeFolderName)
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void MoveSavedPlanFolderPaths(string sourceFolder, string targetFolder)
    {
        var updatedFolders = new List<string>();
        foreach (var folder in this.configuration.SavedPlanFolders)
        {
            var normalizedFolder = NormalizeFolderName(folder);
            if (!FolderMatchesOrContains(normalizedFolder, sourceFolder))
            {
                updatedFolders.Add(normalizedFolder);
                continue;
            }

            var suffix = normalizedFolder.Length == sourceFolder.Length
                ? string.Empty
                : normalizedFolder[sourceFolder.Length..];
            updatedFolders.Add(NormalizeFolderName($"{targetFolder}{suffix}"));
        }

        this.configuration.SavedPlanFolders = updatedFolders;
        this.NormalizeSavedPlanFolders();
    }

    private static List<string> ExpandFolderHierarchy(string folderPath)
    {
        var normalized = NormalizeFolderName(folderPath);
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var paths = new List<string>(segments.Length);
        var current = string.Empty;
        foreach (var segment in segments)
        {
            current = string.IsNullOrWhiteSpace(current) ? segment : $"{current}/{segment}";
            paths.Add(current);
        }

        return paths;
    }

    private static List<string> GetSavedPlanFolders(IEnumerable<string> folders, IEnumerable<SavedRecipePlan> plans) =>
        folders.Select(NormalizeFolderName)
            .Concat(plans.Select(plan => NormalizeFolderName(plan.FolderName)))
            .SelectMany(ExpandFolderHierarchy)
            .Where(folderName => !string.IsNullOrWhiteSpace(folderName))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(folderName => folderName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static string GetFolderDisplayName(string folderName) =>
        string.IsNullOrWhiteSpace(folderName)
            ? "Unfiled"
            : folderName.Contains('/')
                ? folderName[(folderName.LastIndexOf('/') + 1)..]
                : folderName;

    private static string GetParentFolderPath(string folderPath)
    {
        var normalized = NormalizeFolderName(folderPath);
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('/'))
            return string.Empty;

        return normalized[..normalized.LastIndexOf('/')];
    }

    private static string CombineFolderPath(string parentFolderPath, string folderName)
    {
        var normalizedParent = NormalizeFolderName(parentFolderPath);
        var normalizedName = NormalizeFolderName(folderName);
        if (string.IsNullOrWhiteSpace(normalizedParent))
            return normalizedName;
        if (string.IsNullOrWhiteSpace(normalizedName))
            return normalizedParent;

        return $"{normalizedParent}/{normalizedName}";
    }

    private static bool FolderMatchesOrContains(string folderPath, string parentFolderPath) =>
        string.Equals(folderPath, parentFolderPath, StringComparison.OrdinalIgnoreCase) ||
        folderPath.StartsWith(parentFolderPath + "/", StringComparison.OrdinalIgnoreCase);

    private static SavedPlanFolderNode BuildSavedPlanFolderTree(IEnumerable<string> folders, IEnumerable<SavedRecipePlan> plans)
    {
        var root = new SavedPlanFolderNode
        {
            Name = string.Empty,
            FullPath = string.Empty,
        };

        foreach (var folder in GetSavedPlanFolders(folders, plans))
        {
            var currentNode = root;
            var currentPath = string.Empty;
            foreach (var segment in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                currentPath = string.IsNullOrWhiteSpace(currentPath)
                    ? segment
                    : $"{currentPath}/{segment}";
                var nextNode = currentNode.Children.FirstOrDefault(child =>
                    string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));
                if (nextNode is null)
                {
                    nextNode = new SavedPlanFolderNode
                    {
                        Name = segment,
                        FullPath = currentPath,
                    };
                    currentNode.Children.Add(nextNode);
                }

                currentNode = nextNode;
            }

        }

        foreach (var plan in plans)
        {
            var normalizedFolder = NormalizeFolderName(plan.FolderName);
            if (string.IsNullOrWhiteSpace(normalizedFolder))
            {
                root.Plans.Add(plan);
                continue;
            }

            var currentNode = root;
            foreach (var segment in normalizedFolder.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                currentNode = currentNode.Children.First(child =>
                    string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));
            }

            currentNode.Plans.Add(plan);
        }

        SortFolderTree(root);
        return root;
    }

    private static void SortFolderTree(SavedPlanFolderNode node)
    {
        node.Children.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        node.Plans.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
        foreach (var child in node.Children)
            SortFolderTree(child);
    }

    private void SaveCurrentPlan(PlanSaveScope scope, bool saveAsNew)
    {
        var draft = this.GetPlanSaveDraft(scope);
        var planLabel = GetPlanSaveScopeLabel(scope);
        var cleanName = draft.Name.Trim();
        if (cleanName.Length == 0)
        {
            this.ShowPlanMessage($"Enter a name before saving the {planLabel.ToLowerInvariant()} plan.", true);
            return;
        }

        if (this.GetPlanSaveScopeSelectionCount(scope) == 0)
        {
            this.ShowPlanMessage($"Add at least one {planLabel.ToLowerInvariant()} before saving.", true);
            return;
        }

        var savedPlan = new SavedRecipePlan
        {
            Name = cleanName,
            FolderName = NormalizeFolderName(draft.FolderName),
            Recipes = scope == PlanSaveScope.Recipe
                ? this.selectedRecipes.Select(selection =>
                new SavedRecipePlanEntry
                {
                    RecipeId = selection.Recipe.RecipeId,
                    ResultItemId = selection.Recipe.ResultItemId,
                    ResultName = selection.Recipe.ResultName,
                    ResultAmount = selection.Recipe.ResultAmount,
                    JobAbbreviations = selection.Recipe.JobAbbreviations,
                    DesiredAmount = selection.DesiredAmount,
                }).ToList()
                : [],
            Gatherables = scope == PlanSaveScope.Gatherable
                ? this.selectedGatherables
                .Select(CreateSavedSupplementalEntry)
                .ToList()
                : [],
            Collectables = scope == PlanSaveScope.Collectable
                ? this.selectedCollectables
                .Select(CreateSavedSupplementalEntry)
                .ToList()
                : [],
            DirectIngredients = scope == PlanSaveScope.DirectIngredient
                ? CreateSavedIngredientEntries(
                    this.GetDirectIngredientPlanNeeds(this.recipePlanDetails)
                        .Where(material => !IsElementalCatalyst(material)))
                : [],
            RawMaterials = scope == PlanSaveScope.RawMaterial
                ? CreateSavedIngredientEntries(
                    this.GetRawMaterialPlanNeeds(this.recipePlanDetails)
                        .Where(material => !IsElementalCatalyst(material)))
                : [],
        };
        var existingIndex = this.configuration.SavedRecipePlans.FindIndex(plan =>
            string.Equals(
                plan.Name,
                cleanName,
                StringComparison.OrdinalIgnoreCase));
        if (saveAsNew && existingIndex >= 0)
        {
            this.ShowPlanMessage($"A plan named '{cleanName}' already exists. Use Update plan or choose a new name.", true);
            return;
        }

        if (existingIndex >= 0)
        {
            this.configuration.SavedRecipePlans[existingIndex] = savedPlan;
            this.loadedSavedPlan = savedPlan;
        }
        else
        {
            this.configuration.SavedRecipePlans.Add(savedPlan);
            this.loadedSavedPlan = savedPlan;
        }

        this.EnsureSavedPlanFolderHierarchy(savedPlan.FolderName);
        this.saveConfiguration();
        this.ShowPlanMessage(
            existingIndex >= 0
                ? $"Updated plan '{cleanName}'."
                : $"Saved plan '{cleanName}'.",
            false);
        this.ClearPlanSaveDraft(scope);
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
                Gatherables = plan.Gatherables.Select(CloneSavedSupplemental).ToList(),
                Collectables = plan.Collectables.Select(CloneSavedSupplemental).ToList(),
                DirectIngredients = plan.DirectIngredients.Select(CloneSavedSupplemental).ToList(),
                RawMaterials = plan.RawMaterials.Select(CloneSavedSupplemental).ToList(),
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
        this.QueueSavedPlanFolderForClosing(savedPlan.FolderName);
        this.loadedSavedPlan = savedPlan;
        this.selectedRecipes.Clear();
        this.selectedGatherables.Clear();
        this.selectedCollectables.Clear();
        this.selectedDirectIngredients.Clear();
        this.selectedRawMaterials.Clear();
        foreach (var savedRecipe in savedPlan.Recipes)
        {
            this.selectedRecipes.Add(new RecipePlanSelection(
                new RecipeMatch(
                    savedRecipe.RecipeId,
                    savedRecipe.ResultItemId,
                    savedRecipe.ResultName,
                    Math.Max(1, savedRecipe.ResultAmount),
                    savedRecipe.JobAbbreviations),
                Math.Max(1, savedRecipe.DesiredAmount)));
        }

        this.selectedGatherables.AddRange(CreateSavedSupplementalSelections(
            savedPlan.Gatherables ?? [],
            SearchResultKind.GatherableItem));
        this.selectedCollectables.AddRange(CreateSavedSupplementalSelections(
            savedPlan.Collectables ?? [],
            SearchResultKind.CollectibleItem));
        this.selectedDirectIngredients.AddRange(
            (savedPlan.DirectIngredients ?? []).Select(CloneSavedSupplemental));
        this.selectedRawMaterials.AddRange(
            (savedPlan.RawMaterials ?? []).Select(CloneSavedSupplemental));

        this.OpenSelectedRecipesSection();
        this.OpenCollectablesSectionForSelectedRecipes();
        this.OpenSelectedSupplementalSections();
        if (this.selectedDirectIngredients.Count > 0)
            this.autoOpenSectionIds.Add("direct-ingredients-section");
        if (this.selectedRawMaterials.Count > 0)
            this.autoOpenSectionIds.Add("raw-materials-section");
        this.ClearAllPlanSaveDrafts();
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

        foreach (var plan in plans)
            this.QueueSavedPlanFolderForClosing(plan.FolderName);
        this.loadedSavedPlan = null;

        var combinedRecipes = CreateSavedRecipeSelections(plans);
        var combinedGatherables = CreateSavedSupplementalSelections(
            plans.SelectMany(plan => plan.Gatherables ?? []),
            SearchResultKind.GatherableItem);
        var combinedCollectables = CreateSavedSupplementalSelections(
            plans.SelectMany(plan => plan.Collectables ?? []),
            SearchResultKind.CollectibleItem);
        var combinedDirectIngredients = CreateCombinedSavedIngredientEntries(
            plans.SelectMany(plan => plan.DirectIngredients ?? []));
        var combinedRawMaterials = CreateCombinedSavedIngredientEntries(
            plans.SelectMany(plan => plan.RawMaterials ?? []));
        if (combinedRecipes.Count == 0 &&
            combinedGatherables.Count == 0 &&
            combinedCollectables.Count == 0 &&
            combinedDirectIngredients.Count == 0 &&
            combinedRawMaterials.Count == 0)
        {
            this.ShowPlanMessage("The selected plans do not contain any saved items.", true);
            return;
        }

        this.selectedRecipes.Clear();
        this.selectedGatherables.Clear();
        this.selectedCollectables.Clear();
        this.selectedDirectIngredients.Clear();
        this.selectedRawMaterials.Clear();
        this.selectedRecipes.AddRange(combinedRecipes);
        this.selectedGatherables.AddRange(combinedGatherables);
        this.selectedCollectables.AddRange(combinedCollectables);
        this.selectedDirectIngredients.AddRange(combinedDirectIngredients);
        this.selectedRawMaterials.AddRange(combinedRawMaterials);
        this.OpenSelectedRecipesSection();
        this.OpenCollectablesSectionForSelectedRecipes();
        this.OpenSelectedSupplementalSections();
        if (this.selectedDirectIngredients.Count > 0)
            this.autoOpenSectionIds.Add("direct-ingredients-section");
        if (this.selectedRawMaterials.Count > 0)
            this.autoOpenSectionIds.Add("raw-materials-section");
        this.ClearAllPlanSaveDrafts();
        this.searchText = string.Empty;
        this.searchResults = [];
        this.RefreshDetails(true);
        this.ShowPlanMessage(
            $"Loaded {plans.Count} saved plan{(plans.Count == 1 ? string.Empty : "s")}.",
            false);

        if (!craftImmediately || combinedRecipes.Count == 0)
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
                false,
                out this.integrationMessage);
    }

    private void RefreshSelectedSavedPlanCraftAvailability()
    {
        if (!this.savedPlanCraftAvailabilityDirty)
            return;

        var plans = this.selectedSavedPlans
            .Where(plan => this.configuration.SavedRecipePlans.Contains(plan))
            .ToList();
        var recipes = CreateSavedRecipeSelections(plans);
        if (recipes.Count == 0)
        {
            this.canCraftSelectedSavedPlans = false;
        }
        else
        {
            var liveInventory = this.inventoryService.GetImmediatelyUsableItems();
            var details = this.recipeService.GetPlanDetails(recipes, liveInventory);
            this.canCraftSelectedSavedPlans =
                this.recipeService.TryBuildArtisanCraftQueue(details.Recipes, liveInventory, out _, out _);
        }

        this.savedPlanCraftAvailabilityDirty = false;
    }

    private void ClearSelectedPlansAndCurrentPlan()
    {
        this.selectedSavedPlans.Clear();
        this.savedPlanCraftAvailabilityDirty = true;
        this.loadedSavedPlan = null;
        this.selectedRecipes.Clear();
        this.selectedGatherables.Clear();
        this.selectedCollectables.Clear();
        this.selectedDirectIngredients.Clear();
        this.selectedRawMaterials.Clear();
        this.ClearAllPlanSaveDrafts();
        this.RefreshDetails(false);
        this.ShowPlanMessage("Cleared the selected plans and current Recipe Plan.", false);
    }

    private void QueueSavedPlanFolderForClosing(string? folderName)
    {
        var normalizedFolder = NormalizeFolderName(folderName);
        if (string.IsNullOrWhiteSpace(normalizedFolder))
            return;

        var parentFolder = GetParentFolderPath(normalizedFolder);
        this.savedPlanFoldersToClose.Add(
            string.IsNullOrWhiteSpace(parentFolder)
                ? normalizedFolder
                : parentFolder);
    }

    private static List<RecipePlanSelection> CreateSavedRecipeSelections(
        IEnumerable<SavedRecipePlan> plans) =>
        plans
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
                        Math.Max(1U, first.ResultAmount),
                        first.JobAbbreviations),
                    desiredAmount);
            })
            .OrderBy(selection => selection.Recipe.ResultName)
            .ToList();

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
                    this.configuration.TextColor,
                    showOverflowTooltip: false);
                var collectableRewardInfo = this.recipeService.GetCollectibleRewardInfo(recipe.ResultItemId);
                if (collectableRewardInfo is not null)
                {
                    this.DrawCollectibleRewardTooltip(
                        recipe.ResultName,
                        collectableRewardInfo,
                        this.GetRecipeUnlockTooltipLines(recipe.RecipeId),
                        recipe.DesiredAmount,
                        this.recipeService.GetFishTooltipInfo(recipe.ResultItemId),
                        this.recipeService.GetCosmicExplorationTooltipInfo(recipe.ResultItemId),
                        this.recipeService.GetQuestTooltipInfo(recipe.ResultItemId),
                        this.recipeService.GetRecipeLogStatusTooltipInfo(recipe.RecipeId, recipe.ResultItemId),
                        showTotalValue: true);
                }
                else
                {
                    MaterialUsageTooltip.Draw(
                        this.marketboardPriceService,
                        this.configuration,
                        recipe.ResultItemId,
                        recipe.ResultName,
                        quantity: recipe.DesiredAmount,
                        detailLines: this.GetRecipeUnlockTooltipLines(recipe.RecipeId),
                        specialContentTooltipInfo: this.GetSpecialContentTooltipInfo(recipe.ResultItemId),
                        fishTooltipInfo: this.recipeService.GetFishTooltipInfo(recipe.ResultItemId),
                        societyQuestTooltipInfo: this.recipeService.GetSocietyQuestTooltipInfo(recipe.ResultItemId),
                        cosmicExplorationTooltipInfo: this.recipeService.GetCosmicExplorationTooltipInfo(recipe.ResultItemId),
                        questTooltipInfo: this.recipeService.GetQuestTooltipInfo(recipe.ResultItemId),
                        logStatusTooltipInfo: this.recipeService.GetRecipeLogStatusTooltipInfo(recipe.RecipeId, recipe.ResultItemId),
                        aetherialReductionSources: this.recipeService.GetAetherialReductionSources(recipe.ResultItemId),
                        isMarketboardAvailable: this.recipeService.IsMarketboardAvailable(recipe.ResultItemId));
                }

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
                var canCraftRecipeFromLiveInventory =
                    this.recipeService.TryBuildArtisanCraftQueue(
                        [recipe],
                        this.inventoryService.GetImmediatelyUsableItems(),
                        out _,
                        out _);
                ImGui.BeginDisabled(!canCraftRecipeFromLiveInventory);
                if (WindowTheme.ShadowedButton($"Craft Items##plan-artisan-{recipe.RecipeId}", new Vector2(craftButtonWidth, 0)))
                {
                    this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                        new ArtisanCraftQueueEntry(
                            recipe.RecipeId,
                            recipe.ResultItemId,
                            recipe.ResultName,
                            recipe.ResultAmount,
                            recipe.CraftCount,
                            false),
                        out this.integrationMessage);
                }
                ImGui.EndDisabled();
                DrawTooltipIfHovered(
                    canCraftRecipeFromLiveInventory
                        ? "Open this recipe in Artisan with the required craft count."
                        : "Only activates when all materials for this recipe are already in inventory. Use Refresh Inventory if needed.",
                    allowWhenDisabled: true);

                ImGui.SameLine();
                if (WindowTheme.ShadowedButton($"Teamcraft##plan-teamcraft-{recipe.RecipeId}", new Vector2(teamcraftButtonWidth, 0)))
                {
                    this.integrationError = !this.pluginIntegrationService.OpenInTeamcraft(
                        recipe.ResultItemId,
                        recipe.DesiredAmount,
                        out this.integrationMessage);
                }
                DrawTooltipIfHovered("Open this recipe as a Teamcraft import list.");

                ImGui.SameLine();
                if (WindowTheme.ShadowedButton($"Remove##plan-remove-{recipe.RecipeId}", new Vector2(removeButtonWidth, 0)))
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

    private void ToggleObtainedRawMaterials()
    {
        this.configuration.ShowObtainedRawMaterials = !this.configuration.ShowObtainedRawMaterials;
        this.saveConfiguration();
    }

    private void ToggleObtainedElementalCatalysts()
    {
        this.configuration.ShowObtainedElementalCatalysts = !this.configuration.ShowObtainedElementalCatalysts;
        this.saveConfiguration();
    }

    private bool DrawCollapsibleSection(
        string title,
        string subtitle,
        string sectionId,
        string? actionLabel = null,
        Action? action = null,
        bool defaultOpen = true)
    {
        ImGui.Spacing();
        if (this.autoOpenSectionIds.Remove(sectionId))
        {
            ImGui.SetNextItemOpen(true, ImGuiCond.Always);
        }
        else if (this.autoCloseSectionIds.Remove(sectionId))
        {
            ImGui.SetNextItemOpen(false, ImGuiCond.Always);
        }
        else if (!defaultOpen)
        {
            ImGui.SetNextItemOpen(false, ImGuiCond.Appearing);
        }
        ImGui.PushStyleColor(ImGuiCol.Text, this.configuration.SectionHeaderTextColor);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, this.ScaleUi(10f));
        var isOpen = ImGui.CollapsingHeader(
            $"{title}##{sectionId}",
            defaultOpen ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();

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
                var availabilityText = this.GetIngredientAvailabilityText(material, reductionSource);
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
        {
            var reductionSource = this.aetherialReductionService.GetPreferredSource(material.ReductionSources);
            return !string.IsNullOrWhiteSpace(this.GetIngredientAvailabilityText(material, reductionSource));
        });
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
            (showFoundIn ? this.ScaleUi(210f) : 0f) +
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
                SetupColumn("Found in", "found", this.ScaleUi(210f));
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
                    ingredient.ItemId,
                    ingredient.Name,
                    detailLines: this.GetItemUnlockTooltipLines(ingredient.ItemId),
                    specialContentTooltipInfo: this.GetSpecialContentTooltipInfo(ingredient.ItemId),
                    fishTooltipInfo: this.recipeService.GetFishTooltipInfo(ingredient.ItemId),
                    societyQuestTooltipInfo: this.recipeService.GetSocietyQuestTooltipInfo(ingredient.ItemId),
                    cosmicExplorationTooltipInfo: this.recipeService.GetCosmicExplorationTooltipInfo(ingredient.ItemId),
                    questTooltipInfo: this.recipeService.GetQuestTooltipInfo(ingredient.ItemId),
                    logStatusTooltipInfo: this.recipeService.GetItemLogStatusTooltipInfo(ingredient.ItemId),
                    aetherialReductionSources: this.recipeService.GetAetherialReductionSources(ingredient.ItemId),
                    isMarketboardAvailable: this.recipeService.IsMarketboardAvailable(ingredient.ItemId));

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
                var availabilityText = this.GetIngredientAvailabilityText(ingredient, reductionSource);
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
                        if (WindowTheme.ShadowedButton($"Gather##{tableId}-{ingredient.ItemId}", new Vector2(buttonWidth, 0)))
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
                    this.DrawAvailabilityCard(tableId, ingredient, availabilityText);
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
                            locationsText,
                            AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                            this.configuration.TextColor);
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
                            WithAlpha(this.configuration.ButtonColor, 0.16f));
                        var cardMin = ImGui.GetItemRectMin();
                        var buttonSize = this.ScaleUi(new Vector2(96f, 26f));
                        ImGui.SetCursorScreenPos(new Vector2(
                            cardMin.X + MathF.Max(0f, (cardSize.X - buttonSize.X) / 2f),
                            cardMin.Y + MathF.Max(0f, (cardSize.Y - buttonSize.Y) / 2f)));
                        var readyToCraftClicked =
                            WindowTheme.ShadowedButton($"Ready to craft##{tableId}-raw-{ingredient.ItemId}", buttonSize);
                        if (readyToCraftClicked)
                        {
                            var trackedRecipe = this.artisanCraftQueue.FirstOrDefault(recipe =>
                                recipe.RecipeId == rawCraftRecipeId &&
                                recipe.CraftCount == ingredient.RawCraftCount);
                            this.integrationError = !this.pluginIntegrationService.CraftWithArtisan(
                                trackedRecipe ??
                                new ArtisanCraftQueueEntry(
                                    rawCraftRecipeId,
                                    ingredient.ItemId,
                                    ingredient.Name,
                                    1,
                                    ingredient.RawCraftCount,
                                    true),
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

    private void DrawHeaderCard(string label)
    {
        var size = new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), this.ScaleUi(28f));
        var position = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(
            position,
            position + size,
            ImGui.GetColorU32(WithAlpha(AdjustColor(this.configuration.AccentColor, 0.04f), 0.90f)),
            this.ScaleUi(10f));
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
        Vector4 textColor,
        bool showOverflowTooltip = true)
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
        if (showOverflowTooltip &&
            (title.Length > safeTitle.Length || subtitle.Length > safeSubtitle.Length) &&
            ImGui.IsItemHovered())
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

        var displayValue = TrimDisplayTextToWidth(
            value,
            Math.Max(1f, resolvedSize.X - this.ScaleUi(16f)));
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
        string? timerText)
    {
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
        var warningSeconds = GetAvailabilityWarningSeconds(timerText);
        var isCritical = warningSeconds is >= 0 and <= 60;
        var isImminent = !isCritical && warningSeconds is > 60 and <= 120;
        var pulseAlpha = 0.18f + (((float)Math.Sin(ImGui.GetTime() * 8f) + 1f) * 0.08f);
        var size = this.ResolveCardSize(new Vector2(-1, this.ScaleUi(36f)), 36f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"{tableId}-available-{ingredient.ItemId}", size);
        var drawList = ImGui.GetWindowDrawList();
        var backgroundColor = isAvailableNow
            ? isCritical
                ? WithAlpha(this.configuration.MissingTextColor, pulseAlpha)
            : isImminent
                ? WithAlpha(this.configuration.WarningTextColor, pulseAlpha)
                : WithAlpha(this.configuration.SuccessTextColor, 0.20f)
            : isCritical
                ? WithAlpha(this.configuration.MissingTextColor, pulseAlpha)
            : isImminent
                ? WithAlpha(this.configuration.WarningTextColor, pulseAlpha)
                : WithAlpha(this.configuration.AccentColor, 0.16f);
        var textColor = isAvailableNow
            ? isCritical
                ? this.configuration.MissingTextColor
            : isImminent
                ? this.configuration.WarningTextColor
                : this.configuration.SuccessTextColor
            : isCritical
                ? this.configuration.MissingTextColor
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
        timerText = NormalizeAvailabilityText(timerText);
        var normalized = NormalizeAvailabilityText(timerText);
        if (normalized.StartsWith("Now", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (!normalized.StartsWith("In ", StringComparison.OrdinalIgnoreCase))
            return -1;

        return ParseDurationSeconds(normalized[3..].Trim());
    }

    private static int GetAvailabilityWarningSeconds(string timerText)
    {
        timerText = NormalizeAvailabilityText(timerText);
        var normalized = timerText.Replace("Ãƒâ€šÃ‚Â·", "-").Replace("Ã‚Â·", "-").Trim();
        if (normalized.StartsWith("Now", StringComparison.OrdinalIgnoreCase))
        {
            var leftIndex = normalized.LastIndexOf(" left", StringComparison.OrdinalIgnoreCase);
            if (leftIndex <= 3)
                return -1;

            var remainingText = normalized[3..leftIndex].Trim().TrimStart('-', ' ');
            return ParseDurationSeconds(remainingText);
        }

        return GetAvailabilityWaitSeconds(normalized);
    }

    private static string NormalizeAvailabilityText(string timerText) =>
        timerText
            .Replace("ÃƒÆ’Ã¢â‚¬Å¡Ãƒâ€šÃ‚Â·", "-")
            .Replace("Ãƒâ€šÃ‚Â·", "-")
            .Replace("Ã‚Â·", "-")
            .Replace("Â·", "-")
            .Trim();

    private static int ParseDurationSeconds(string durationText)
    {
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

    private string? GetIngredientAvailabilityText(
        IngredientNeed ingredient,
        AetherialReductionSource? reductionSource)
    {
        var fishItemId = reductionSource is { IsFishing: true }
            ? reductionSource.ItemId
            : ingredient.IsFishing
                ? ingredient.ItemId
                : 0;
        if (fishItemId != 0 && this.recipeService.GetGatherBuddyFishAvailabilityText(fishItemId) is { } fishAvailability)
            return fishAvailability;

        return reductionSource is not null
            ? this.aetherialReductionService.GetTimerText(reductionSource)
            : this.aetherialReductionService.GetGatheringTimerText(ingredient.ItemId);
    }

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
                    if (WindowTheme.ShadowedButton("Show map"))
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

                if (WindowTheme.ShadowedButton("Teleport"))
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
        if (WindowTheme.ShadowedButton("Close"))
        {
            this.isTravelPopupOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private void OnInventoryChanged()
    {
        this.inventoryRefreshRequested = true;
        this.savedPlanCraftAvailabilityDirty = true;
    }

    private void DrawTooltipIfHovered(string text, bool allowWhenDisabled = false)
    {
        var hovered = allowWhenDisabled
            ? ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)
            : ImGui.IsItemHovered();
        if (!hovered)
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
