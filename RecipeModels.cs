using System;
using System.Collections.Generic;
using System.Linq;

namespace DalamudRecipeHelper;

public sealed record RecipeMatch(uint RecipeId, uint ResultItemId, string ResultName, uint ResultAmount);

public sealed record RecipePlanSelection(RecipeMatch Recipe, uint DesiredAmount);

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
    IReadOnlyList<AetherialReductionSource>? ReductionSources = null)
{
    public uint Owned => (uint)Math.Min((ulong)this.OwnedNq + this.OwnedHq, uint.MaxValue);
    public IReadOnlyList<string> Locations =>
        this.NqLocations
            .Concat(this.HqLocations)
            .Distinct()
            .ToList();
    public uint Missing => this.Required > this.Owned ? this.Required - this.Owned : 0;
    public bool HasEnough => this.Missing == 0;
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

public sealed record RecipePlanDetails(
    IReadOnlyList<RecipeDetails> Recipes,
    IReadOnlyList<IngredientNeed> Ingredients,
    IReadOnlyList<IngredientNeed> RawMaterials);
