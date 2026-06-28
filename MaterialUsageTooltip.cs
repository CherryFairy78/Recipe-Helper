using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DalamudRecipeHelper;

public static class MaterialUsageTooltip
{
    private const int MaximumDisplayedRecipes = 18;

    public static void Draw(
        RecipeService recipeService,
        Configuration configuration,
        IngredientNeed material)
    {
        if (!ImGui.IsItemHovered())
            return;

        var usages = recipeService.GetRecipesUsing(material.ItemId);
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(300, 0),
            new Vector2(540, 460));
        ImGui.BeginTooltip();

        ImGui.TextColored(configuration.AccentColor, material.Name);
        ImGui.TextDisabled($"Used directly in {usages.Count} recipe(s)");
        ImGui.Separator();

        if (usages.Count == 0)
        {
            ImGui.TextDisabled("No direct recipe uses were found.");
        }
        else
        {
            foreach (var usage in usages.Take(MaximumDisplayedRecipes))
            {
                var yield = usage.ResultAmount > 1 ? $"  |  yields {usage.ResultAmount}" : string.Empty;
                ImGui.TextUnformatted(
                    $"- {usage.ResultName}  |  uses {usage.IngredientAmount}{yield}");
            }

            if (usages.Count > MaximumDisplayedRecipes)
            {
                ImGui.Spacing();
                ImGui.TextDisabled(
                    $"+ {usages.Count - MaximumDisplayedRecipes} more recipe(s)");
            }
        }

        ImGui.EndTooltip();
    }
}
