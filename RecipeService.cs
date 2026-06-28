using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace DalamudRecipeHelper;

public sealed class RecipeService
{
    private readonly IDataManager dataManager;
    private readonly FileLogService fileLog;
    private readonly AetherialReductionService aetherialReductionService;
    private HashSet<uint>? craftableItemIds;
    private HashSet<uint>? gatherableItemIds;
    private HashSet<uint>? fishingItemIds;
    private HashSet<uint>? vendorItemIds;
    private IReadOnlyDictionary<uint, IReadOnlyList<MaterialRecipeUsage>>? recipesByIngredient;

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
        var recipes = this.dataManager.GetExcelSheet<Recipe>();
        if (recipes is null)
            return [];

        var cleanQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(cleanQuery))
            return [];

        var results = recipes
            .Select(this.TryCreateMatch)
            .Where(match => match is not null)
            .Select(match => match!)
            .Where(match =>
                match.ResultName.Contains(cleanQuery, StringComparison.CurrentCultureIgnoreCase) ||
                match.ResultItemId.ToString(CultureInfo.InvariantCulture) == cleanQuery)
            .OrderBy(match => match.ResultName.StartsWith(cleanQuery, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(match => match.ResultName)
            .ToList();
        this.fileLog.Info("Recipes", $"Search '{cleanQuery}' returned {results.Count} result(s).");
        return results;
    }

    public IReadOnlyList<CraftableRecipeAvailability> GetCraftableRecipes(
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
    {
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
            .Select(group => group
                .OrderByDescending(availability => availability.CraftCount)
                .ThenBy(availability => availability.Recipe.RecipeId)
                .First())
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
        var rawAmounts = new Dictionary<uint, ulong>();
        foreach (var ingredient in ingredients)
        {
            if (recipesByResult.TryGetValue(ingredient.ItemId, out var ingredientRecipe))
            {
                var resultAmount = Math.Max(1U, this.ReadUInt(ingredientRecipe, "AmountResult"));
                var craftCount =
                    ((ulong)ingredient.Required + resultAmount - 1) / resultAmount;
                this.ExpandRawMaterials(
                    ingredientRecipe,
                    craftCount,
                    recipesByResult,
                    rawAmounts,
                    new HashSet<uint>());
            }
            else
            {
                rawAmounts.TryGetValue(ingredient.ItemId, out var current);
                rawAmounts[ingredient.ItemId] = current + ingredient.Required;
            }
        }

        var rawMaterials = this.CreateIngredientNeeds(
            rawAmounts
                .Select(entry => (
                    entry.Key,
                    (uint)Math.Min(entry.Value, uint.MaxValue)))
                .OrderBy(entry => this.GetItemName(entry.Key)),
            ownedItems);

        this.fileLog.Info(
            "Recipes",
            $"Calculated combined plan with {recipeDetails.Count} recipe(s), {ingredients.Count} direct material(s), and {rawMaterials.Count} raw material(s).");
        return new RecipePlanDetails(recipeDetails, ingredients, rawMaterials);
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
        var amounts = new Dictionary<uint, ulong>();
        var recipePath = new HashSet<uint>();
        this.ExpandRawMaterials(rootRecipe, craftCount, recipesByResult, amounts, recipePath);

        var rawIngredients = amounts
            .Select(entry => (entry.Key, (uint)Math.Min(entry.Value, uint.MaxValue)))
            .OrderBy(entry => this.GetItemName(entry.Key))
            .ToList();

        return this.CreateIngredientNeeds(rawIngredients, ownedItems);
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
            var rawAmounts = new Dictionary<uint, ulong>();
            this.ExpandRawMaterials(
                recipe,
                craftCount,
                recipesByResult,
                rawAmounts,
                new HashSet<uint>());

            var canCraft = rawAmounts.All(raw =>
                ownedItems.TryGetValue(raw.Key, out var owned) &&
                owned.Quantity >= raw.Value);

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
        IReadOnlyDictionary<uint, OwnedInventoryItem> ownedItems)
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
                ReductionSources: reductionSources);
        }).ToList();
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

        this.craftableItemIds = this.dataManager.GetExcelSheet<Recipe>()?
            .Select(recipe => this.ReadItemId(recipe, "ItemResult"))
            .Where(itemId => itemId != 0)
            .ToHashSet() ?? [];

        this.gatherableItemIds = this.dataManager.GetExcelSheet<GatheringItem>()?
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

        this.vendorItemIds = this.dataManager.GetExcelSheet<Item>()?
            .Where(item => item.PriceMid > 0)
            .Select(item => item.RowId)
            .ToHashSet() ?? [];

        this.fileLog.Info(
            "Recipes",
            $"Loaded source indexes: {this.craftableItemIds.Count} craftable, {this.gatherableItemIds.Count} gatherable, {this.fishingItemIds.Count} fishing, {this.vendorItemIds.Count} vendor.");
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
        if (resultItemId == 0)
            return null;

        var resultName = this.GetItemName(resultItemId);
        if (string.IsNullOrWhiteSpace(resultName))
            return null;

        var amount = this.ReadUInt(recipe, "AmountResult");
        return new RecipeMatch(recipe.RowId, resultItemId, resultName, Math.Max(1, amount));
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
}
