using System;
using System.Collections.Generic;
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
        Draw(
            marketboardPriceService,
            configuration,
            material.ItemId,
            material.Name);
    }

    public static void Draw(
        MarketboardPriceService marketboardPriceService,
        Configuration configuration,
        uint itemId,
        string itemName,
        string? footerText = null,
        uint quantity = 1,
        IReadOnlyList<string>? detailLines = null,
        SpecialContentTooltipInfo? specialContentTooltipInfo = null)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.SetNextWindowSizeConstraints(
            new Vector2(360, 0),
            new Vector2(980, 600));
        ImGui.BeginTooltip();
        WindowTheme.ApplyTextScale(configuration);

        ImGui.TextColored(configuration.AccentTextColor, itemName);
        if (specialContentTooltipInfo is not null)
        {
            ImGui.TextDisabled(specialContentTooltipInfo.Subtitle);
        }
        else
        {
            ImGui.TextDisabled("Marketboard snapshot via Universalis");
            if (quantity > 1)
                ImGui.TextDisabled($"Scaled for quantity: {quantity:N0}");
        }
        ImGui.Separator();

        if (specialContentTooltipInfo is not null)
        {
            foreach (var line in specialContentTooltipInfo.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
                ImGui.TextUnformatted(line);
        }
        else
        {
            var priceState = marketboardPriceService.GetState(itemId);
            if (priceState.Snapshot is not { } snapshot)
            {
                ImGui.TextDisabled(priceState.IsLoading
                    ? "Loading current prices..."
                    : "No marketboard data is available.");
            }
            else
            {
                if (snapshot.LastUploadTime is { } lastUpload)
                    ImGui.TextDisabled($"Last upload: {lastUpload:yyyy-MM-dd HH:mm}");

                if (quantity > 1)
                {
                    if (snapshot.CurrentAveragePrice > 0)
                        ImGui.TextUnformatted($"Average NQ total: {Math.Round(snapshot.CurrentAveragePrice * quantity):N0} gil");
                    if (snapshot.CurrentAveragePriceHq is { } averagePriceHq)
                        ImGui.TextUnformatted($"Average HQ total: {Math.Round(averagePriceHq * quantity):N0} gil");
                }

                if (snapshot.NqWorldPrices.Count > 0 || snapshot.HqWorldPrices.Count > 0)
                {
                    ImGui.Spacing();
                    var columnCount = snapshot.NqWorldPrices.Count > 0 && snapshot.HqWorldPrices.Count > 0
                        ? 2
                        : 1;
                    if (ImGui.BeginTable(
                            "##universalis-tooltip-worlds",
                            columnCount,
                            ImGuiTableFlags.SizingStretchSame))
                    {
                        if (snapshot.NqWorldPrices.Count > 0)
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextColored(
                                configuration.AccentTextColor,
                                quantity > 1 ? "Cheapest NQ Totals by World" : "Cheapest NQ by World");
                            foreach (var worldPrice in snapshot.NqWorldPrices)
                                DrawWorldPriceLine(configuration, worldPrice, quantity);
                        }

                        if (snapshot.HqWorldPrices.Count > 0)
                        {
                            ImGui.TableNextColumn();
                            ImGui.TextColored(
                                configuration.AccentTextColor,
                                quantity > 1 ? "Cheapest HQ Totals by World" : "Cheapest HQ by World");
                            foreach (var worldPrice in snapshot.HqWorldPrices)
                                DrawWorldPriceLine(configuration, worldPrice, quantity);
                        }

                        ImGui.EndTable();
                    }
                }

                if (priceState.IsLoading)
                    ImGui.TextDisabled("Refreshing...");
            }
        }

        if (!string.IsNullOrWhiteSpace(footerText))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled(footerText);
        }

        if (detailLines is { Count: > 0 })
        {
            var detailColor = WindowTheme.GetTooltipDetailTextColor(configuration);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            foreach (var detailLine in detailLines.Where(line => !string.IsNullOrWhiteSpace(line)))
                ImGui.TextColored(detailColor, detailLine);
            ImGui.PopTextWrapPos();
        }

        ImGui.EndTooltip();
    }

    private static void DrawWorldPriceLine(
        Configuration configuration,
        MarketboardPriceService.WorldPriceSnapshot worldPrice,
        uint quantity)
    {
        if (quantity <= 1)
        {
            ImGui.TextUnformatted($"- {worldPrice.WorldName}: {worldPrice.PricePerUnit:N0} gil");
            return;
        }

        ImGui.TextUnformatted(
            $"- {worldPrice.WorldName}: {(ulong)worldPrice.PricePerUnit * quantity:N0} gil total");
        ImGui.SameLine(0f, 0f);
        ImGui.PushStyleColor(ImGuiCol.Text, configuration.WarningTextColor);
        ImGui.TextUnformatted($" ({worldPrice.PricePerUnit:N0} each)");
        ImGui.PopStyleColor();
    }
}
