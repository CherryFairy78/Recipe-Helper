using System;
using System.Collections.Generic;
using System.Linq;

namespace DalamudRecipeHelper;

public enum SearchResultKind
{
    CraftedRecipe,
    CollectibleItem,
    GatherableItem,
}

public sealed record RecipeMatch(
    uint RecipeId,
    uint ResultItemId,
    string ResultName,
    uint ResultAmount,
    string JobAbbreviations,
    SearchResultKind ResultKind = SearchResultKind.CraftedRecipe,
    string SearchMetadata = "",
    bool CanAddToPlan = true);

public sealed record CraftableRecipeAvailability(
    RecipeMatch Recipe,
    uint CraftCount,
    ulong OutputAmount);

public sealed record RecipePlanSelection(RecipeMatch Recipe, uint DesiredAmount);

public sealed class SavedRecipePlan
{
    public string Name { get; set; } = string.Empty;

    public string FolderName { get; set; } = string.Empty;

    public List<SavedRecipePlanEntry> Recipes { get; set; } = [];

    public List<SavedSupplementalPlanEntry> Gatherables { get; set; } = [];

    public List<SavedSupplementalPlanEntry> Collectables { get; set; } = [];

    public List<SavedSupplementalPlanEntry> DirectIngredients { get; set; } = [];

    public List<SavedSupplementalPlanEntry> RawMaterials { get; set; } = [];
}

public sealed class SavedRecipePlanEntry
{
    public uint RecipeId { get; set; }

    public uint ResultItemId { get; set; }

    public string ResultName { get; set; } = string.Empty;

    public uint ResultAmount { get; set; }

    public string JobAbbreviations { get; set; } = string.Empty;

    public uint DesiredAmount { get; set; }
}

public sealed class SavedSupplementalPlanEntry
{
    public uint ResultItemId { get; set; }

    public string ResultName { get; set; } = string.Empty;

    public string JobAbbreviations { get; set; } = string.Empty;

    public string SearchMetadata { get; set; } = string.Empty;

    public uint DesiredAmount { get; set; }
}

public sealed record MaterialRecipeUsage(
    uint RecipeId,
    string ResultName,
    uint IngredientAmount,
    uint ResultAmount);

public sealed record EorzeaNodeWindow(int StartMinute, int EndMinute);

public sealed record AetherialReductionSource(
    uint ItemId,
    string Name,
    bool IsFishing,
    IReadOnlyList<EorzeaNodeWindow> Windows);

