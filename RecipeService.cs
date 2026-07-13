using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace DalamudRecipeHelper;

public sealed class RecipeService
{
    private const uint CrystalInventoryCap = 9999;
    private static readonly IReadOnlyDictionary<uint, string> CraftingJobAbbreviationsByCraftTypeId =
        new Dictionary<uint, string>
        {
            [0] = "CRP",
            [1] = "BSM",
            [2] = "ARM",
            [3] = "GSM",
            [4] = "LTW",
            [5] = "WVR",
            [6] = "ALC",
            [7] = "CUL",
        };

    private static readonly IReadOnlyDictionary<string, string> CraftingJobAbbreviations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Carpenter"] = "CRP",
            ["Blacksmith"] = "BSM",
            ["Armorer"] = "ARM",
            ["Goldsmith"] = "GSM",
            ["Leatherworker"] = "LTW",
            ["Weaver"] = "WVR",
            ["Alchemist"] = "ALC",
            ["Culinarian"] = "CUL",
        };

    // Quest-only fish can omit their fishing hole from the client data. These entries
    // preserve the actual fishing destination instead of showing the quest giver's location.
    private static readonly IReadOnlyDictionary<uint, FishTooltipInfo> QuestFishFallbackInfoByItemId =
        new Dictionary<uint, FishTooltipInfo>
        {
            [17949] = new("Salmon Roe", "Regular fish", "The Fringes", "Velodyna River"),
            [17950] = new("Metal Spinner", "Regular fish", "The Ruby Sea", "Shoal Rock"),
            [17952] = new("Live Shrimp", "Regular fish", "The Ruby Sea", "Onokoro"),
            [26750] = new("Fruit Worm", "Regular fish", "Il Mheg", "Longmirror Lake"),
            [26751] = new("Moyebi Shrimp", "Regular fish", "The Tempest", "The Norvrandt Slope"),
            [35849] = new("Gold Salmon Roe", "Regular fish", "Thavnair", "River west of Palaka's Stand"),
            [35850] = new("Grey Worm", "Regular fish", "Mare Lamentorum", "Frozen Fissure"),
            [43915] = new("Honeybee", "Regular fish", "Yak T'el", "Bitterbark Cenote"),
            [43916] = new("Popper Lure", "Regular fish", "Heritage Found", "Submerged Skyline"),
        };

    private readonly IDataManager dataManager;
    private readonly FileLogService fileLog;
    private readonly AetherialReductionService aetherialReductionService;
    private HashSet<uint>? craftableItemIds;
    private HashSet<uint>? gatherableItemIds;
    private HashSet<uint>? fishingItemIds;
    private HashSet<uint>? vendorItemIds;
    private HashSet<uint>? marketboardItemIds;
    private HashSet<uint>? excludedSearchItemIds;
    private IReadOnlyDictionary<uint, string>? gatherableJobsByItemId;
    private IReadOnlyDictionary<uint, IReadOnlyList<uint>>? gatheringLevelsByItemId;
    private IReadOnlyDictionary<uint, uint>? recipeLevelsByRecipeId;
    private IReadOnlyDictionary<uint, IReadOnlyList<uint>>? recipeIdsByResultItemId;
    private IReadOnlyDictionary<uint, CollectibleRewardInfo>? collectibleRewardsByItemId;
    private IReadOnlyDictionary<uint, FolkloreBookInfo>? folkloreBookInfoByItemId;
    private IReadOnlyDictionary<uint, RequiredItemInfo>? requiredItemsByItemId;
    private IReadOnlyDictionary<uint, FishTooltipInfo>? fishTooltipInfoByItemId;
    private IReadOnlyDictionary<uint, IReadOnlyList<ItemLogEntry>>? itemLogEntriesByItemId;
    private IReadOnlyDictionary<uint, SocietyQuestTooltipInfo>? societyQuestTooltipInfoByItemId;
    private IReadOnlyDictionary<uint, CosmicExplorationTooltipInfo>? cosmicExplorationTooltipInfoByItemId;
    private IReadOnlyDictionary<uint, QuestTooltipInfo>? questTooltipInfoByItemId;
    private IReadOnlyDictionary<uint, GatherBuddyFishTooltipData>? gatherBuddyFishTooltipData;
    private DateTime nextGatherBuddyFishTooltipRetryUtc;
    private IReadOnlyDictionary<uint, SpecialContentTooltipInfo>? specialContentTooltipInfoByItemId;
    private IReadOnlyDictionary<uint, MasterRecipeBookInfo>? masterRecipeBookInfoByRecipeId;
    private IReadOnlyDictionary<uint, IReadOnlyList<MaterialRecipeUsage>>? recipesByIngredient;
    private IReadOnlyList<RecipeMatch>? browseAllSearchResults;

    public RecipeService(
        IDataManager dataManager,
        FileLogService fileLog,
        AetherialReductionService aetherialReductionService)
    {
        this.dataManager = dataManager;
        this.fileLog = fileLog;
        this.aetherialReductionService = aetherialReductionService;
    }

    public IReadOnlyList<RecipeMatch> Search(string query)
    {
        this.EnsureItemSources();
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        var items = this.dataManager.GetExcelSheet<Item>();
        if (recipes is null || items is null)
            return [];

        var cleanQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(cleanQuery))
            return [];

        var recipeResults = recipes
            .Select(this.TryCreateMatch)
            .Where(match => match is not null)
            .Select(match => match!)
            .Where(match => this.MatchesSearchQuery(match, cleanQuery))
            .ToList();
        var supplementalResults = items
            .Where(item =>
                item.RowId != 0 &&
                !recipeResults.Any(result => result.ResultItemId == item.RowId) &&
                this.IsSupplementalSearchItem(item.RowId))
            .Select(item => this.CreateSupplementalMatch(item))
            .Where(match => !string.IsNullOrWhiteSpace(match.ResultName))
            .Where(match => this.MatchesSearchQuery(match, cleanQuery))
            .OrderBy(match => GetSearchKindSortOrder(match.ResultKind))
            .ToList();
        var results = recipeResults
            .Concat(supplementalResults)
            .OrderBy(match => match.ResultName.StartsWith(cleanQuery, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(match => match.ResultName)
            .ThenBy(match => GetSearchKindSortOrder(match.ResultKind))
            .ToList();
        this.fileLog.Info(
            "Recipes",
            $"Search '{cleanQuery}' returned {results.Count} result(s): {recipeResults.Count} recipe and {supplementalResults.Count} gatherable/collectible.");
        return results;
    }

    public IReadOnlyList<RecipeMatch> BrowseAllSearchResults()
    {
        if (this.browseAllSearchResults is not null)
            return this.browseAllSearchResults;

        this.EnsureItemSources();
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        var items = this.dataManager.GetExcelSheet<Item>();
        if (recipes is null || items is null)
            return [];

        var recipeResults = recipes
            .Select(this.TryCreateMatch)
            .Where(match => match is not null)
            .Select(match => match!)
            .ToList();
        var supplementalResults = items
            .Where(item =>
                item.RowId != 0 &&
                !recipeResults.Any(result => result.ResultItemId == item.RowId) &&
                this.IsSupplementalSearchItem(item.RowId))
            .Select(item => this.CreateSupplementalMatch(item))
            .Where(match => !string.IsNullOrWhiteSpace(match.ResultName))
            .OrderBy(match => GetSearchKindSortOrder(match.ResultKind))
            .ToList();
        this.browseAllSearchResults = recipeResults
            .Concat(supplementalResults)
            .OrderBy(match => match.ResultName)
            .ThenBy(match => GetSearchKindSortOrder(match.ResultKind))
            .ToList();
        return this.browseAllSearchResults;
    }

    public IReadOnlyList<CraftableRecipeAvailability> GetCraftableRecipes(
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        this.EnsureItemSources();
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is null || ownedItems.Count == 0)
            return [];

        var recipeRows = recipes
            .Select(recipe => (Recipe: recipe, Match: this.TryCreateMatch(recipe)))
            .Where(entry =>
                entry.Match is not null &&
                this.ReadIngredients(entry.Recipe).Count > 0)
            .ToList();
        var recipesByResult = this.BuildRecipesByResult(
            recipeRows.Select(entry => entry.Recipe));
        var available = recipeRows
            .Select(entry =>
            {
                var craftCount = this.GetMaximumCraftCount(
                    entry.Recipe,
                    recipesByResult,
                    ownedItems);
                return new CraftableRecipeAvailability(
                    entry.Match!,
                    craftCount,
                    MultiplySaturating(
                        (ulong)entry.Match!.ResultAmount,
                        craftCount));
            })
            .Where(availability => availability.CraftCount > 0)
            .GroupBy(availability => availability.Recipe.ResultItemId)
            .Select(group =>
            {
                var selectedAvailability = group
                    .OrderByDescending(availability => availability.CraftCount)
                    .ThenBy(availability => availability.Recipe.RecipeId)
                    .First();
                var combinedJobs = string.Join(
                    " / ",
                    group.SelectMany(availability => SplitJobAbbreviations(availability.Recipe.JobAbbreviations))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(static job => job));
                return string.IsNullOrWhiteSpace(combinedJobs)
                    ? selectedAvailability
                    : selectedAvailability with
                    {
                        Recipe = selectedAvailability.Recipe with
                        {
                            JobAbbreviations = combinedJobs,
                        },
                    };
            })
            .OrderBy(availability => availability.Recipe.ResultName)
            .ThenBy(availability => availability.Recipe.RecipeId)
            .ToList();

        this.fileLog.Info(
            "Recipes",
            $"Stock check found {available.Count} craftable result(s) from {ownedItems.Count} owned item type(s).");
        return available;
    }

    private uint GetMaximumCraftCount(
        Recipe recipe,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        if (!this.CanCraftFromOwnedItems(recipe, 1, recipesByResult, ownedItems))
            return 0;

        uint lower = 1;
        uint upper = 2;
        while (this.CanCraftFromOwnedItems(recipe, upper, recipesByResult, ownedItems))
        {
            lower = upper;
            if (upper == uint.MaxValue)
                return upper;

            upper = upper > uint.MaxValue / 2
                ? uint.MaxValue
                : upper * 2;
        }

        while ((ulong)lower + 1 < upper)
        {
            var middle = (uint)(((ulong)lower + upper) / 2);
            if (this.CanCraftFromOwnedItems(recipe, middle, recipesByResult, ownedItems))
                lower = middle;
            else
                upper = middle;
        }

        return lower;
    }

    public RecipeDetails? GetDetails(
        RecipeMatch match,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        uint desiredAmount)
    {
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is null)
            return null;

        var recipe = recipes.FirstOrDefault(row => row.RowId == match.RecipeId);
        if (recipe.RowId != match.RecipeId)
            return null;

        desiredAmount = Math.Max(1, desiredAmount);
        var resultAmount = Math.Max(1, match.ResultAmount);
        var craftCount = (uint)Math.Min(
            ((ulong)desiredAmount + resultAmount - 1) / resultAmount,
            uint.MaxValue);
        var producedAmount = MultiplySaturating(resultAmount, craftCount);

        var recipesByResult = this.BuildRecipesByResult(recipes);
        var scaledIngredients = this.ReadIngredients(recipe)
            .Select(ingredient => (ingredient.ItemId, MultiplySaturating(ingredient.Amount, craftCount)));
        var ingredients = this.AddRawCraftAvailability(
            this.CreateIngredientNeeds(scaledIngredients, ownedItems),
            recipesByResult,
            ownedItems);
        var rawMaterials = this.GetRawMaterials(recipe, recipesByResult, ownedItems, craftCount);
        this.fileLog.Info(
            "Recipes",
            $"Calculated recipe {match.RecipeId} ({match.ResultName}), requested {desiredAmount}, crafts {craftCount}, direct materials {ingredients.Count}, craftable from owned raw materials {ingredients.Count(item => item.CanCraftMissingFromRaw is true)}, raw materials {rawMaterials.Count}.");

        return new RecipeDetails(
            match.RecipeId,
            match.ResultItemId,
            match.ResultName,
            match.ResultAmount,
            desiredAmount,
            craftCount,
            producedAmount,
            ingredients,
            rawMaterials,
            ingredients.Count == 0 ? CreateRecipeDebugInfo(recipe) : string.Empty);
    }

    public IngredientNeed? GetStandaloneIngredientNeed(
        uint itemId,
        uint desiredAmount,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        if (itemId == 0 || desiredAmount == 0)
            return null;

        return this.CreateIngredientNeeds(
                [(itemId, desiredAmount)],
                ownedItems)
            .FirstOrDefault();
    }

    public CollectibleRewardInfo? GetCollectibleRewardInfo(uint itemId)
    {
        this.EnsureItemSources();
        return this.collectibleRewardsByItemId!.TryGetValue(itemId, out var rewardInfo)
            ? rewardInfo
            : null;
    }

    public FolkloreBookInfo? GetFolkloreBookInfo(uint itemId)
    {
        this.EnsureItemSources();
        return this.folkloreBookInfoByItemId!.TryGetValue(itemId, out var folkloreBookInfo)
            ? folkloreBookInfo
            : null;
    }

    public string GetGatheringJobDisplayLabel(uint itemId, string defaultJobAbbreviations)
    {
        this.EnsureItemSources();
        if (!this.gatheringLevelsByItemId!.TryGetValue(itemId, out var levels) || levels.Count == 0)
            return defaultJobAbbreviations;

        var levelLabel = levels.Count == 1
            ? $"Lv {levels[0]}"
            : $"Lv {string.Join("/", levels)}";
        return string.IsNullOrWhiteSpace(defaultJobAbbreviations)
            ? levelLabel
            : $"{defaultJobAbbreviations} {levelLabel}";
    }

    public RequiredItemInfo? GetRequiredItemInfo(uint itemId)
    {
        this.EnsureItemSources();
        return this.requiredItemsByItemId!.TryGetValue(itemId, out var requiredItemInfo)
            ? requiredItemInfo
            : null;
    }

    public FishTooltipInfo? GetFishTooltipInfo(uint itemId)
    {
        this.EnsureItemSources();
        if (!this.fishTooltipInfoByItemId!.TryGetValue(itemId, out var fishTooltipInfo))
            return null;

        var gatherBuddyFishInfo = this.GetGatherBuddyFishTooltipData().GetValueOrDefault(itemId);
        return gatherBuddyFishInfo is null
            ? fishTooltipInfo
            : fishTooltipInfo with
            {
                BaitName = string.IsNullOrWhiteSpace(gatherBuddyFishInfo.BaitName)
                    ? fishTooltipInfo.BaitName
                    : gatherBuddyFishInfo.BaitName,
                FishType = string.IsNullOrWhiteSpace(gatherBuddyFishInfo.FishType)
                    ? fishTooltipInfo.FishType
                    : gatherBuddyFishInfo.FishType,
            };
    }

    public IReadOnlyList<AetherialReductionSource> GetAetherialReductionSources(uint itemId) =>
        this.aetherialReductionService.GetSources(itemId);

    public unsafe LogStatusTooltipInfo? GetItemLogStatusTooltipInfo(uint itemId)
    {
        this.EnsureItemSources();
        if (!this.itemLogEntriesByItemId!.TryGetValue(itemId, out var logEntries))
            return null;
        if (this.IsItemLogStatusExcluded(itemId))
            return null;

        try
        {
            var playerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            if (playerState is null)
                return null;

            var obtainedByLogLabel = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var logEntry in logEntries)
            {
                var isRecorded = logEntry.Kind switch
                {
                    ItemLogKind.Gathering =>
                        logEntry.EntryId <= ushort.MaxValue &&
                        QuestManager.IsGatheringItemGathered((ushort)logEntry.EntryId),
                    ItemLogKind.Fishing => playerState is not null && playerState->IsFishCaught(logEntry.EntryId),
                    ItemLogKind.Spearfishing => playerState is not null && playerState->IsSpearfishCaught(logEntry.EntryId),
                    _ => false,
                };
                obtainedByLogLabel[logEntry.Label] =
                    obtainedByLogLabel.GetValueOrDefault(logEntry.Label) || isRecorded;
            }

            return new LogStatusTooltipInfo(obtainedByLogLabel
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => $"{entry.Key}: {(entry.Value ? "Obtained" : "Not yet obtained")}")
                .ToList());
        }
        catch
        {
            return null;
        }
    }

    public unsafe bool? IsItemObtainedInLog(uint itemId)
    {
        this.EnsureItemSources();
        if (!this.itemLogEntriesByItemId!.TryGetValue(itemId, out var logEntries))
            return null;
        if (this.IsItemLogStatusExcluded(itemId))
            return null;

        try
        {
            var playerState = FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance();
            if (playerState is null)
                return null;
            foreach (var logEntry in logEntries)
            {
                var isRecorded = logEntry.Kind switch
                {
                    ItemLogKind.Gathering =>
                        logEntry.EntryId <= ushort.MaxValue &&
                        QuestManager.IsGatheringItemGathered((ushort)logEntry.EntryId),
                    ItemLogKind.Fishing => playerState is not null && playerState->IsFishCaught(logEntry.EntryId),
                    ItemLogKind.Spearfishing => playerState is not null && playerState->IsSpearfishCaught(logEntry.EntryId),
                    _ => false,
                };
                if (isRecorded)
                    return true;
            }

            return false;
        }
        catch
        {
            return null;
        }
    }

    private bool IsItemLogStatusExcluded(uint itemId) =>
        this.cosmicExplorationTooltipInfoByItemId!.ContainsKey(itemId) ||
        this.questTooltipInfoByItemId!.ContainsKey(itemId) ||
        this.societyQuestTooltipInfoByItemId!.ContainsKey(itemId) ||
        (this.requiredItemsByItemId!.TryGetValue(itemId, out var requiredItemInfo) && requiredItemInfo.IsTool);

    public unsafe LogStatusTooltipInfo? GetRecipeLogStatusTooltipInfo(uint recipeId, uint resultItemId = 0)
    {
        this.EnsureItemSources();
        if (!this.recipeLevelsByRecipeId!.ContainsKey(recipeId) ||
            this.masterRecipeBookInfoByRecipeId!.ContainsKey(recipeId))
            return null;

        try
        {
            if (FFXIVClientStructs.FFXIV.Client.Game.UI.PlayerState.Instance() is null)
                return null;

            var recipeIds = resultItemId != 0
                ? this.recipeIdsByResultItemId!.GetValueOrDefault(resultItemId) ?? []
                : [];
            var hasCraftedRecipe = recipeIds
                .Append(recipeId)
                .Distinct()
                .Any(QuestManager.IsRecipeComplete);
            // The client only exposes a positive completion flag. Older and some
            // special recipes can have no flag even when the item was crafted.
            return hasCraftedRecipe
                ? new LogStatusTooltipInfo(["Crafting log: Crafted"])
                : null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsMarketboardAvailable(uint itemId)
    {
        this.EnsureItemSources();
        return this.marketboardItemIds!.Contains(itemId);
    }

    public SpecialContentTooltipInfo? GetSpecialContentTooltipInfo(uint itemId)
    {
        this.EnsureItemSources();
        return this.specialContentTooltipInfoByItemId!.TryGetValue(itemId, out var specialContentTooltipInfo)
            ? specialContentTooltipInfo
            : null;
    }

    public SocietyQuestTooltipInfo? GetSocietyQuestTooltipInfo(uint itemId)
    {
        this.EnsureItemSources();
        return this.societyQuestTooltipInfoByItemId!.TryGetValue(itemId, out var societyQuestTooltipInfo)
            ? societyQuestTooltipInfo
            : null;
    }

    public CosmicExplorationTooltipInfo? GetCosmicExplorationTooltipInfo(uint itemId)
    {
        this.EnsureItemSources();
        if (!this.cosmicExplorationTooltipInfoByItemId!.TryGetValue(itemId, out var cosmicExplorationTooltipInfo))
            return null;

        var cosmicMissionName = this.GetGatherBuddyFishTooltipData().GetValueOrDefault(itemId)?.CosmicMissionName;
        return string.IsNullOrWhiteSpace(cosmicMissionName)
            ? cosmicExplorationTooltipInfo
            : cosmicExplorationTooltipInfo with { MissionName = cosmicMissionName };
    }

    public string? GetGatherBuddyFishAvailabilityText(uint itemId)
    {
        try
        {
            var gatherBuddyType = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly =>
                    string.Equals(assembly.GetName().Name, "GatherBuddy", StringComparison.Ordinal))?
                .GetType("GatherBuddy.GatherBuddy");
            var gameData = gatherBuddyType?
                .GetProperty("GameData", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null);
            var uptimeManager = gatherBuddyType?
                .GetProperty("UptimeManager", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null);
            var seTime = gatherBuddyType?
                .GetProperty("Time", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null);
            var serverTime = seTime?
                .GetType()
                .GetProperty("ServerTime", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(seTime);
            var fishes = gameData?
                .GetType()
                .GetProperty("Fishes", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(gameData) as IEnumerable;
            var fish = fishes is null ? null : GetGatherBuddyFish(fishes, itemId);
            if (uptimeManager is null || serverTime is null || fish is null)
                return null;

            var bestLocation = uptimeManager
                .GetType()
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .FirstOrDefault(method => method.Name == "BestLocation" && method.GetParameters().Length == 1)?
                .Invoke(uptimeManager, [fish]);
            var interval = bestLocation?
                .GetType()
                .GetField("Item2")?
                .GetValue(bestLocation);
            if (interval is null)
                return null;

            if (serverTime.GetType().GetProperty("Time", BindingFlags.Public | BindingFlags.Instance)?.GetValue(serverTime) is not long now ||
                interval.GetType().GetProperty("Start", BindingFlags.Public | BindingFlags.Instance)?.GetValue(interval) is not { } start ||
                interval.GetType().GetProperty("End", BindingFlags.Public | BindingFlags.Instance)?.GetValue(interval) is not { } end ||
                start.GetType().GetProperty("Time", BindingFlags.Public | BindingFlags.Instance)?.GetValue(start) is not long startTime ||
                end.GetType().GetProperty("Time", BindingFlags.Public | BindingFlags.Instance)?.GetValue(end) is not long endTime)
                return null;

            if (startTime == long.MinValue && endTime == long.MaxValue)
                return "Always Up";
            if (endTime <= now || endTime <= startTime)
                return "Check GatherBuddy";
            if (startTime <= now)
                return $"Now - {FormatGatherBuddyDuration(endTime - now)} left";

            return $"In {FormatGatherBuddyDuration(startTime - now)}";
        }
        catch
        {
            return null;
        }
    }

    public QuestTooltipInfo? GetQuestTooltipInfo(uint itemId)
    {
        this.EnsureItemSources();
        return this.questTooltipInfoByItemId!.TryGetValue(itemId, out var questTooltipInfo)
            ? questTooltipInfo
            : null;
    }

    public string GetRecipeJobDisplayLabel(uint recipeId, string defaultJobAbbreviations)
    {
        this.EnsureItemSources();
        if (!this.recipeLevelsByRecipeId!.TryGetValue(recipeId, out var level) || level == 0)
            return defaultJobAbbreviations;

        var levelLabel = $"Lv {level}";
        return string.IsNullOrWhiteSpace(defaultJobAbbreviations)
            ? levelLabel
            : $"{defaultJobAbbreviations} {levelLabel}";
    }

    public MasterRecipeBookInfo? GetMasterRecipeBookInfo(uint recipeId)
    {
        this.EnsureItemSources();
        return this.masterRecipeBookInfoByRecipeId!.TryGetValue(recipeId, out var masterRecipeBookInfo)
            ? masterRecipeBookInfo
            : null;
    }

    public RecipePlanDetails GetPlanDetails(
        IReadOnlyList<RecipePlanSelection> selections,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        var recipeDetails = selections
            .Select(selection => this.GetDetails(
                selection.Recipe,
                ownedItems,
                selection.DesiredAmount))
            .Where(details => details is not null)
            .Select(details => details!)
            .ToList();

        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        var recipesByResult = recipes is null
            ? new Dictionary<uint, Recipe>()
            : this.BuildRecipesByResult(recipes);
        var ingredients = this.AddRawCraftAvailability(
            this.CombineMaterialRequirements(
                recipeDetails.SelectMany(details => details.Ingredients),
                ownedItems),
            recipesByResult,
            ownedItems);
        var rawMaterials = this.GetRawMaterialsForRequirements(
            ingredients.Select(ingredient => (ingredient.ItemId, (ulong)ingredient.Required)),
            recipesByResult,
            ownedItems);

        this.fileLog.Info(
            "Recipes",
            $"Calculated combined plan with {recipeDetails.Count} recipe(s), {ingredients.Count} direct material(s), and {rawMaterials.Count} raw material(s).");
        return new RecipePlanDetails(recipeDetails, ingredients, rawMaterials);
    }

    public bool TryBuildDreamTargets(
        RecipePlanDetails details,
        IReadOnlyDictionary<uint, OwnedInventoryItem> liveOwnedItems,
        IReadOnlyList<StoredRetainerInventory> storedRetainers,
        out IReadOnlyList<RetainerWithdrawalTarget> targets,
        out string error)
    {
        targets = [];
        error = string.Empty;

        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is null)
        {
            error = "Recipe data is not available.";
            return false;
        }

        var recipesByResult = this.BuildRecipesByResult(recipes);
        var liveStock = liveOwnedItems.ToDictionary(
            entry => entry.Key,
            entry => (ulong)entry.Value.Quantity);
        var retainerStates = storedRetainers
            .Select(retainer => new RetainerStockState(retainer))
            .ToList();
        var plannedTargets = new List<RetainerWithdrawalTarget>();
        foreach (var ingredient in details.Ingredients
                     .GroupBy(ingredient => ingredient.ItemId)
                     .Select(group => group.First())
                     .Where(ingredient => !IsElementalCatalystItem(ingredient.ItemId)))
            this.PlanRetainerWithdrawalsForNeed(ingredient, liveStock, retainerStates, plannedTargets);

        foreach (var catalyst in details.RawMaterials
                     .Where(material => IsElementalCatalystItem(material.ItemId))
                     .GroupBy(material => material.ItemId)
                     .Select(group => group.First()))
            this.PlanRetainerWithdrawalsForNeed(catalyst, liveStock, retainerStates, plannedTargets);

        foreach (var finalRecipe in details.Recipes)
        {
            var recipe = recipes.GetRowOrDefault(finalRecipe.RecipeId);
            if (recipe is not { } recipeRow)
            {
                error = $"Recipe data for {finalRecipe.ResultName} is not available.";
                targets = [];
                return false;
            }

            if (!this.TryPlanDreamRecipe(
                    recipeRow,
                    finalRecipe.CraftCount,
                    liveStock,
                    recipesByResult,
                    retainerStates,
                    plannedTargets,
                    new HashSet<uint>(),
                    out error))
            {
                targets = [];
                return false;
            }
        }

        targets = plannedTargets;
        return true;
    }

    private void PlanRetainerWithdrawalsForNeed(
        IngredientNeed need,
        IDictionary<uint, ulong> liveStock,
        IList<RetainerStockState> retainerStates,
        IList<RetainerWithdrawalTarget> targets)
    {
        liveStock.TryGetValue(need.ItemId, out var liveOwned);
        var missing = need.Required > liveOwned
            ? (uint)Math.Min((ulong)need.Required - liveOwned, uint.MaxValue)
            : 0u;
        missing = CapInitialCatalystWithdrawal(need.ItemId, liveOwned, missing);
        if (missing == 0)
            return;

        foreach (var retainer in retainerStates)
        {
            if (!retainer.TryWithdraw(need.ItemId, missing, out var withdrawn, out var snapshotQuantity))
                continue;

            AddOrMergeDreamTarget(
                targets,
                new RetainerWithdrawalTarget(
                    retainer.RetainerId,
                    retainer.Name,
                    need.ItemId,
                    need.Name,
                    withdrawn,
                    snapshotQuantity));
            liveOwned = AddSaturating(liveOwned, withdrawn);
            liveStock[need.ItemId] = liveOwned;
            missing -= withdrawn;
            if (missing == 0)
                break;
        }
    }

    public bool TryBuildArtisanCraftQueue(
        IReadOnlyList<RecipeDetails> finalRecipes,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        out IReadOnlyList<ArtisanCraftQueueEntry> queue,
        out string error)
    {
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is null)
        {
            queue = [];
            error = "Recipe data is not available.";
            return false;
        }

        var recipesByResult = this.BuildRecipesByResult(recipes);
        var stock = ownedItems.ToDictionary(
            entry => entry.Key,
            entry => (ulong)entry.Value.Quantity);
        var plannedQueue = new List<ArtisanCraftQueueEntry>();
        foreach (var finalRecipe in finalRecipes)
        {
            var recipe = recipes.GetRowOrDefault(finalRecipe.RecipeId);
            if (recipe is not { } recipeRow)
            {
                queue = [];
                error = $"Recipe data for {finalRecipe.ResultName} is not available.";
                return false;
            }

            if (!this.TryQueueRecipe(
                    recipeRow,
                    finalRecipe.CraftCount,
                    false,
                    stock,
                    recipesByResult,
                    plannedQueue,
                    new HashSet<uint>(),
                    out error))
            {
                queue = [];
                return false;
            }
        }

        queue = plannedQueue;
        error = string.Empty;
        return true;
    }

    public bool CanCraftRecipeFromInventory(
        uint recipeId,
        uint craftCount,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        out string error)
    {
        if (craftCount == 0 || !this.TryGetRecipe(recipeId, out var recipe))
        {
            error = "Recipe data is not available.";
            return false;
        }

        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is null)
        {
            error = "Recipe data is not available.";
            return false;
        }

        var stock = ownedItems.ToDictionary(
            entry => entry.Key,
            entry => (ulong)entry.Value.Quantity);
        return this.TryQueueRecipe(
            recipe,
            craftCount,
            true,
            stock,
            this.BuildRecipesByResult(recipes),
            new List<ArtisanCraftQueueEntry>(),
            new HashSet<uint>(),
            out error);
    }

    public uint GetMaximumCraftableCountFromCurrentCatalysts(
        uint recipeId,
        uint desiredCraftCount,
        IReadOnlyDictionary<uint, OwnedInventoryItem> liveOwnedItems,
        out bool usesCatalysts)
    {
        usesCatalysts = false;
        if (desiredCraftCount == 0)
            return 0;

        if (!this.TryGetRecipe(recipeId, out var recipe))
            return desiredCraftCount;

        var catalystRequirements = this.GetElementalCatalystRequirements(recipe);
        if (catalystRequirements.Count == 0)
            return desiredCraftCount;

        usesCatalysts = true;
        var maxCraftCount = desiredCraftCount;
        foreach (var catalyst in catalystRequirements)
        {
            var ownedQuantity = liveOwnedItems.GetValueOrDefault(catalyst.ItemId)?.Quantity ?? 0;
            var supportedCrafts = catalyst.AmountPerCraft == 0
                ? desiredCraftCount
                : ownedQuantity / catalyst.AmountPerCraft;
            maxCraftCount = Math.Min(maxCraftCount, supportedCrafts);
            if (maxCraftCount == 0)
                break;
        }

        return maxCraftCount;
    }

    public uint GetMaximumCraftableCountFromCurrentInventory(
        uint recipeId,
        uint desiredCraftCount,
        IReadOnlyDictionary<uint, OwnedInventoryItem> liveOwnedItems)
    {
        if (desiredCraftCount == 0)
            return 0;

        if (!this.TryGetRecipe(recipeId, out var recipe))
            return desiredCraftCount;

        var maxCraftCount = desiredCraftCount;
        foreach (var ingredient in this.ReadIngredients(recipe))
        {
            if (ingredient.Amount == 0 || IsElementalCatalystItem(ingredient.ItemId))
                continue;

            var ownedQuantity = liveOwnedItems.GetValueOrDefault(ingredient.ItemId)?.Quantity ?? 0;
            var supportedCrafts = ownedQuantity / ingredient.Amount;
            maxCraftCount = Math.Min(maxCraftCount, supportedCrafts);
            if (maxCraftCount == 0)
                break;
        }

        return maxCraftCount;
    }

    public bool TryBuildDreamCatalystTopUpTargets(
        uint recipeId,
        uint remainingCraftCount,
        IReadOnlyDictionary<uint, OwnedInventoryItem> liveOwnedItems,
        IReadOnlyList<StoredRetainerInventory> storedRetainers,
        out IReadOnlyList<RetainerWithdrawalTarget> targets,
        out string error)
    {
        targets = [];
        error = string.Empty;

        if (remainingCraftCount == 0)
            return true;

        if (!this.TryGetRecipe(recipeId, out var recipe))
        {
            error = $"Recipe data for recipe #{recipeId} is not available.";
            return false;
        }

        var catalystRequirements = this.GetElementalCatalystRequirements(recipe);
        if (catalystRequirements.Count == 0)
            return true;

        if (storedRetainers.Count == 0)
        {
            error = "No retainer snapshot is available for crystal top-ups.";
            return false;
        }

        var liveStock = liveOwnedItems.ToDictionary(
            entry => entry.Key,
            entry => (ulong)entry.Value.Quantity);
        var retainerStates = storedRetainers
            .Select(retainer => new RetainerStockState(retainer))
            .ToList();
        var plannedTargets = new List<RetainerWithdrawalTarget>();

        foreach (var catalyst in catalystRequirements)
        {
            var currentOwned = liveOwnedItems.GetValueOrDefault(catalyst.ItemId)?.Quantity ?? 0;
            if (currentOwned >= CrystalInventoryCap)
                continue;

            var totalRequired = MultiplySaturating((ulong)catalyst.AmountPerCraft, remainingCraftCount);
            var missing = totalRequired > currentOwned
                ? totalRequired - currentOwned
                : 0UL;
            if (missing == 0)
                continue;

            var topUpQuantity = (uint)Math.Min((ulong)(CrystalInventoryCap - currentOwned), missing);
            if (topUpQuantity == 0)
                continue;

            this.PlanRetainerWithdrawalsForNeed(
                new IngredientNeed(
                    catalyst.ItemId,
                    catalyst.Name,
                    AddSaturating(currentOwned, topUpQuantity),
                    currentOwned,
                    0,
                    string.Empty,
                    false,
                    false,
                    [],
                    []),
                liveStock,
                retainerStates,
                plannedTargets);
        }

        foreach (var catalyst in catalystRequirements)
        {
            var currentOwned = liveOwnedItems.GetValueOrDefault(catalyst.ItemId)?.Quantity ?? 0;
            var plannedTopUp = plannedTargets
                .Where(target => target.ItemId == catalyst.ItemId)
                .Aggregate(0u, (total, target) => AddSaturating(total, target.WithdrawQuantity));
            if (AddSaturating(currentOwned, plannedTopUp) >= catalyst.AmountPerCraft)
                continue;

            error = $"Not enough {catalyst.Name} is available on retainers to continue crafting.";
            targets = [];
            return false;
        }

        targets = plannedTargets;
        return true;
    }

    private bool TryPlanDreamRecipe(
        Recipe recipe,
        ulong craftCount,
        IDictionary<uint, ulong> liveStock,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IList<RetainerStockState> retainerStates,
        IList<RetainerWithdrawalTarget> targets,
        ISet<uint> recipePath,
        out string error)
    {
        if (craftCount == 0)
        {
            error = string.Empty;
            return true;
        }

        var resultItemId = this.ReadItemId(recipe, "ItemResult");
        if (resultItemId == 0 || !recipePath.Add(resultItemId))
        {
            error = "A circular or invalid intermediate recipe was found.";
            return false;
        }

        foreach (var ingredient in this.ReadIngredients(recipe)
                     .GroupBy(item => item.ItemId)
                     .Select(group => (
                         ItemId: group.Key,
                         Amount: group.Aggregate(
                             0UL,
                             (total, item) => AddSaturating(total, item.Amount)))))
        {
            if (IsElementalCatalystItem(ingredient.ItemId))
                continue;

            var required = MultiplySaturating(ingredient.Amount, craftCount);
            if (!this.TryPlanDreamItem(
                    ingredient.ItemId,
                    required,
                    liveStock,
                    recipesByResult,
                    retainerStates,
                    targets,
                    recipePath,
                    out error))
            {
                recipePath.Remove(resultItemId);
                return false;
            }
        }

        recipePath.Remove(resultItemId);
        liveStock.TryGetValue(resultItemId, out var currentStock);
        var resultAmount = Math.Max(1U, this.ReadUInt(recipe, "AmountResult"));
        liveStock[resultItemId] = AddSaturating(
            currentStock,
            MultiplySaturating((ulong)resultAmount, craftCount));
        error = string.Empty;
        return true;
    }

    private bool TryPlanDreamItem(
        uint itemId,
        ulong required,
        IDictionary<uint, ulong> liveStock,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IList<RetainerStockState> retainerStates,
        IList<RetainerWithdrawalTarget> targets,
        ISet<uint> recipePath,
        out string error)
    {
        liveStock.TryGetValue(itemId, out var available);
        var consumed = Math.Min(available, required);
        liveStock[itemId] = available - consumed;
        var missing = required - consumed;
        if (missing == 0)
        {
            error = string.Empty;
            return true;
        }

        foreach (var retainer in retainerStates)
        {
            if (!retainer.TryWithdraw(itemId, missing, out var withdrawn, out var snapshotQuantity))
                continue;

            AddOrMergeDreamTarget(
                targets,
                new RetainerWithdrawalTarget(
                    retainer.RetainerId,
                    retainer.Name,
                    itemId,
                    this.GetItemName(itemId),
                    withdrawn,
                    snapshotQuantity));
            missing -= withdrawn;
            if (missing == 0)
            {
                error = string.Empty;
                return true;
            }
        }

        if (recipePath.Contains(itemId) ||
            !recipesByResult.TryGetValue(itemId, out var ingredientRecipe))
        {
            error = $"Missing materials remain for {this.GetItemName(itemId)}.";
            return false;
        }

        var resultAmount = Math.Max(1U, this.ReadUInt(ingredientRecipe, "AmountResult"));
        var ingredientCrafts =
            (missing / resultAmount) +
            (missing % resultAmount == 0 ? 0UL : 1UL);
        var canPlan = this.TryPlanDreamRecipe(
            ingredientRecipe,
            ingredientCrafts,
            liveStock,
            recipesByResult,
            retainerStates,
            targets,
            recipePath,
            out error);
        if (!canPlan)
            return false;

        liveStock.TryGetValue(itemId, out available);
        if (available < missing)
        {
            error = $"Could not plan enough {this.GetItemName(itemId)}.";
            return false;
        }

        liveStock[itemId] = available - missing;

        error = string.Empty;
        return true;
    }

    private bool TryQueueRecipe(
        Recipe recipe,
        ulong craftCount,
        bool isIntermediate,
        IDictionary<uint, ulong> stock,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        ICollection<ArtisanCraftQueueEntry> queue,
        ISet<uint> recipePath,
        out string error)
    {
        if (craftCount == 0)
        {
            error = string.Empty;
            return true;
        }

        if (recipe.RowId > ushort.MaxValue || craftCount > int.MaxValue)
        {
            error = "One of the required recipe amounts cannot be sent to Artisan.";
            return false;
        }

        var resultItemId = this.ReadItemId(recipe, "ItemResult");
        if (resultItemId == 0 || !recipePath.Add(resultItemId))
        {
            error = "A circular or invalid intermediate recipe was found.";
            return false;
        }

        foreach (var ingredient in this.ReadIngredients(recipe)
                     .GroupBy(item => item.ItemId)
                     .Select(group => (
                         ItemId: group.Key,
                         Amount: group.Aggregate(
                             0UL,
                             (total, item) => AddSaturating(total, item.Amount)))))
        {
            var required = MultiplySaturating(ingredient.Amount, craftCount);
            if (!this.TryEnsureQueueItem(
                    ingredient.ItemId,
                    required,
                    stock,
                    recipesByResult,
                    queue,
                    recipePath,
                    out error))
            {
                recipePath.Remove(resultItemId);
                return false;
            }
        }

        recipePath.Remove(resultItemId);
        var resultName = this.GetItemName(resultItemId);
        var resultAmount = Math.Max(1U, this.ReadUInt(recipe, "AmountResult"));
        queue.Add(new ArtisanCraftQueueEntry(
            recipe.RowId,
            resultItemId,
            resultName,
            resultAmount,
            (uint)craftCount,
            isIntermediate));
        stock.TryGetValue(resultItemId, out var currentStock);
        stock[resultItemId] = AddSaturating(
            currentStock,
            MultiplySaturating((ulong)resultAmount, craftCount));
        error = string.Empty;
        return true;
    }

    private bool TryEnsureQueueItem(
        uint itemId,
        ulong required,
        IDictionary<uint, ulong> stock,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        ICollection<ArtisanCraftQueueEntry> queue,
        ISet<uint> recipePath,
        out string error)
    {
        stock.TryGetValue(itemId, out var available);
        var consumed = Math.Min(available, required);
        stock[itemId] = available - consumed;
        var missing = required - consumed;
        if (missing == 0)
        {
            error = string.Empty;
            return true;
        }

        if (recipePath.Contains(itemId) ||
            !recipesByResult.TryGetValue(itemId, out var ingredientRecipe))
        {
            error = $"Missing {missing:N0} × {this.GetItemName(itemId)}.";
            return false;
        }

        var resultAmount = Math.Max(1U, this.ReadUInt(ingredientRecipe, "AmountResult"));
        var ingredientCrafts =
            (missing / resultAmount) +
            (missing % resultAmount == 0 ? 0UL : 1UL);
        if (!this.TryQueueRecipe(
                ingredientRecipe,
                ingredientCrafts,
                true,
                stock,
                recipesByResult,
                queue,
                recipePath,
                out error))
            return false;

        stock.TryGetValue(itemId, out available);
        if (available < missing)
        {
            error = $"Could not plan enough {this.GetItemName(itemId)}.";
            return false;
        }

        stock[itemId] = available - missing;
        error = string.Empty;
        return true;
    }

    public IReadOnlyList<MaterialRecipeUsage> GetRecipesUsing(uint itemId)
    {
        this.EnsureRecipeUsageIndex();
        return this.recipesByIngredient!.GetValueOrDefault(itemId) ?? [];
    }

    private void EnsureRecipeUsageIndex()
    {
        if (this.recipesByIngredient is not null)
            return;

        var index = new Dictionary<uint, List<MaterialRecipeUsage>>();
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is not null)
        {
            foreach (var recipe in recipes)
            {
                var match = this.TryCreateMatch(recipe);
                if (match is null)
                    continue;

                foreach (var ingredient in this.ReadIngredients(recipe)
                             .GroupBy(ingredient => ingredient.ItemId)
                             .Select(group => (
                                 ItemId: group.Key,
                                 Amount: (uint)Math.Min(
                                     group.Aggregate(
                                         0UL,
                                         (total, item) => total + item.Amount),
                                     uint.MaxValue))))
                {
                    if (!index.TryGetValue(ingredient.ItemId, out var usages))
                    {
                        usages = [];
                        index[ingredient.ItemId] = usages;
                    }

                    usages.Add(new MaterialRecipeUsage(
                        match.RecipeId,
                        match.ResultName,
                        ingredient.Amount,
                        match.ResultAmount));
                }
            }
        }

        this.recipesByIngredient = index.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<MaterialRecipeUsage>)entry.Value
                .OrderBy(usage => usage.ResultName)
                .ThenBy(usage => usage.RecipeId)
                .ToList());
        this.fileLog.Info(
            "Recipes",
            $"Built material usage index for {this.recipesByIngredient.Count} ingredient(s).");
    }

    private IReadOnlyList<IngredientNeed> GetRawMaterials(
        Recipe rootRecipe,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        uint craftCount)
    {
        var requirements = this.ReadIngredients(rootRecipe)
            .Select(ingredient => (
                ingredient.ItemId,
                MultiplySaturating((ulong)ingredient.Amount, craftCount)));

        return this.GetRawMaterialsForRequirements(requirements, recipesByResult, ownedItems);
    }

    private IReadOnlyList<IngredientNeed> GetRawMaterialsForRequirements(
        IEnumerable<(uint ItemId, ulong Amount)> requirements,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        var amounts = new Dictionary<uint, ulong>();
        var coveredAmounts = new Dictionary<uint, ulong>();
        var coverageNamesByItemId = new Dictionary<uint, HashSet<string>>();
        var combinedRequirements = requirements
            .Where(requirement => requirement.ItemId != 0 && requirement.Amount != 0)
            .GroupBy(requirement => requirement.ItemId)
            .Select(group => (
                ItemId: group.Key,
                Amount: group.Aggregate(0UL, (total, requirement) => AddSaturating(total, requirement.Amount))))
            .ToList();

        // Always list the complete raw-material recipe tree, then separately mark branches already covered by owned pre-crafts.
        foreach (var requirement in combinedRequirements)
            this.AddRawMaterialRequirement(requirement.ItemId, requirement.Amount, recipesByResult, amounts);

        var stock = new Dictionary<uint, ulong>();
        foreach (var requirement in combinedRequirements)
            this.MarkOwnedPreCraftCoverage(
                requirement.ItemId,
                requirement.Amount,
                recipesByResult,
                ownedItems,
                stock,
                coveredAmounts,
                coverageNamesByItemId,
                new HashSet<uint>());

        var rawIngredients = amounts
            .Select(entry => (entry.Key, (uint)Math.Min(entry.Value, uint.MaxValue)))
            .OrderBy(entry => this.GetItemName(entry.Key))
            .ToList();

        var coverageAmounts = rawIngredients.ToDictionary(
            ingredient => ingredient.Key,
            ingredient => (uint)Math.Min(
                coveredAmounts.GetValueOrDefault(ingredient.Key),
                ingredient.Item2));
        var coverageNames = coverageNamesByItemId.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList());
        return this.CreateIngredientNeeds(rawIngredients, ownedItems, coverageAmounts, coverageNames);
    }

    private void AddRawMaterialRequirement(
        uint itemId,
        ulong required,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IDictionary<uint, ulong> rawAmounts)
    {
        if (required == 0)
            return;

        if (!recipesByResult.TryGetValue(itemId, out var recipe))
        {
            rawAmounts.TryGetValue(itemId, out var current);
            rawAmounts[itemId] = AddSaturating(current, required);
            return;
        }

        var resultAmount = Math.Max(1U, this.ReadUInt(recipe, "AmountResult"));
        var craftCount = (required + resultAmount - 1) / resultAmount;
        this.ExpandRawMaterials(recipe, craftCount, recipesByResult, rawAmounts, new HashSet<uint>());
    }

    private void MarkOwnedPreCraftCoverage(
        uint itemId,
        ulong required,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        IDictionary<uint, ulong> stock,
        IDictionary<uint, ulong> coveredAmounts,
        IDictionary<uint, HashSet<string>> coverageNamesByItemId,
        ISet<uint> recipePath)
    {
        if (required == 0 ||
            recipePath.Contains(itemId) ||
            !recipesByResult.TryGetValue(itemId, out var recipe))
            return;

        if (!stock.TryGetValue(itemId, out var available))
        {
            available = ownedItems.TryGetValue(itemId, out var ownedItem)
                ? ownedItem.Quantity
                : 0;
        }

        var consumed = Math.Min(available, required);
        stock[itemId] = available - consumed;
        if (consumed == required)
        {
            var resultAmount = Math.Max(1U, this.ReadUInt(recipe, "AmountResult"));
            var craftCount = (required + resultAmount - 1) / resultAmount;
            var coveredRawAmounts = new Dictionary<uint, ulong>();
            this.ExpandRawMaterials(
                recipe,
                craftCount,
                recipesByResult,
                coveredRawAmounts,
                new HashSet<uint>());

            var preCraftName = this.GetItemName(itemId);
            foreach (var coveredRawAmount in coveredRawAmounts)
            {
                coveredAmounts.TryGetValue(coveredRawAmount.Key, out var current);
                coveredAmounts[coveredRawAmount.Key] = AddSaturating(current, coveredRawAmount.Value);
                if (!coverageNamesByItemId.TryGetValue(coveredRawAmount.Key, out var names))
                {
                    names = [];
                    coverageNamesByItemId[coveredRawAmount.Key] = names;
                }

                names.Add(preCraftName);
            }

            return;
        }

        var resultAmountForChildren = Math.Max(1U, this.ReadUInt(recipe, "AmountResult"));
        var craftCountForChildren = (required + resultAmountForChildren - 1) / resultAmountForChildren;
        recipePath.Add(itemId);
        foreach (var ingredient in this.ReadIngredients(recipe)
                     .GroupBy(ingredient => ingredient.ItemId)
                     .Select(group => (
                         ItemId: group.Key,
                         Amount: group.Aggregate(0UL, (total, ingredient) => AddSaturating(total, ingredient.Amount)))))
        {
            this.MarkOwnedPreCraftCoverage(
                ingredient.ItemId,
                MultiplySaturating(ingredient.Amount, craftCountForChildren),
                recipesByResult,
                ownedItems,
                stock,
                coveredAmounts,
                coverageNamesByItemId,
                recipePath);
        }

        recipePath.Remove(itemId);
    }

    private IReadOnlyDictionary<uint, Recipe> BuildRecipesByResult(IEnumerable<Recipe> recipes)
    {
        var recipesByResult = new Dictionary<uint, Recipe>();
        foreach (var recipe in recipes)
        {
            var resultItemId = this.ReadItemId(recipe, "ItemResult");
            if (resultItemId != 0)
                recipesByResult.TryAdd(resultItemId, recipe);
        }

        return recipesByResult;
    }

    private bool CanCraftFromOwnedItems(
        Recipe recipe,
        ulong craftCount,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        var stock = new Dictionary<uint, ulong>();
        var recipePath = new HashSet<uint>();
        var resultItemId = this.ReadItemId(recipe, "ItemResult");
        if (resultItemId != 0)
            recipePath.Add(resultItemId);

        return this.TryConsumeRecipeIngredients(
            recipe,
            craftCount,
            stock,
            recipesByResult,
            ownedItems,
            recipePath);
    }

    private bool TryConsumeRecipeIngredients(
        Recipe recipe,
        ulong craftCount,
        IDictionary<uint, ulong> stock,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        ISet<uint> recipePath)
    {
        foreach (var ingredient in this.ReadIngredients(recipe)
                     .GroupBy(item => item.ItemId)
                     .Select(group => (
                         ItemId: group.Key,
                         Amount: group.Aggregate(
                             0UL,
                             (total, item) => total + item.Amount))))
        {
            var required = MultiplySaturating(ingredient.Amount, craftCount);
            if (!this.TryConsumeItem(
                    ingredient.ItemId,
                    required,
                    stock,
                    recipesByResult,
                    ownedItems,
                    recipePath))
                return false;
        }

        return true;
    }

    private bool TryConsumeItem(
        uint itemId,
        ulong required,
        IDictionary<uint, ulong> stock,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        ISet<uint> recipePath)
    {
        if (!stock.TryGetValue(itemId, out var owned))
        {
            owned = ownedItems.TryGetValue(itemId, out var ownedItem)
                ? ownedItem.Quantity
                : 0;
        }

        var consumed = Math.Min(owned, required);
        stock[itemId] = owned - consumed;
        var missing = required - consumed;
        if (missing == 0)
            return true;

        if (recipePath.Contains(itemId) ||
            !recipesByResult.TryGetValue(itemId, out var ingredientRecipe))
            return false;

        var resultAmount = Math.Max(1U, this.ReadUInt(ingredientRecipe, "AmountResult"));
        var ingredientCrafts =
            (missing / resultAmount) +
            (missing % resultAmount == 0 ? 0UL : 1UL);
        recipePath.Add(itemId);
        var canCraft = this.TryConsumeRecipeIngredients(
            ingredientRecipe,
            ingredientCrafts,
            stock,
            recipesByResult,
            ownedItems,
            recipePath);
        recipePath.Remove(itemId);
        if (!canCraft)
            return false;

        var produced = MultiplySaturating((ulong)resultAmount, ingredientCrafts);
        if (produced > missing)
        {
            stock.TryGetValue(itemId, out var remaining);
            stock[itemId] = AddSaturating(remaining, produced - missing);
        }

        return true;
    }

    private IReadOnlyList<IngredientNeed> AddRawCraftAvailability(
        IReadOnlyList<IngredientNeed> ingredients,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        return ingredients.Select(ingredient =>
        {
            if (ingredient.HasEnough || !recipesByResult.TryGetValue(ingredient.ItemId, out var recipe))
                return ingredient;

            var resultAmount = Math.Max(1U, this.ReadUInt(recipe, "AmountResult"));
            var craftCount = ((ulong)ingredient.Missing + resultAmount - 1) / resultAmount;
            // Use the same recursive stock simulation as Craftable Now so owned intermediate materials count too.
            var canCraft = this.CanCraftFromOwnedItems(
                recipe,
                craftCount,
                recipesByResult,
                ownedItems);

            return ingredient with
            {
                CanCraftMissingFromRaw = canCraft,
                RawCraftRecipeId = recipe.RowId,
                RawCraftCount = (uint)Math.Min(craftCount, uint.MaxValue),
            };
        }).ToList();
    }

    private void ExpandRawMaterials(
        Recipe recipe,
        ulong craftCount,
        IReadOnlyDictionary<uint, Recipe> recipesByResult,
        IDictionary<uint, ulong> rawAmounts,
        ISet<uint> recipePath)
    {
        var resultItemId = this.ReadItemId(recipe, "ItemResult");
        if (resultItemId != 0)
            recipePath.Add(resultItemId);

        foreach (var (itemId, amount) in this.ReadIngredients(recipe))
        {
            var required = (ulong)amount * craftCount;
            if (recipesByResult.TryGetValue(itemId, out var ingredientRecipe) && !recipePath.Contains(itemId))
            {
                var resultAmount = Math.Max(1U, this.ReadUInt(ingredientRecipe, "AmountResult"));
                var ingredientCrafts = (required + resultAmount - 1) / resultAmount;
                this.ExpandRawMaterials(ingredientRecipe, ingredientCrafts, recipesByResult, rawAmounts, recipePath);
                continue;
            }

            rawAmounts.TryGetValue(itemId, out var current);
            rawAmounts[itemId] = current + required;
        }

        if (resultItemId != 0)
            recipePath.Remove(resultItemId);
    }

    private IReadOnlyList<IngredientNeed> CreateIngredientNeeds(
        IEnumerable<(uint ItemId, uint Amount)> ingredients,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems,
        IReadOnlyDictionary<uint, uint>? preCraftCoveredAmounts = null,
        IReadOnlyDictionary<uint, IReadOnlyList<string>>? preCraftCoverageNames = null)
    {
        return ingredients.Select(ingredient =>
        {
            ownedItems.TryGetValue(ingredient.ItemId, out var owned);
            var reductionSources = this.aetherialReductionService.GetSources(ingredient.ItemId);
            return new IngredientNeed(
                ingredient.ItemId,
                this.GetItemName(ingredient.ItemId),
                ingredient.Amount,
                owned?.NqQuantity ?? 0,
                owned?.HqQuantity ?? 0,
                this.GetItemSource(ingredient.ItemId),
                this.IsGatherable(ingredient.ItemId),
                this.IsFishing(ingredient.ItemId),
                owned?.NqLocations ?? [],
                owned?.HqLocations ?? [],
                ReductionSources: reductionSources,
                PreCraftCoveredAmount: preCraftCoveredAmounts?.GetValueOrDefault(ingredient.ItemId) ?? 0,
                PreCraftCoverageNames: preCraftCoverageNames?.GetValueOrDefault(ingredient.ItemId) ?? []);
        }).ToList();
    }

    private bool TryGetRecipe(uint recipeId, out Recipe recipe)
    {
        recipe = default;
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is null)
            return false;

        var recipeRow = recipes.GetRowOrDefault(recipeId);
        if (recipeRow is not { } resolvedRecipe)
            return false;

        recipe = resolvedRecipe;
        return true;
    }

    private IReadOnlyList<(uint ItemId, string Name, uint AmountPerCraft)> GetElementalCatalystRequirements(Recipe recipe)
    {
        return this.ReadIngredients(recipe)
            .Where(ingredient => IsElementalCatalystItem(ingredient.ItemId))
            .GroupBy(ingredient => ingredient.ItemId)
            .Select(group => (
                group.Key,
                this.GetItemName(group.Key),
                (uint)Math.Min(
                    group.Aggregate(0UL, (total, ingredient) => AddSaturating(total, ingredient.Amount)),
                    uint.MaxValue)))
            .ToList();
    }

    private IReadOnlyList<IngredientNeed> CombineMaterialRequirements(
        IEnumerable<IngredientNeed> materials,
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
        var combined = materials
            .GroupBy(material => material.ItemId)
            .Select(group => (
                ItemId: group.Key,
                Amount: (uint)Math.Min(
                    group.Aggregate(
                        0UL,
                        (total, material) => total + material.Required),
                    uint.MaxValue)))
            .OrderBy(material => this.GetItemName(material.ItemId))
            .ToList();
        return this.CreateIngredientNeeds(combined, ownedItems);
    }

    private bool IsGatherable(uint itemId)
    {
        this.EnsureItemSources();
        return this.gatherableItemIds!.Contains(itemId) ||
               this.aetherialReductionService.GetSources(itemId).Count > 0;
    }

    private bool IsFishing(uint itemId)
    {
        this.EnsureItemSources();
        return this.fishingItemIds!.Contains(itemId);
    }

    private string GetItemSource(uint itemId)
    {
        this.EnsureItemSources();

        var sources = new List<string>();
        if (this.fishingItemIds!.Contains(itemId))
            sources.Add("Fishing");
        else if (this.gatherableItemIds!.Contains(itemId))
            sources.Add("Gatherable");
        if (this.aetherialReductionService.GetSources(itemId).Count > 0)
            sources.Add("Aetherial reduction");
        if (this.vendorItemIds!.Contains(itemId))
            sources.Add("Vendor");
        if (this.craftableItemIds!.Contains(itemId))
            sources.Add("Craftable");

        return sources.Count > 0 ? string.Join(", ", sources) : "Other";
    }

    private void EnsureItemSources()
    {
        if (this.craftableItemIds is not null)
            return;

        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        var gatheringItems = this.dataManager.GetExcelSheet<GatheringItem>();
        var gatheringPointBases = this.dataManager.GetExcelSheet<GatheringPointBase>();
        var gatheringPoints = this.dataManager.GetExcelSheet<GatheringPoint>();
        var gatheringItemPoints = this.dataManager.GetSubrowExcelSheet<GatheringItemPoint>();
        var items = this.dataManager.GetExcelSheet<Item>();
        var specialShops = this.dataManager.GetExcelSheet<SpecialShop>();
        var fishParameters = this.dataManager.GetExcelSheet<FishParameter>();
        var spearfishingItems = this.dataManager.GetExcelSheet<SpearfishingItem>();

        this.craftableItemIds = recipes?
            .Select(recipe => this.ReadItemId(recipe, "ItemResult"))
            .Where(itemId => itemId != 0)
            .ToHashSet() ?? [];

        this.gatherableItemIds = gatheringItems?
            .Select(item => item.Item.RowId)
            .Where(itemId => itemId != 0)
            .ToHashSet() ?? [];

        this.fishingItemIds = this.dataManager.GetExcelSheet<FishParameter>()?
            .Select(item => item.Item.RowId)
            .Where(itemId => itemId != 0)
            .ToHashSet() ?? [];

        var spearfishingItemIds = this.dataManager.GetExcelSheet<SpearfishingItem>()?
            .Select(item => item.Item.RowId)
            .Where(itemId => itemId != 0) ?? [];
        this.fishingItemIds.UnionWith(spearfishingItemIds);
        this.gatherableItemIds.UnionWith(this.fishingItemIds);

        this.vendorItemIds = items?
            .Where(item => item.PriceMid > 0)
            .Select(item => item.RowId)
            .ToHashSet() ?? [];
        this.marketboardItemIds = items?
            .Where(item =>
                item.RowId != 0 &&
                !item.IsUntradable &&
                item.ItemSearchCategory.RowId != 0)
            .Select(item => item.RowId)
            .ToHashSet() ?? [];

        this.gatherableJobsByItemId = BuildGatherableJobsByItemId(
            gatheringItems,
            gatheringPointBases,
            gatheringPoints,
            gatheringItemPoints,
            this.fishingItemIds);
        this.gatheringLevelsByItemId = BuildGatheringLevelsByItemId(
            gatheringItems,
            gatheringItemPoints,
            fishParameters,
            spearfishingItems);
        this.itemLogEntriesByItemId = BuildItemLogEntriesByItemId(
            gatheringItems,
            fishParameters,
            spearfishingItems);
        this.recipeLevelsByRecipeId = BuildRecipeLevelsByRecipeId(recipes);
        this.recipeIdsByResultItemId = recipes?
            .Where(recipe => recipe.RowId != 0)
            .Select(recipe => (RecipeId: recipe.RowId, ResultItemId: this.ReadItemId(recipe, "ItemResult")))
            .Where(entry => entry.ResultItemId != 0)
            .GroupBy(entry => entry.ResultItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<uint>)group.Select(entry => entry.RecipeId).ToList()) ??
            new Dictionary<uint, IReadOnlyList<uint>>();
        this.collectibleRewardsByItemId = BuildCollectibleRewardLabelsByItemId(
            this.dataManager.GetSubrowExcelSheet<CollectablesShopItem>(),
            this.gatherableItemIds);
        this.folkloreBookInfoByItemId = BuildFolkloreBookInfoByItemId(
            gatheringItems,
            gatheringItemPoints,
            fishParameters,
            specialShops);
        this.requiredItemsByItemId = BuildRequiredItemsByItemId(
            gatheringItems,
            gatheringItemPoints,
            items);
        this.gatherBuddyFishTooltipData = this.TryGetGatherBuddyFishTooltipData();
        this.nextGatherBuddyFishTooltipRetryUtc = DateTime.UtcNow.AddSeconds(10);
        this.fishTooltipInfoByItemId = BuildFishTooltipInfoByItemId(
            fishParameters,
            this.dataManager.GetExcelSheet<FishingSpot>(),
            this.dataManager.GetExcelSheet<TerritoryType>(),
            this.dataManager.GetExcelSheet<PlaceName>(),
            items,
            this.gatherBuddyFishTooltipData);
        this.cosmicExplorationTooltipInfoByItemId = BuildCosmicExplorationTooltipInfoByItemId(
            this.dataManager.GetExcelSheet<WKSItemInfo>(),
            fishParameters,
            spearfishingItems,
            this.gatherBuddyFishTooltipData);
        this.questTooltipInfoByItemId = BuildQuestTooltipInfoByItemId(
            this.dataManager.GetExcelSheet<Quest>(),
            this.dataManager.GetExcelSheet<Item>(),
            this.dataManager.GetExcelSheet<PlaceName>());
        this.societyQuestTooltipInfoByItemId = BuildSocietyQuestTooltipInfoByItemId(
            this.dataManager.GetExcelSheet<Quest>(),
            this.dataManager.GetSubrowExcelSheet<QuestClassJobSupply>(),
            this.dataManager.GetExcelSheet<BeastTribe>());
        this.specialContentTooltipInfoByItemId = BuildSpecialContentTooltipInfoByItemId(
            this.dataManager.GetExcelSheet<HWDCrafterSupply>(),
            this.dataManager.GetExcelSheet<HWDGathererInspection>());
        this.excludedSearchItemIds = BuildExcludedSearchItemIds(
            items,
            this.specialContentTooltipInfoByItemId);
        this.masterRecipeBookInfoByRecipeId = BuildMasterRecipeBookInfoByRecipeId(recipes);

        this.fileLog.Info(
            "Recipes",
            $"Loaded source indexes: {this.craftableItemIds.Count} craftable, {this.gatherableItemIds.Count} gatherable, {this.fishingItemIds.Count} fishing, {this.vendorItemIds.Count} vendor, {this.marketboardItemIds.Count} marketboard-tradeable, {this.collectibleRewardsByItemId.Count} collectible reward entries, {this.folkloreBookInfoByItemId.Count} folklore mappings, {this.requiredItemsByItemId.Count} required-item mappings, {this.fishTooltipInfoByItemId.Count} fish tooltip mappings, {this.societyQuestTooltipInfoByItemId.Count} society-quest mappings, {this.cosmicExplorationTooltipInfoByItemId.Count} cosmic-exploration mappings, {this.questTooltipInfoByItemId.Count} quest-item mappings, {this.specialContentTooltipInfoByItemId.Count} special-content mappings, {this.masterRecipeBookInfoByRecipeId.Count} master recipe mappings, {this.gatheringLevelsByItemId.Count} gathering level mappings, {this.recipeLevelsByRecipeId.Count} recipe level mappings, {this.excludedSearchItemIds.Count} excluded search items.");
    }

    private IReadOnlyList<(uint ItemId, uint Amount)> ReadIngredients(Recipe recipe)
    {
        var ingredients = new List<(uint ItemId, uint Amount)>();

        for (var i = 0; i < 10; i++)
        {
            var itemId = this.ReadIndexedItemId(recipe, "Ingredient", i);
            if (itemId == 0)
                itemId = this.ReadIndexedItemId(recipe, "ItemIngredient", i);
            if (itemId == 0)
                itemId = this.ReadItemId(recipe, $"ItemIngredient{i}");
            if (itemId == 0)
                itemId = this.ReadItemId(recipe, $"Ingredient{i}");

            var amount = this.ReadIndexedUInt(recipe, "AmountIngredient", i);
            if (amount == 0)
                amount = this.ReadUInt(recipe, $"AmountIngredient{i}");
            if (itemId != 0 && amount != 0)
                ingredients.Add((itemId, amount));
        }

        return ingredients;
    }

    private static string CreateRecipeDebugInfo<T>(T recipe)
    {
        var type = typeof(T);
        var flags = BindingFlags.Instance | BindingFlags.Public;
        var members = type.GetProperties(flags)
            .Select(member => member.Name)
            .Concat(type.GetFields(flags).Select(member => member.Name))
            .Where(name => name.Contains("Ingredient", StringComparison.OrdinalIgnoreCase) || name.Contains("Item", StringComparison.OrdinalIgnoreCase) || name.Contains("Amount", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name)
            .Take(40);

        return $"Recipe type: {type.FullName} | Members: {string.Join(", ", members)}";
    }

    private RecipeMatch? TryCreateMatch(Recipe recipe)
    {
        var resultItemId = this.ReadItemId(recipe, "ItemResult");
        if (resultItemId == 0 || !this.IsSearchVisible(resultItemId))
            return null;

        var resultName = this.GetItemName(resultItemId);
        if (string.IsNullOrWhiteSpace(resultName))
            return null;

        var amount = this.ReadUInt(recipe, "AmountResult");
        var collectibleRewardLabel = this.collectibleRewardsByItemId is not null &&
                                     this.collectibleRewardsByItemId.TryGetValue(
                                         resultItemId,
                                         out var rewardInfo)
            ? rewardInfo.DisplayLabel
            : string.Empty;
        return new RecipeMatch(
            recipe.RowId,
            resultItemId,
            resultName,
            Math.Max(1, amount),
            this.GetRecipeJobAbbreviations(recipe),
            SearchResultKind.CraftedRecipe,
            collectibleRewardLabel);
    }

    private string GetRecipeJobAbbreviations(Recipe recipe)
    {
        var jobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        this.TryAddJobAbbreviationFromValue(ReadMember(recipe, "CraftType"), jobs);
        this.TryAddJobAbbreviationFromValue(ReadMember(recipe, "ClassJob"), jobs);
        return jobs.Count == 0
            ? string.Empty
            : string.Join(" / ", jobs.OrderBy(static job => job));
    }

    private void TryAddJobAbbreviationFromValue(object? value, ISet<string> jobs)
    {
        if (value is null)
            return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;

        if (value is System.Collections.IEnumerable enumerable && value is not string)
        {
            foreach (var entry in enumerable)
                this.TryAddJobAbbreviationFromValue(entry, jobs);
        }

        var nestedValue = value.GetType().GetProperty("Value", flags)?.GetValue(value);
        if (nestedValue is not null && !ReferenceEquals(nestedValue, value))
        {
            this.TryAddJobAbbreviationFromValue(nestedValue, jobs);
            return;
        }

        var rowId = value.GetType().GetProperty("RowId", flags)?.GetValue(value);
        if (rowId is uint typedRowId &&
            CraftingJobAbbreviationsByCraftTypeId.TryGetValue(typedRowId, out var mappedByRowId))
        {
            jobs.Add(mappedByRowId);
            return;
        }

        var abbreviation = value.GetType().GetProperty("Abbreviation", flags)?.GetValue(value)?.ToString();
        if (!string.IsNullOrWhiteSpace(abbreviation))
        {
            jobs.Add(abbreviation.Trim().ToUpperInvariant());
            return;
        }

        var name = value.GetType().GetProperty("Name", flags)?.GetValue(value)?.ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(name) && CraftingJobAbbreviations.TryGetValue(name, out var mappedAbbreviation))
            jobs.Add(mappedAbbreviation);
    }

    private static IReadOnlyList<string> SplitJobAbbreviations(string jobAbbreviations) =>
        string.IsNullOrWhiteSpace(jobAbbreviations)
            ? []
            : jobAbbreviations
                .Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private bool MatchesSearchQuery(RecipeMatch match, string query) =>
        (!string.IsNullOrWhiteSpace(match.ResultName) &&
         match.ResultName.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(match.SearchMetadata) &&
         match.SearchMetadata.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
        (!string.IsNullOrWhiteSpace(match.JobAbbreviations) &&
         match.JobAbbreviations.Contains(query, StringComparison.CurrentCultureIgnoreCase)) ||
        this.MatchesSearchAlias(match, query) ||
        match.ResultItemId.ToString(CultureInfo.InvariantCulture) == query;

    private bool MatchesSearchAlias(RecipeMatch match, string query)
    {
        var normalizedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            return false;

        if (normalizedQuery.StartsWith("gather", StringComparison.CurrentCultureIgnoreCase))
        {
            return match.ResultKind == SearchResultKind.CollectibleItem &&
                   match.SearchMetadata.Contains("Gatherers'", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool IsSupplementalSearchItem(uint itemId) =>
        this.IsSearchVisible(itemId) &&
        (this.collectibleRewardsByItemId!.ContainsKey(itemId) ||
         this.gatherableItemIds!.Contains(itemId) ||
         this.aetherialReductionService.GetSources(itemId).Count > 0);

    private RecipeMatch CreateSupplementalMatch(Item item)
    {
        var itemId = item.RowId;
        var isCollectible = this.collectibleRewardsByItemId!.TryGetValue(itemId, out var collectibleRewardInfo);
        var isGatherable = this.gatherableItemIds!.Contains(itemId);
        var metadata = isCollectible
            ? collectibleRewardInfo?.DisplayLabel ?? string.Empty
            : string.Empty;
        return new RecipeMatch(
            CreateSupplementalSearchId(itemId, isCollectible
                ? SearchResultKind.CollectibleItem
                : SearchResultKind.GatherableItem),
            itemId,
            item.Name.ToString(),
            1,
            this.GetItemJobAbbreviations(itemId),
            isCollectible
                ? SearchResultKind.CollectibleItem
                : SearchResultKind.GatherableItem,
            metadata,
            false);
    }

    private string GetItemJobAbbreviations(uint itemId)
    {
        if (this.gatherableJobsByItemId!.TryGetValue(itemId, out var jobs) &&
            !string.IsNullOrWhiteSpace(jobs))
            return jobs;

        if (this.fishingItemIds!.Contains(itemId))
            return "FSH";

        return this.gatherableItemIds!.Contains(itemId)
            ? "MIN / BTN"
            : string.Empty;
    }

    private static uint CreateSupplementalSearchId(uint itemId, SearchResultKind kind) =>
        kind switch
        {
            SearchResultKind.CollectibleItem => 4_000_000_000u + itemId,
            SearchResultKind.GatherableItem => 3_000_000_000u + itemId,
            _ => itemId,
        };

    private static int GetSearchKindSortOrder(SearchResultKind kind) =>
        kind switch
        {
            SearchResultKind.CraftedRecipe => 0,
            SearchResultKind.CollectibleItem => 1,
            SearchResultKind.GatherableItem => 2,
            _ => 3,
        };

    private static IReadOnlyDictionary<uint, string> BuildGatherableJobsByItemId(
        ExcelSheet<GatheringItem>? gatheringItems,
        ExcelSheet<GatheringPointBase>? gatheringPointBases,
        ExcelSheet<GatheringPoint>? gatheringPoints,
        SubrowExcelSheet<GatheringItemPoint>? gatheringItemPoints,
        IReadOnlySet<uint> fishingItemIds)
    {
        if (gatheringItems is null)
            return new Dictionary<uint, string>();

        var gatheringItemIdsByRowId = gatheringItems
            .Where(item => item.RowId != 0 && item.Item.RowId != 0)
            .ToDictionary(item => item.RowId, item => item.Item.RowId);
        var jobsByItemId = new Dictionary<uint, HashSet<string>>();
        var itemsWithExplicitJobs = new HashSet<uint>();

        if (gatheringPoints is not null && gatheringItemPoints is not null)
        {
            var jobsByPointId = gatheringPoints
                .Where(point => point.RowId != 0)
                .ToDictionary(
                    point => point.RowId,
                    GetGatheringPointJob);
            foreach (var row in gatheringItemPoints.SelectMany(rows => rows))
            {
                if (!gatheringItemIdsByRowId.TryGetValue(row.RowId, out var itemId) ||
                    !jobsByPointId.TryGetValue(row.GatheringPoint.RowId, out var job) ||
                    string.IsNullOrWhiteSpace(job))
                    continue;

                AddGatheringJob(jobsByItemId, itemId, job);
                itemsWithExplicitJobs.Add(itemId);
            }
        }

        if (gatheringPointBases is not null)
        {
            foreach (var pointBase in gatheringPointBases.Where(pointBase => pointBase.RowId != 0))
            {
                var job = MapGatheringTypeToJob(pointBase.GatheringType.Value.Name.ToString());
                if (string.IsNullOrWhiteSpace(job))
                    continue;

                foreach (var entry in pointBase.Item)
                {
                    if (gatheringItemIdsByRowId.TryGetValue(entry.RowId, out var itemId) &&
                        !itemsWithExplicitJobs.Contains(itemId))
                        AddGatheringJob(jobsByItemId, itemId, job);
                }
            }
        }

        foreach (var itemId in fishingItemIds)
            AddGatheringJob(jobsByItemId, itemId, "FSH");

        return jobsByItemId.ToDictionary(
            pair => pair.Key,
            pair => string.Join(
                " / ",
                pair.Value.OrderBy(static job => job)));
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<uint>> BuildGatheringLevelsByItemId(
        ExcelSheet<GatheringItem>? gatheringItems,
        SubrowExcelSheet<GatheringItemPoint>? gatheringItemPoints,
        ExcelSheet<FishParameter>? fishParameters,
        ExcelSheet<SpearfishingItem>? spearfishingItems)
    {
        if (gatheringItems is null && fishParameters is null && spearfishingItems is null)
            return new Dictionary<uint, IReadOnlyList<uint>>();

        var itemIdsByGatheringItemRowId = gatheringItems is null
            ? new Dictionary<uint, uint>()
            : gatheringItems
                .Where(item => item.RowId != 0 && item.Item.RowId != 0)
                .ToDictionary(item => item.RowId, item => item.Item.RowId);
        var levelsByItemId = new Dictionary<uint, HashSet<uint>>();

        foreach (var gatheringItem in gatheringItems?.Where(item => item.RowId != 0 && item.Item.RowId != 0) ?? [])
        {
            try
            {
                var itemLevel = gatheringItem.GatheringItemLevel.Value.GatheringItemLevel;
                if (itemLevel > 0)
                    AddGatheringLevel(levelsByItemId, gatheringItem.Item.RowId, itemLevel);
            }
            catch (InvalidOperationException)
            {
            }
        }

        if (gatheringItemPoints is not null)
        {
            foreach (var row in gatheringItemPoints.SelectMany(rows => rows))
            {
                if (!itemIdsByGatheringItemRowId.TryGetValue(row.RowId, out var itemId))
                    continue;

                try
                {
                    var gatheringLevel = row.GatheringPoint.Value.GatheringPointBase.Value.GatheringLevel;
                    if (gatheringLevel > 0)
                        AddGatheringLevel(levelsByItemId, itemId, gatheringLevel);
                }
                catch (InvalidOperationException)
                {
                }
            }
        }

        foreach (var fishParameter in fishParameters?.Where(item => item.RowId != 0 && item.Item.RowId != 0) ?? [])
        {
            try
            {
                var itemLevel = fishParameter.GatheringItemLevel.Value.GatheringItemLevel;
                if (itemLevel > 0)
                    AddGatheringLevel(levelsByItemId, fishParameter.Item.RowId, itemLevel);
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                var gatheringLevel = fishParameter.FishingSpot.Value.GatheringLevel;
                if (gatheringLevel > 0)
                    AddGatheringLevel(levelsByItemId, fishParameter.Item.RowId, gatheringLevel);
            }
            catch (InvalidOperationException)
            {
            }
        }

        foreach (var spearfishingItem in spearfishingItems?.Where(item => item.RowId != 0 && item.Item.RowId != 0) ?? [])
        {
            try
            {
                var itemLevel = spearfishingItem.GatheringItemLevel.Value.GatheringItemLevel;
                if (itemLevel > 0)
                    AddGatheringLevel(levelsByItemId, spearfishingItem.Item.RowId, itemLevel);
            }
            catch (InvalidOperationException)
            {
            }
        }

        return levelsByItemId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<uint>)pair.Value.OrderBy(level => level).ToList());
    }

    private static IReadOnlyDictionary<uint, uint> BuildRecipeLevelsByRecipeId(
        ExcelSheet<Recipe>? recipes)
    {
        if (recipes is null)
            return new Dictionary<uint, uint>();

        var recipeLevelsByRecipeId = new Dictionary<uint, uint>();
        foreach (var recipe in recipes.Where(recipe => recipe.RowId != 0))
        {
            try
            {
                var level = recipe.RecipeLevelTable.Value.ClassJobLevel;
                if (level > 0)
                    recipeLevelsByRecipeId[recipe.RowId] = level;
            }
            catch (InvalidOperationException)
            {
            }
        }

        return recipeLevelsByRecipeId;
    }

    private static string GetGatheringPointJob(GatheringPoint point)
    {
        try
        {
            var classJob = point.GatheringSubCategory.Value.ClassJob.Value;
            var abbreviation = classJob.Abbreviation.ToString().Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(abbreviation))
                return abbreviation;
        }
        catch (InvalidOperationException)
        {
            // Some gathering points expose an unusable subcategory row ref; fall back to gathering type below.
        }

        return MapGatheringTypeToJob(point.GatheringPointBase.Value.GatheringType.Value.Name.ToString());
    }

    private static IReadOnlyDictionary<uint, CollectibleRewardInfo> BuildCollectibleRewardLabelsByItemId(
        SubrowExcelSheet<CollectablesShopItem>? collectableItems,
        IReadOnlySet<uint>? gatherableItemIds)
    {
        if (collectableItems is null)
            return new Dictionary<uint, CollectibleRewardInfo>();

        return collectableItems
            .SelectMany(rows => rows)
            .Where(row => row.Item.RowId != 0)
            .GroupBy(row => row.Item.RowId)
            .Select(group =>
            {
                var selected = group
                    .OrderByDescending(row => row.CollectablesShopRewardScrip.Value.LowReward)
                    .ThenByDescending(row => row.LevelMax)
                    .First();
                return new
                {
                    ItemId = group.Key,
                    RewardInfo = BuildCollectibleRewardLabel(
                        group.Key,
                        selected,
                        gatherableItemIds ?? new HashSet<uint>()),
                };
            })
            .Where(entry => entry.RewardInfo is not null)
            .ToDictionary(
                entry => entry.ItemId,
                entry => entry.RewardInfo!);
    }

    private static CollectibleRewardInfo? BuildCollectibleRewardLabel(
        uint itemId,
        CollectablesShopItem row,
        IReadOnlySet<uint> gatherableItemIds)
    {
        var rewardSheet = row.CollectablesShopRewardScrip.Value;
        var baseReward = rewardSheet.LowReward;
        if (baseReward == 0)
            return null;

        var isGatherer = gatherableItemIds.Contains(itemId);
        var tier = row.LevelMin >= 100 || row.LevelMax >= 100
            ? "Orange"
            : "Purple";
        var role = isGatherer
            ? "Gatherers'"
            : "Crafters'";
        return new CollectibleRewardInfo(
            $"{tier} {role} Scrips",
            baseReward,
            rewardSheet.MidReward,
            rewardSheet.HighReward);
    }

    private static IReadOnlyDictionary<uint, FolkloreBookInfo> BuildFolkloreBookInfoByItemId(
        ExcelSheet<GatheringItem>? gatheringItems,
        SubrowExcelSheet<GatheringItemPoint>? gatheringItemPoints,
        ExcelSheet<FishParameter>? fishParameters,
        ExcelSheet<SpecialShop>? specialShops)
    {
        if ((gatheringItems is null || gatheringItemPoints is null) && fishParameters is null)
            return new Dictionary<uint, FolkloreBookInfo>();

        var itemIdsByGatheringItemRowId = gatheringItems is null
            ? new Dictionary<uint, uint>()
            : gatheringItems
                .Where(item => item.RowId != 0 && item.Item.RowId != 0)
                .ToDictionary(item => item.RowId, item => item.Item.RowId);
        var exchangeInfoByBookItemId = BuildFolkloreExchangeInfoByBookItemId(specialShops);
        var folkloreByItemId = new Dictionary<uint, FolkloreBookInfo>();

        foreach (var row in gatheringItemPoints?.SelectMany(rows => rows) ?? [])
        {
            if (!itemIdsByGatheringItemRowId.TryGetValue(row.RowId, out var itemId))
                continue;

            if (!TryGetFolkloreBookMapping(row, exchangeInfoByBookItemId, out var folkloreBookInfo))
                continue;

            folkloreByItemId[itemId] = folkloreBookInfo;
        }

        foreach (var fishParameter in fishParameters?.Where(item => item.RowId != 0 && item.Item.RowId != 0) ?? [])
        {
            if (!TryGetFolkloreBookMapping(
                    fishParameter.Item.RowId,
                    fishParameter.GatheringSubCategory,
                    exchangeInfoByBookItemId,
                    out var folkloreBookInfo))
                continue;

            folkloreByItemId[fishParameter.Item.RowId] = folkloreBookInfo;
        }

        return folkloreByItemId;
    }

    private static bool TryGetFolkloreBookMapping(
        GatheringItemPoint row,
        IReadOnlyDictionary<uint, (string ExchangeName, string CostLabel)> exchangeInfoByBookItemId,
        out FolkloreBookInfo folkloreBookInfo)
    {
        folkloreBookInfo = null!;

        try
        {
            var gatheringPoint = row.GatheringPoint.Value;
            if (gatheringPoint.RowId == 0)
                return false;

            var subCategory = gatheringPoint.GatheringSubCategory.Value;
            var bookItemId = subCategory.Item.RowId;
            if (bookItemId == 0 ||
                !exchangeInfoByBookItemId.TryGetValue(bookItemId, out var exchangeInfo))
                return false;

            var bookName = TryGetItemName(subCategory.Item).Trim();
            if (string.IsNullOrWhiteSpace(bookName))
                bookName = subCategory.FolkloreBook.ToString().Trim();
            if (string.IsNullOrWhiteSpace(bookName))
                return false;

            folkloreBookInfo = new FolkloreBookInfo(
                bookName,
                exchangeInfo.ExchangeName,
                exchangeInfo.CostLabel);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryGetFolkloreBookMapping(
        uint itemId,
        Lumina.Excel.RowRef<GatheringSubCategory> gatheringSubCategoryRef,
        IReadOnlyDictionary<uint, (string ExchangeName, string CostLabel)> exchangeInfoByBookItemId,
        out FolkloreBookInfo folkloreBookInfo)
    {
        folkloreBookInfo = null!;

        try
        {
            var subCategory = gatheringSubCategoryRef.Value;
            var bookItemId = subCategory.Item.RowId;
            if (itemId == 0 ||
                bookItemId == 0 ||
                !exchangeInfoByBookItemId.TryGetValue(bookItemId, out var exchangeInfo))
                return false;

            var bookName = TryGetItemName(subCategory.Item).Trim();
            if (string.IsNullOrWhiteSpace(bookName))
                bookName = subCategory.FolkloreBook.ToString().Trim();
            if (string.IsNullOrWhiteSpace(bookName))
                return false;

            folkloreBookInfo = new FolkloreBookInfo(
                bookName,
                exchangeInfo.ExchangeName,
                exchangeInfo.CostLabel);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<uint, MasterRecipeBookInfo> BuildMasterRecipeBookInfoByRecipeId(
        ExcelSheet<Recipe>? recipes)
    {
        if (recipes is null)
            return new Dictionary<uint, MasterRecipeBookInfo>();

        var masterRecipeBookInfoByRecipeId = new Dictionary<uint, MasterRecipeBookInfo>();
        foreach (var recipe in recipes.Where(recipe => recipe.RowId != 0))
        {
            if (!TryGetMasterRecipeBookInfo(recipe, out var masterRecipeBookInfo))
                continue;

            masterRecipeBookInfoByRecipeId[recipe.RowId] = masterRecipeBookInfo;
        }

        return masterRecipeBookInfoByRecipeId;
    }

    private static bool TryGetMasterRecipeBookInfo(
        Recipe recipe,
        out MasterRecipeBookInfo masterRecipeBookInfo)
    {
        masterRecipeBookInfo = null!;

        try
        {
            var secretRecipeBook = recipe.SecretRecipeBook.Value;
            if (secretRecipeBook.RowId == 0)
                return false;

            var bookName = secretRecipeBook.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(bookName))
                bookName = secretRecipeBook.Item.Value.Name.ToString().Trim();
            if (string.IsNullOrWhiteSpace(bookName))
                return false;

            masterRecipeBookInfo = new MasterRecipeBookInfo(bookName);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<uint, RequiredItemInfo> BuildRequiredItemsByItemId(
        ExcelSheet<GatheringItem>? gatheringItems,
        SubrowExcelSheet<GatheringItemPoint>? gatheringItemPoints,
        ExcelSheet<Item>? items)
    {
        if (gatheringItems is null || gatheringItemPoints is null || items is null)
            return new Dictionary<uint, RequiredItemInfo>();

        var itemIdsByGatheringItemRowId = gatheringItems
            .Where(item => item.RowId != 0 && item.Item.RowId != 0)
            .ToDictionary(item => item.RowId, item => item.Item.RowId);
        var itemsById = items
            .Where(item => item.RowId != 0)
            .ToDictionary(item => item.RowId);
        var requiredItemsByItemId = new Dictionary<uint, RequiredItemInfo>();

        foreach (var row in gatheringItemPoints.SelectMany(rows => rows))
        {
            if (!itemIdsByGatheringItemRowId.TryGetValue(row.RowId, out var itemId))
                continue;

            try
            {
                var gatheringPoint = row.GatheringPoint.Value;
                if (gatheringPoint.RowId == 0)
                    continue;

                foreach (var bonusRef in gatheringPoint.GatheringPointBonus)
                {
                    if (bonusRef.RowId == 0)
                        continue;

                    var bonus = bonusRef.Value;
                    if (bonus.Condition.RowId != 20 ||
                        bonus.ConditionValue == 0 ||
                        !itemsById.TryGetValue(bonus.ConditionValue, out var requiredItem))
                        continue;

                    var itemName = requiredItem.Name.ToString().Trim();
                    if (string.IsNullOrWhiteSpace(itemName))
                        continue;

                    requiredItemsByItemId[itemId] = new RequiredItemInfo(
                        itemName,
                        IsToolItem(requiredItem));
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        return requiredItemsByItemId;
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<ItemLogEntry>> BuildItemLogEntriesByItemId(
        ExcelSheet<GatheringItem>? gatheringItems,
        ExcelSheet<FishParameter>? fishParameters,
        ExcelSheet<SpearfishingItem>? spearfishingItems)
    {
        var entriesByItemId = new Dictionary<uint, List<ItemLogEntry>>();

        void Add(uint itemId, ItemLogEntry entry)
        {
            if (itemId == 0)
                return;

            if (!entriesByItemId.TryGetValue(itemId, out var entries))
            {
                entries = [];
                entriesByItemId[itemId] = entries;
            }

            if (!entries.Contains(entry))
                entries.Add(entry);
        }

        foreach (var gatheringItem in gatheringItems?.Where(item => item.RowId != 0 && item.Item.RowId != 0) ?? [])
            Add(gatheringItem.Item.RowId, new ItemLogEntry(ItemLogKind.Gathering, gatheringItem.RowId, "Gathering log"));

        foreach (var fishParameter in fishParameters?.Where(fish => fish.RowId != 0 && fish.Item.RowId != 0 && fish.IsInLog) ?? [])
            Add(fishParameter.Item.RowId, new ItemLogEntry(ItemLogKind.Fishing, fishParameter.RowId, "Fishing log"));

        foreach (var spearfishingItem in spearfishingItems?.Where(fish => fish.RowId >= 20000 && fish.Item.RowId != 0) ?? [])
            Add(spearfishingItem.Item.RowId, new ItemLogEntry(ItemLogKind.Spearfishing, spearfishingItem.RowId, "Spearfishing log"));

        return entriesByItemId.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<ItemLogEntry>)pair.Value);
    }

    private static IReadOnlyDictionary<uint, FishTooltipInfo> BuildFishTooltipInfoByItemId(
        ExcelSheet<FishParameter>? fishParameters,
        ExcelSheet<FishingSpot>? fishingSpots,
        ExcelSheet<TerritoryType>? territoryTypes,
        ExcelSheet<PlaceName>? placeNames,
        ExcelSheet<Item>? items,
        IReadOnlyDictionary<uint, GatherBuddyFishTooltipData> gatherBuddyFishTooltipData)
    {
        if (fishParameters is null)
            return new Dictionary<uint, FishTooltipInfo>();

        var spotsById = fishingSpots?
            .Where(spot => spot.RowId != 0)
            .ToDictionary(spot => spot.RowId) ?? new Dictionary<uint, FishingSpot>();
        var territoriesById = territoryTypes?
            .Where(territory => territory.RowId != 0)
            .ToDictionary(territory => territory.RowId) ?? new Dictionary<uint, TerritoryType>();
        var placeNamesById = placeNames?
            .Where(placeName => placeName.RowId != 0)
            .ToDictionary(
                placeName => placeName.RowId,
                placeName => placeName.Name.ToString().Trim()) ?? new Dictionary<uint, string>();
        var itemRaritiesById = items?
            .Where(item => item.RowId != 0)
            .ToDictionary(item => item.RowId, item => item.Rarity) ?? new Dictionary<uint, byte>();
        var tooltipInfoByItemId = gatherBuddyFishTooltipData
            .Where(pair =>
                !string.IsNullOrWhiteSpace(pair.Value.BaitName) ||
                !string.IsNullOrWhiteSpace(pair.Value.FishType))
            .ToDictionary(
                pair => pair.Key,
                pair => new FishTooltipInfo(
                    pair.Value.BaitName,
                    pair.Value.FishType,
                    string.Empty,
                    string.Empty));

        foreach (var fishParameter in fishParameters.Where(fish => fish.RowId != 0 && fish.Item.RowId != 0))
        {
            gatherBuddyFishTooltipData.TryGetValue(fishParameter.Item.RowId, out var gatherBuddyFishInfo);
            var baitName = gatherBuddyFishInfo?.BaitName ?? string.Empty;

            var fishType = string.Empty;
            var bestZone = string.Empty;
            var bestSpot = string.Empty;
            if (spotsById.TryGetValue(fishParameter.FishingSpot.RowId, out var fishingSpot))
            {
                bestSpot = GetPlaceName(
                    placeNamesById,
                    fishingSpot.PlaceName.RowId,
                    fishingSpot.PlaceNameMain.RowId,
                    fishingSpot.PlaceNameSub.RowId);
                if (territoriesById.TryGetValue(fishingSpot.TerritoryType.RowId, out var territory))
                    bestZone = GetPlaceName(placeNamesById, territory.PlaceName.RowId);
            }

            fishType = !string.IsNullOrWhiteSpace(gatherBuddyFishInfo?.FishType)
                ? gatherBuddyFishInfo.FishType
                : itemRaritiesById.TryGetValue(fishParameter.Item.RowId, out var rarity) && rarity > 1
                    ? "Big fish"
                    : "Regular fish";

            if (string.IsNullOrWhiteSpace(baitName) &&
                string.IsNullOrWhiteSpace(fishType) &&
                string.IsNullOrWhiteSpace(bestZone) &&
                string.IsNullOrWhiteSpace(bestSpot))
                continue;

            tooltipInfoByItemId[fishParameter.Item.RowId] = new FishTooltipInfo(
                baitName,
                fishType,
                bestZone,
                bestSpot);
        }

        foreach (var (itemId, fallbackInfo) in QuestFishFallbackInfoByItemId)
        {
            if (!tooltipInfoByItemId.TryGetValue(itemId, out var tooltipInfo))
            {
                tooltipInfoByItemId[itemId] = fallbackInfo;
                continue;
            }

            tooltipInfoByItemId[itemId] = tooltipInfo with
            {
                BaitName = string.IsNullOrWhiteSpace(tooltipInfo.BaitName) ? fallbackInfo.BaitName : tooltipInfo.BaitName,
                FishType = string.IsNullOrWhiteSpace(tooltipInfo.FishType) ? fallbackInfo.FishType : tooltipInfo.FishType,
                BestZone = string.IsNullOrWhiteSpace(tooltipInfo.BestZone) ? fallbackInfo.BestZone : tooltipInfo.BestZone,
                BestSpot = string.IsNullOrWhiteSpace(tooltipInfo.BestSpot) ? fallbackInfo.BestSpot : tooltipInfo.BestSpot,
            };
        }

        return tooltipInfoByItemId;
    }

    private static IReadOnlyDictionary<uint, CosmicExplorationTooltipInfo> BuildCosmicExplorationTooltipInfoByItemId(
        ExcelSheet<WKSItemInfo>? wksItemInfos,
        ExcelSheet<FishParameter>? fishParameters,
        ExcelSheet<SpearfishingItem>? spearfishingItems,
        IReadOnlyDictionary<uint, GatherBuddyFishTooltipData> gatherBuddyFishTooltipData)
    {
        var tooltipInfoByItemId = wksItemInfos?
            .Where(itemInfo => itemInfo.Item.RowId != 0)
            .Select(itemInfo => itemInfo.Item.RowId)
            .Distinct()
            .ToDictionary(
                itemId => itemId,
                _ => new CosmicExplorationTooltipInfo(string.Empty)) ??
            new Dictionary<uint, CosmicExplorationTooltipInfo>();

        foreach (var (itemId, fishTooltipData) in gatherBuddyFishTooltipData)
        {
            if (!string.IsNullOrWhiteSpace(fishTooltipData.CosmicMissionName))
                tooltipInfoByItemId[itemId] = new CosmicExplorationTooltipInfo(fishTooltipData.CosmicMissionName);
        }

        return tooltipInfoByItemId;
    }

    private static IReadOnlyDictionary<uint, QuestTooltipInfo> BuildQuestTooltipInfoByItemId(
        ExcelSheet<Quest>? quests,
        ExcelSheet<Item>? items,
        ExcelSheet<PlaceName>? placeNames)
    {
        if (quests is null || items is null || placeNames is null)
            return new Dictionary<uint, QuestTooltipInfo>();

        var itemIds = items.Where(item => item.RowId != 0).Select(item => item.RowId).ToHashSet();
        var placeNamesById = placeNames
            .Where(placeName => placeName.RowId != 0)
            .ToDictionary(placeName => placeName.RowId, placeName => placeName.Name.ToString().Trim());
        var linesByItemId = new Dictionary<uint, HashSet<string>>();

        foreach (var quest in quests.Where(quest => quest.RowId != 0 && !string.IsNullOrWhiteSpace(quest.Name.ToString())))
        {
            var requiredItemIds = quest.ItemCatalyst
                .Select(item => item.RowId)
                .Concat(quest.QuestParams
                    .Where(parameter => parameter.ScriptInstruction.ToString().StartsWith("RITEM", StringComparison.Ordinal))
                    .Select(parameter => parameter.ScriptArg))
                .Where(itemId => itemId != 0 && itemIds.Contains(itemId))
                .Distinct();
            var questName = quest.Name.ToString().Trim();
            var locationName = placeNamesById.GetValueOrDefault(quest.PlaceName.RowId, string.Empty);

            foreach (var itemId in requiredItemIds)
            {
                if (!linesByItemId.TryGetValue(itemId, out var lines))
                {
                    lines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    linesByItemId[itemId] = lines;
                }

                lines.Add($"Quest: {questName}");
                if (!string.IsNullOrWhiteSpace(locationName))
                    lines.Add($"Location: {locationName}");
            }
        }

        return linesByItemId.ToDictionary(
            pair => pair.Key,
            pair => new QuestTooltipInfo(pair.Value.ToList()));
    }

    private IReadOnlyDictionary<uint, GatherBuddyFishTooltipData> GetGatherBuddyFishTooltipData()
    {
        if (this.gatherBuddyFishTooltipData is { Count: > 0 } || DateTime.UtcNow < this.nextGatherBuddyFishTooltipRetryUtc)
            return this.gatherBuddyFishTooltipData ?? new Dictionary<uint, GatherBuddyFishTooltipData>();

        this.gatherBuddyFishTooltipData = this.TryGetGatherBuddyFishTooltipData();
        this.nextGatherBuddyFishTooltipRetryUtc = DateTime.UtcNow.AddSeconds(10);
        return this.gatherBuddyFishTooltipData;
    }

    private IReadOnlyDictionary<uint, GatherBuddyFishTooltipData> TryGetGatherBuddyFishTooltipData()
    {
        try
        {
            var gatherBuddyAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly =>
                    string.Equals(assembly.GetName().Name, "GatherBuddy", StringComparison.Ordinal));
            var gameData = gatherBuddyAssembly?
                .GetType("GatherBuddy.GatherBuddy")?
                .GetProperty("GameData", BindingFlags.Public | BindingFlags.Static)?
                .GetValue(null);
            var fishes = gameData?
                .GetType()
                .GetProperty("Fishes", BindingFlags.Public | BindingFlags.Instance)?
                .GetValue(gameData) as IEnumerable;
            if (fishes is null)
                return new Dictionary<uint, GatherBuddyFishTooltipData>();

            var tooltipDataByFishItemId = new Dictionary<uint, GatherBuddyFishTooltipData>();
            foreach (var entry in fishes)
            {
                if (entry is null)
                    continue;

                var entryType = entry.GetType();
                if (entryType.GetProperty("Key")?.GetValue(entry) is not uint fishItemId || fishItemId == 0)
                    continue;

                var fish = entryType.GetProperty("Value")?.GetValue(entry);
                if (fish is null)
                    continue;

                var bait = fish.GetType()
                    .GetProperty("InitialBait", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(fish);
                var baitId = bait?
                    .GetType()
                    .GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(bait);
                var baitName = bait?
                    .GetType()
                    .GetField("Name", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(bait) as string;
                var cosmicMission = fish.GetType()
                    .GetProperty("CosmicMission", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(fish);
                var cosmicMissionName = cosmicMission?
                    .GetType()
                    .GetField("Name", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(cosmicMission) as string;
                var fishType = GetGatherBuddyFishType(fish);
                var resolvedBaitName = baitId is uint { } id &&
                                       id != 0 &&
                                       !string.IsNullOrWhiteSpace(baitName) &&
                                       !string.Equals(baitName, "Unknown", StringComparison.OrdinalIgnoreCase)
                    ? baitName
                    : string.Empty;
                if (!string.IsNullOrWhiteSpace(resolvedBaitName) ||
                    !string.IsNullOrWhiteSpace(cosmicMissionName) ||
                    !string.IsNullOrWhiteSpace(fishType))
                {
                    tooltipDataByFishItemId[fishItemId] = new GatherBuddyFishTooltipData(
                        resolvedBaitName,
                        cosmicMissionName ?? string.Empty,
                        fishType);
                }
            }

            return tooltipDataByFishItemId;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning(
                "GatherBuddy",
                $"Could not read resolved fish bait data: {exception.Message}");
            return new Dictionary<uint, GatherBuddyFishTooltipData>();
        }
    }

    private sealed record GatherBuddyFishTooltipData(
        string BaitName,
        string CosmicMissionName,
        string FishType);

    private sealed record ItemLogEntry(ItemLogKind Kind, uint EntryId, string Label);

    private enum ItemLogKind
    {
        Gathering,
        Fishing,
        Spearfishing,
    }

    private static string GetGatherBuddyFishType(object fish)
    {
        var fishType = fish.GetType();
        if (fishType.GetProperty("IsSpearFish", BindingFlags.Public | BindingFlags.Instance)?.GetValue(fish) is true)
            return "Spearfishing";
        if (fishType.GetProperty("OceanFish", BindingFlags.Public | BindingFlags.Instance)?.GetValue(fish) is true)
            return "Ocean fishing";
        if (fishType.GetProperty("IsBigFish", BindingFlags.Public | BindingFlags.Instance)?.GetValue(fish) is true)
            return "Big fish";

        return "Regular fish";
    }

    private static object? GetGatherBuddyFish(IEnumerable fishes, uint itemId)
    {
        foreach (var entry in fishes)
        {
            if (entry is not null &&
                entry.GetType().GetProperty("Key")?.GetValue(entry) is uint fishItemId &&
                fishItemId == itemId)
                return entry.GetType().GetProperty("Value")?.GetValue(entry);
        }

        return null;
    }

    private static string FormatGatherBuddyDuration(long milliseconds)
    {
        var duration = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s"
            : $"{duration.Minutes}m {duration.Seconds}s";
    }

    private static IReadOnlyDictionary<uint, SocietyQuestTooltipInfo> BuildSocietyQuestTooltipInfoByItemId(
        ExcelSheet<Quest>? quests,
        SubrowExcelSheet<QuestClassJobSupply>? questClassJobSupplies,
        ExcelSheet<BeastTribe>? beastTribes)
    {
        if (quests is null || questClassJobSupplies is null || beastTribes is null)
            return new Dictionary<uint, SocietyQuestTooltipInfo>();

        var societyNamesById = beastTribes
            .Where(society => society.RowId != 0)
            .ToDictionary(society => society.RowId, society => society.Name.ToString().Trim());
        var societyNamesBySupplyId = new Dictionary<uint, HashSet<string>>();
        foreach (var quest in quests.Where(quest =>
                     quest.RowId != 0 &&
                     quest.BeastTribe.RowId != 0 &&
                     quest.QuestClassJobSupply.RowId != 0))
        {
            if (!societyNamesById.TryGetValue(quest.BeastTribe.RowId, out var societyName) ||
                string.IsNullOrWhiteSpace(societyName))
                continue;

            if (!societyNamesBySupplyId.TryGetValue(quest.QuestClassJobSupply.RowId, out var societyNames))
            {
                societyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                societyNamesBySupplyId[quest.QuestClassJobSupply.RowId] = societyNames;
            }

            societyNames.Add(societyName);
        }

        var societyNamesByItemId = new Dictionary<uint, HashSet<string>>();
        foreach (var supply in questClassJobSupplies.SelectMany(supplies => supplies))
        {
            if (supply.Item.RowId == 0 ||
                !societyNamesBySupplyId.TryGetValue(supply.RowId, out var societyNames))
                continue;

            if (!societyNamesByItemId.TryGetValue(supply.Item.RowId, out var itemSocietyNames))
            {
                itemSocietyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                societyNamesByItemId[supply.Item.RowId] = itemSocietyNames;
            }

            itemSocietyNames.UnionWith(societyNames);
        }

        return societyNamesByItemId.ToDictionary(
            pair => pair.Key,
            pair => new SocietyQuestTooltipInfo(
                pair.Value
                    .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                    .Select(name => $"Society quest: {name}")
                    .ToList()));
    }

    private static string GetPlaceName(
        IReadOnlyDictionary<uint, string> placeNamesById,
        params uint[] placeNameIds)
    {
        foreach (var placeNameId in placeNameIds)
        {
            if (placeNameId != 0 &&
                placeNamesById.TryGetValue(placeNameId, out var placeName) &&
                !string.IsNullOrWhiteSpace(placeName))
                return placeName;
        }

        return string.Empty;
    }

    private static IReadOnlyDictionary<uint, SpecialContentTooltipInfo> BuildSpecialContentTooltipInfoByItemId(
        ExcelSheet<HWDCrafterSupply>? hwdCrafterSupply,
        ExcelSheet<HWDGathererInspection>? hwdGathererInspection)
    {
        var tooltipInfoByItemId = new Dictionary<uint, SpecialContentTooltipInfo>();

        if (hwdCrafterSupply is not null)
        {
            foreach (var row in hwdCrafterSupply)
            {
                foreach (var supply in row.HWDCrafterSupplyParams.Where(supply => supply.ItemTradeIn.RowId != 0))
                {
                    tooltipInfoByItemId[supply.ItemTradeIn.RowId] = new SpecialContentTooltipInfo(
                        "Ishgardian Restoration",
                        BuildCrafterRestorationLines(supply));
                }
            }
        }

        if (hwdGathererInspection is not null)
        {
            foreach (var row in hwdGathererInspection)
            {
                foreach (var inspection in row.HWDGathererInspectionData.Where(inspection => inspection.ItemReceived.RowId != 0))
                {
                    tooltipInfoByItemId[inspection.ItemReceived.RowId] = new SpecialContentTooltipInfo(
                        "Ishgardian Restoration",
                        BuildGathererRestorationLines(inspection));
                }
            }
        }

        return tooltipInfoByItemId;
    }

    private static IReadOnlyDictionary<uint, (string ExchangeName, string CostLabel)> BuildFolkloreExchangeInfoByBookItemId(
        ExcelSheet<SpecialShop>? specialShops)
    {
        if (specialShops is null)
            return new Dictionary<uint, (string ExchangeName, string CostLabel)>();

        var exchangesByBookItemId = new Dictionary<uint, List<(string ExchangeName, string CostLabel)>>();
        foreach (var shop in specialShops.Where(shop => shop.RowId != 0))
        {
            var shopName = shop.Name.ToString().Trim();
            foreach (var entry in shop.Item)
            {
                var receiveItem = entry.ReceiveItems
                    .Select(receive => receive.Item.RowId)
                    .FirstOrDefault(itemId => itemId != 0);
                if (receiveItem == 0)
                    continue;

                var costLabel = BuildSpecialShopCostLabel(shop, entry.ItemCosts);
                if (string.IsNullOrWhiteSpace(costLabel))
                    continue;

                var exchangeName = GetFolkloreExchangeName(shopName);
                if (!exchangesByBookItemId.TryGetValue(receiveItem, out var exchanges))
                {
                    exchanges = [];
                    exchangesByBookItemId[receiveItem] = exchanges;
                }

                exchanges.Add((exchangeName, costLabel));
            }
        }

        return exchangesByBookItemId.Keys.ToDictionary(
            bookItemId => bookItemId,
            bookItemId => (
                SelectPreferredFolkloreExchange(exchangesByBookItemId[bookItemId])));
    }

    private static (string ExchangeName, string CostLabel) SelectPreferredFolkloreExchange(
        IEnumerable<(string ExchangeName, string CostLabel)> exchanges)
    {
        var candidates = exchanges
            .Where(exchange =>
                !exchange.CostLabel.Contains("Regional Folklore Trader's Token", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0)
            candidates = exchanges.ToList();

        return candidates
            .OrderByDescending(exchange =>
                exchange.CostLabel.Contains("Purple Gatherers' Scrip", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(exchange =>
                exchange.CostLabel.Contains("Gatherers' Scrip", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(exchange =>
                exchange.CostLabel.Contains("Scrip", StringComparison.OrdinalIgnoreCase))
            .ThenBy(exchange => exchange.ExchangeName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(exchange => exchange.CostLabel, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static string GetFolkloreExchangeName(string shopName) =>
        shopName.Equals("Folklore Items", StringComparison.OrdinalIgnoreCase)
            ? "Fieldcraft Items, Folklore Items"
            : string.IsNullOrWhiteSpace(shopName)
                ? "Scrip Exchange"
                : shopName;

    private static string BuildSpecialShopCostLabel(
        SpecialShop shop,
        Lumina.Excel.Collection<SpecialShop.ItemStruct.ItemCostsStruct> itemCosts)
    {
        var costs = new List<(uint Amount, byte CostType, string ItemName)>();
        foreach (var cost in itemCosts)
        {
            try
            {
                if (cost.CurrencyCost == 0)
                    continue;

                costs.Add((cost.CurrencyCost, cost.CostType, TryGetItemName(cost.ItemCost).Trim()));
            }
            catch (NullReferenceException)
            {
                // Some unused generated SpecialShop rows have no backing cost data.
            }
        }

        var purpleGathererScrip = costs.FirstOrDefault(cost =>
            cost.ItemName.Contains("Purple Gatherers' Scrip", StringComparison.OrdinalIgnoreCase));
        if (purpleGathererScrip.Amount != 0)
            return $"{purpleGathererScrip.Amount:N0} {purpleGathererScrip.ItemName}";

        var shopName = shop.Name.ToString().Trim();
        var folkloreShardCost = costs.FirstOrDefault(cost =>
            shopName.Contains("Folklore Items", StringComparison.OrdinalIgnoreCase) &&
            cost.ItemName.EndsWith("Shard", StringComparison.OrdinalIgnoreCase));
        if (folkloreShardCost.Amount != 0)
            return $"{folkloreShardCost.Amount:N0} Purple Gatherers' Scrips";

        foreach (var costEntry in costs)
        {
            var itemCostName = costEntry.ItemName;
            if (!string.IsNullOrWhiteSpace(itemCostName) &&
                itemCostName.Contains("Scrip", StringComparison.OrdinalIgnoreCase))
                return $"{costEntry.Amount:N0} {itemCostName}";

            var inferredCurrencyName = InferSpecialShopCurrencyName(
                shopName,
                costEntry.CostType);
            if (!string.IsNullOrWhiteSpace(inferredCurrencyName))
                return $"{costEntry.Amount:N0} {inferredCurrencyName}";

            if (!string.IsNullOrWhiteSpace(itemCostName))
                return $"{costEntry.Amount:N0} {itemCostName}";
        }

        return string.Empty;
    }

    private static string InferSpecialShopCurrencyName(string shopName, byte costType)
    {
        if (costType != 3 || string.IsNullOrWhiteSpace(shopName))
            return string.Empty;

        if (shopName.Contains("Purple Scrip", StringComparison.OrdinalIgnoreCase))
            return "Purple Scrips";

        if (shopName.Contains("Orange Scrip", StringComparison.OrdinalIgnoreCase))
            return "Orange Scrips";

        if (shopName.Contains("White Scrip", StringComparison.OrdinalIgnoreCase))
            return "White Scrips";

        if (shopName.Contains("Crafters'", StringComparison.OrdinalIgnoreCase))
            return "Crafters' Scrips";

        if (shopName.Contains("Gatherers'", StringComparison.OrdinalIgnoreCase))
            return "Gatherers' Scrips";

        if (shopName.Contains("Scrip", StringComparison.OrdinalIgnoreCase))
            return "Scrips";

        return string.Empty;
    }

    private static string TryGetItemName(Lumina.Excel.RowRef<Item> itemRef)
    {
        try
        {
            return itemRef.RowId == 0
                ? string.Empty
                : itemRef.Value.Name.ToString();
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static bool IsToolItem(Item item) =>
        item.ItemUICategory.RowId == 28 ||
        item.Name.ToString().Contains("Pickaxe", StringComparison.OrdinalIgnoreCase) ||
        item.Name.ToString().Contains("Hatchet", StringComparison.OrdinalIgnoreCase) ||
        item.Name.ToString().Contains("Rod", StringComparison.OrdinalIgnoreCase);

    private static HashSet<uint> BuildExcludedSearchItemIds(
        ExcelSheet<Item>? items,
        IReadOnlyDictionary<uint, SpecialContentTooltipInfo>? specialContentTooltipInfoByItemId)
    {
        if (items is null)
            return [];

        var activeSpecialContentItemIds = specialContentTooltipInfoByItemId?.Keys.ToHashSet() ?? [];
        return items
            .Where(item => item.RowId != 0)
            .Where(item =>
            {
                var itemName = item.Name.ToString();
                if (string.IsNullOrWhiteSpace(itemName))
                    return false;

                // Old Restoration-only items can linger in raw sheets after they stop being obtainable.
                return itemName.Contains("Skybuilders'", StringComparison.OrdinalIgnoreCase) &&
                       !activeSpecialContentItemIds.Contains(item.RowId);
            })
            .Select(item => item.RowId)
            .ToHashSet();
    }

    private bool IsSearchVisible(uint itemId) =>
        itemId != 0 &&
        (this.excludedSearchItemIds is null || !this.excludedSearchItemIds.Contains(itemId));

    private static IReadOnlyList<string> BuildCrafterRestorationLines(
        HWDCrafterSupply.HWDCrafterSupplyParamsStruct supply)
    {
        var lines = new List<string>
        {
            "Crafted supply turn-in",
            $"Phase: {supply.TermName.Value.Name}",
            $"Level: {supply.Level}-{supply.LevelMax}",
            $"Skybuilders' Scrips: {FormatCrafterRewardTier(supply.BaseCollectableReward)} / {FormatCrafterRewardTier(supply.MidCollectableReward)} / {FormatCrafterRewardTier(supply.HighCollectableReward)}",
        };

        var pointsLine = FormatCrafterPointsLine(
            supply.BaseCollectableReward,
            supply.MidCollectableReward,
            supply.HighCollectableReward);
        if (!string.IsNullOrWhiteSpace(pointsLine))
            lines.Add(pointsLine);

        return lines;
    }

    private static IReadOnlyList<string> BuildGathererRestorationLines(
        HWDGathererInspection.HWDGathererInspectionDataStruct inspection)
    {
        var lines = new List<string>
        {
            "Gatherer inspection turn-in",
            $"Phase: {inspection.Phase.Value.Name}",
            $"Amount required: {inspection.AmountRequired}",
        };

        var rewardLabels = inspection.Reward
            .Where(reward => reward.RowId != 0)
            .Select(reward => FormatGathererReward(reward.Value))
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (rewardLabels.Count > 0)
            lines.Add($"Rewards: {string.Join(" | ", rewardLabels)}");

        return lines;
    }

    private static string FormatCrafterRewardTier(Lumina.Excel.RowRef<HWDCrafterSupplyReward> rewardRef)
    {
        try
        {
            var reward = rewardRef.Value;
            return reward.ScriptRewardAmount.ToString(CultureInfo.InvariantCulture);
        }
        catch (InvalidOperationException)
        {
            return "0";
        }
    }

    private static string FormatCrafterPointsLine(
        Lumina.Excel.RowRef<HWDCrafterSupplyReward> baseRewardRef,
        Lumina.Excel.RowRef<HWDCrafterSupplyReward> midRewardRef,
        Lumina.Excel.RowRef<HWDCrafterSupplyReward> highRewardRef)
    {
        try
        {
            var basePoints = baseRewardRef.Value.Points;
            var midPoints = midRewardRef.Value.Points;
            var highPoints = highRewardRef.Value.Points;
            if (basePoints == 0 && midPoints == 0 && highPoints == 0)
                return string.Empty;

            return $"Skyward Points: {basePoints} / {midPoints} / {highPoints}";
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static string FormatGathererReward(HWDGathererInspectionReward reward)
    {
        if (reward.Scrips == 0 && reward.Points == 0)
            return string.Empty;

        if (reward.Points == 0)
            return $"{reward.Scrips} Skybuilders' Scrips";

        if (reward.Scrips == 0)
            return $"{reward.Points} Skyward Points";

        return $"{reward.Scrips} Skybuilders' Scrips, {reward.Points} Skyward Points";
    }

    private static string MapGatheringTypeToJob(string gatheringTypeName)
    {
        if (string.IsNullOrWhiteSpace(gatheringTypeName))
            return string.Empty;

        return gatheringTypeName switch
        {
            "Quarrying" => "MIN",
            "Logging" or "Harvesting" => "BTN",
            _ when gatheringTypeName.Contains("銛", StringComparison.Ordinal) => "FSH",
            _ => string.Empty,
        };
    }

    private static void AddGatheringJob(
        IDictionary<uint, HashSet<string>> jobsByItemId,
        uint itemId,
        string job)
    {
        if (!jobsByItemId.TryGetValue(itemId, out var jobs))
        {
            jobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            jobsByItemId[itemId] = jobs;
        }

        jobs.Add(job);
    }

    private static void AddGatheringLevel(
        IDictionary<uint, HashSet<uint>> levelsByItemId,
        uint itemId,
        uint level)
    {
        if (!levelsByItemId.TryGetValue(itemId, out var levels))
        {
            levels = [];
            levelsByItemId[itemId] = levels;
        }

        levels.Add(level);
    }

    private string GetItemName(uint itemId)
    {
        var item = this.dataManager.GetExcelSheet<Item>()?.GetRow(itemId);
        return item?.Name.ToString() ?? $"Item #{itemId}";
    }

    private uint ReadIndexedItemId<T>(T row, string memberName, int index)
    {
        var value = ReadIndexedValue(ReadMember(row, memberName), index);
        return this.ReadItemIdFromValue(value);
    }

    private uint ReadIndexedUInt<T>(T row, string memberName, int index)
    {
        return ConvertToUInt(ReadIndexedValue(ReadMember(row, memberName), index));
    }

    private uint ReadItemId<T>(T row, string memberName)
    {
        return this.ReadItemIdFromValue(ReadMember(row, memberName));
    }

    private uint ReadItemIdFromValue(object? value)
    {
        if (value is null)
            return 0;

        var rowIdMember = value.GetType().GetProperty("RowId", BindingFlags.Instance | BindingFlags.Public);
        if (rowIdMember?.GetValue(value) is uint rowId)
            return rowId;

        var valueMember = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
        if (valueMember?.GetValue(value) is { } nestedValue)
            return this.ReadItemIdFromValue(nestedValue);

        return ConvertToUInt(value);
    }

    private uint ReadUInt<T>(T row, string memberName) => ConvertToUInt(ReadMember(row, memberName));

    private static object? ReadMember<T>(T row, string memberName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var type = typeof(T);
        return type.GetProperty(memberName, flags)?.GetValue(row)
            ?? type.GetField(memberName, flags)?.GetValue(row);
    }

    private static object? ReadIndexedValue(object? collection, int index)
    {
        if (collection is null)
            return null;

        var type = collection.GetType();
        if (collection is System.Collections.IList list)
            return index >= 0 && index < list.Count ? list[index] : null;

        var lengthMember = type.GetProperty("Length", BindingFlags.Instance | BindingFlags.Public)
            ?? type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
        var length = ConvertToUInt(lengthMember?.GetValue(collection));
        if (length != 0 && index >= length)
            return null;

        var indexer = type.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, null, [typeof(int)], null);
        return indexer?.GetValue(collection, [index]);
    }

    private static uint ConvertToUInt(object? value)
    {
        return value switch
        {
            byte typed => typed,
            sbyte typed => typed < 0 ? 0U : (uint)typed,
            short typed => typed < 0 ? 0U : (uint)typed,
            ushort typed => typed,
            int typed => typed < 0 ? 0U : (uint)typed,
            uint typed => typed,
            long typed => typed < 0 ? 0U : (uint)typed,
            ulong typed => typed > uint.MaxValue ? uint.MaxValue : (uint)typed,
            _ => 0,
        };
    }

    private static uint MultiplySaturating(uint value, uint multiplier)
    {
        return (uint)Math.Min((ulong)value * multiplier, uint.MaxValue);
    }

    private static ulong MultiplySaturating(ulong value, ulong multiplier)
    {
        if (value == 0 || multiplier == 0)
            return 0;
        return value > ulong.MaxValue / multiplier
            ? ulong.MaxValue
            : value * multiplier;
    }

    private static ulong AddSaturating(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static uint AddSaturating(uint left, uint right) =>
        uint.MaxValue - left < right ? uint.MaxValue : left + right;

    private static uint CapInitialCatalystWithdrawal(uint itemId, ulong liveOwned, uint missing)
    {
        if (!IsElementalCatalystItem(itemId))
            return missing;

        if (liveOwned >= CrystalInventoryCap)
            return 0;

        return (uint)Math.Min((ulong)missing, CrystalInventoryCap - liveOwned);
    }

    private static bool IsElementalCatalystItem(uint itemId) =>
        itemId is >= 2 and <= 19;

    private static void AddOrMergeDreamTarget(
        IList<RetainerWithdrawalTarget> targets,
        RetainerWithdrawalTarget target)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var existing = targets[i];
            if (existing.RetainerId != target.RetainerId || existing.ItemId != target.ItemId)
                continue;

            targets[i] = existing with
            {
                WithdrawQuantity = AddSaturating(existing.WithdrawQuantity, target.WithdrawQuantity),
            };
            return;
        }

        targets.Add(target);
    }

    private sealed class RetainerStockState
    {
        private readonly Dictionary<uint, ulong> remainingQuantities;
        private readonly Dictionary<uint, uint> snapshotQuantities;

        public RetainerStockState(StoredRetainerInventory retainer)
        {
            this.RetainerId = retainer.RetainerId;
            this.Name = retainer.Name;
            this.remainingQuantities = retainer.Items.ToDictionary(
                entry => entry.Key,
                entry => (ulong)entry.Value.NqQuantity + entry.Value.HqQuantity);
            this.snapshotQuantities = retainer.Items.ToDictionary(
                entry => entry.Key,
                entry => AddSaturating(entry.Value.NqQuantity, entry.Value.HqQuantity));
        }

        public ulong RetainerId { get; }

        public string Name { get; }

        public bool TryWithdraw(uint itemId, ulong requested, out uint withdrawn, out uint snapshotQuantity)
        {
            snapshotQuantity = this.snapshotQuantities.GetValueOrDefault(itemId);
            if (!this.remainingQuantities.TryGetValue(itemId, out var available) || available == 0 || requested == 0)
            {
                withdrawn = 0;
                return false;
            }

            var taken = Math.Min(available, requested);
            this.remainingQuantities[itemId] = available - taken;
            withdrawn = (uint)Math.Min(taken, uint.MaxValue);
            return withdrawn > 0;
        }
    }
}
