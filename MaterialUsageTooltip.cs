using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace DalamudRecipeHelper;

public static class MaterialUsageTooltip
{
    public static void Draw(
        MarketboardPriceService marketboardPriceService,
        Configuration configuration,
        IngredientNeed material)
    {
        if (!ImGui.IsItemHovered())
            return;

        var priceState = marketboardPriceService.GetState(material.ItemId);
        ImGui.SetNextWindowSizeConstraints(
            new Vector2(320, 0),
            new Vector2(520, 420));
        ImGui.BeginTooltip();
        WindowTheme.ApplyTextScale(configuration);

        ImGui.TextColored(configuration.AccentTextColor, material.Name);
        ImGui.TextDisabled("Marketboard snapshot via Universalis");
        ImGui.Separator();

        if (priceState.Snapshot is not { } snapshot)
        {
            ImGui.TextDisabled(priceState.IsLoading
                ? "Loading current prices..."
                : "No marketboard data is available.");
        }
        else
        {
            var lineCount =
                1 +
                (snapshot.LastUploadTime is not null ? 1 : 0) +
                (snapshot.NqWorldPrices.Count > 0 ? snapshot.NqWorldPrices.Count + 1 : 0) +
                (snapshot.HqWorldPrices.Count > 0 ? snapshot.HqWorldPrices.Count + 1 : 0) +
                (priceState.IsLoading ? 1 : 0);
            var childHeight = Math.Min(280f, (lineCount * ImGui.GetTextLineHeightWithSpacing()) + 6f);
            if (ImGui.BeginChild("##universalis-tooltip-scroll", new Vector2(0, childHeight), false))
            {
                if (snapshot.LastUploadTime is { } lastUpload)
                    ImGui.TextDisabled($"Last upload: {lastUpload:yyyy-MM-dd HH:mm}");

                if (snapshot.NqWorldPrices.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(configuration.AccentTextColor, "Cheapest NQ by World");
                    foreach (var worldPrice in snapshot.NqWorldPrices)
                        ImGui.TextUnformatted($"- {worldPrice.WorldName}: {worldPrice.PricePerUnit:N0} gil");
                }

                if (snapshot.HqWorldPrices.Count > 0)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(configuration.AccentTextColor, "Cheapest HQ by World");
                    foreach (var worldPrice in snapshot.HqWorldPrices)
                        ImGui.TextUnformatted($"- {worldPrice.WorldName}: {worldPrice.PricePerUnit:N0} gil");
                }

                if (priceState.IsLoading)
                    ImGui.TextDisabled("Refreshing...");
            }
            ImGui.EndChild();
        }

        ImGui.EndTooltip();
    }
}