public sealed record CollectibleRewardInfo(
    string CurrencyLabel,
    uint BaseReward,
    uint BonusRewardOne,
    uint BonusRewardTwo)
{
    public string DisplayLabel => $"{this.BaseReward} {this.CurrencyLabel}";

    public string FormatTotal(uint quantity) =>
        $"{this.FormatRewardTotal(this.BaseReward, quantity)} {this.CurrencyLabel}";

    public string GetTooltipText() =>
        this.GetTooltipTextCore(this.BaseReward, this.BonusRewardOne, this.BonusRewardTwo);

    public string GetTotalTooltipText(uint quantity) =>
        this.GetTooltipTextCore(
            this.FormatRewardTotal(this.BaseReward, quantity),
            this.FormatRewardTotal(this.BonusRewardOne, quantity),
            this.FormatRewardTotal(this.BonusRewardTwo, quantity));

    private string GetTooltipTextCore(uint baseReward, uint bonusRewardOne, uint bonusRewardTwo) =>
        this.GetTooltipTextCore(
            baseReward.ToString(),
            bonusRewardOne.ToString(),
            bonusRewardTwo.ToString());

    private string GetTooltipTextCore(string baseReward, string bonusRewardOne, string bonusRewardTwo)
    {
        var lines = new List<string>
        {
            $"Base value hand-in: {baseReward} {this.CurrencyLabel}",
        };

        if (this.BonusRewardOne > 0 && this.BonusRewardOne != this.BaseReward)
            lines.Add($"Quality bonus 1: {bonusRewardOne} {this.CurrencyLabel}");

        if (this.BonusRewardTwo > 0 &&
            this.BonusRewardTwo != this.BonusRewardOne &&
            this.BonusRewardTwo != this.BaseReward)
            lines.Add($"Quality bonus 2: {bonusRewardTwo} {this.CurrencyLabel}");

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatRewardTotal(uint reward, uint quantity) =>
        Math.Min((ulong)reward * quantity, uint.MaxValue).ToString();
}

public sealed record FolkloreBookInfo(
    string BookName,
    string ExchangeName,
    string CostLabel);

public sealed record MasterRecipeBookInfo(string BookName);

public sealed record RequiredItemInfo(
    string ItemName,
    bool IsTool);

public sealed record FishTooltipInfo(
    string BaitName,
    string FishType,
    string BestZone,
    string BestSpot);

public sealed record LogStatusTooltipInfo(IReadOnlyList<string> Lines);

public sealed record SocietyQuestTooltipInfo(IReadOnlyList<string> Lines);

public sealed record CosmicExplorationTooltipInfo(string MissionName);

public sealed record QuestTooltipInfo(IReadOnlyList<string> Lines);

public sealed record SpecialContentTooltipInfo(
    string Subtitle,
    IReadOnlyList<string> Lines);

public sealed record StoredRetainerItem(uint NqQuantity, uint HqQuantity);

public sealed record StoredRetainerInventory(
    ulong RetainerId,
    string Name,
    DateTimeOffset CapturedAt,
    IReadOnlyDictionary<uint, StoredRetainerItem> Items);

public sealed record OwnedInventoryItem(
    uint NqQuantity,
    uint HqQuantity,
    IReadOnlyList<string> NqLocations,
    IReadOnlyList<string> HqLocations)
{
    public uint Quantity => (uint)Math.Min((ulong)this.NqQuantity + this.HqQuantity, uint.MaxValue);
}

public sealed record GatheringDestination(
    uint ItemId,
    uint TerritoryId,
    string ZoneName,
    string LocationName,
    uint? AetheryteId,
    byte AetheryteSubIndex,
    string? AetheryteName,
    uint TeleportCost,
    uint? MapId,
    float? X,
    float? Z);

public sealed record IngredientNeed(
    uint ItemId,
    string Name,
    uint Required,
    uint OwnedNq,
    uint OwnedHq,
    string Source,
    bool IsGatherable,
    bool IsFishing,
    IReadOnlyList<string> NqLocations,
    IReadOnlyList<string> HqLocations,
    bool? CanCraftMissingFromRaw = null,
    uint? RawCraftRecipeId = null,
    uint RawCraftCount = 0,
    IReadOnlyList<AetherialReductionSource>? ReductionSources = null,
    uint PreCraftCoveredAmount = 0,
    IReadOnlyList<string>? PreCraftCoverageNames = null)
{
    public uint Owned => (uint)Math.Min((ulong)this.OwnedNq + this.OwnedHq, uint.MaxValue);
    public IReadOnlyList<string> Locations =>
        this.NqLocations
            .Concat(this.HqLocations)
            .Distinct()
            .ToList();
    public uint Missing => this.Required > this.Owned ? this.Required - this.Owned : 0;
    public bool HasEnough => this.Missing == 0;
    public bool IsFullyCoveredByOwnedPreCraft =>
        this.Required > 0 && this.PreCraftCoveredAmount >= this.Required;
}

public sealed record RecipeDetails(
    uint RecipeId,
    uint ResultItemId,
    string ResultName,
    uint ResultAmount,
    uint DesiredAmount,
    uint CraftCount,
    uint ProducedAmount,
    IReadOnlyList<IngredientNeed> Ingredients,
    IReadOnlyList<IngredientNeed> RawMaterials,
    string DebugInfo = "");

public sealed record ArtisanCraftQueueEntry(
    uint RecipeId,
    uint ResultItemId,
    string ResultName,
    uint ResultAmount,
    uint CraftCount,
    bool IsIntermediate,
    int QueueSequence = 0)
{
    public ulong TotalQuantity => (ulong)this.ResultAmount * this.CraftCount;
}

public sealed record ArtisanCraftProgressSnapshot(
    bool IsActive,
    ArtisanCraftQueueEntry? CurrentEntry,
    bool CurrentEntryStarted,
    uint CurrentEntryCompletedCrafts,
    bool IsPausedForAutoRetainer,
    bool IsPausedForRetainerRefill,
    bool StopAfterCurrentCraftRequested,
    TimeSpan Elapsed,
    TimeSpan CurrentEntryElapsed,
    IReadOnlyList<ArtisanCraftQueueEntry> OrderedEntries,
    IReadOnlyList<ArtisanCraftQueueEntry> PendingEntries,
    IReadOnlyList<ArtisanCraftQueueEntry> CompletedEntries);

public sealed record RecipePlanDetails(
    IReadOnlyList<RecipeDetails> Recipes,
    IReadOnlyList<IngredientNeed> Ingredients,
    IReadOnlyList<IngredientNeed> RawMaterials);

public sealed record RetainerWithdrawalTarget(
    ulong RetainerId,
    string RetainerName,
    uint ItemId,
    string ItemName,
    uint WithdrawQuantity,
    uint SnapshotQuantity);
