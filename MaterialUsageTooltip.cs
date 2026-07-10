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
        SpecialContentTooltipInfo? specialContentTooltipInfo = null,
        FishTooltipInfo? fishTooltipInfo = null,
        SocietyQuestTooltipInfo? societyQuestTooltipInfo = null,
        CosmicExplorationTooltipInfo? cosmicExplorationTooltipInfo = null,
        QuestTooltipInfo? questTooltipInfo = null,
        LogStatusTooltipInfo? logStatusTooltipInfo = null,
        bool isMarketboardAvailable = true)
    {
        if (!ImGui.IsItemHovered())
            return;

        var priceState = specialContentTooltipInfo is null && isMarketboardAvailable
            ? marketboardPriceService.GetState(itemId)
            : (MarketboardPriceService.MarketboardPriceState?)null;

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
        else if (!isMarketboardAvailable || priceState is { IsMarketboardAvailable: false })
        {
            ImGui.TextDisabled("Not tradable on the marketboard");
        }
        else
        {
            ImGui.TextDisabled("Marketboard snapshot via Universalis");
            if (quantity > 1)
                ImGui.TextDisabled($"Scaled for quantity: {quantity:N0}");
        }
        if (specialContentTooltipInfo is not null || priceState is { IsMarketboardAvailable: not false })
            ImGui.Separator();

        if (specialContentTooltipInfo is not null)
        {
            foreach (var line in specialContentTooltipInfo.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
                DrawDetailLine(configuration, line);
        }
        else if (priceState is { IsMarketboardAvailable: not false } availablePriceState)
        {
            if (availablePriceState.Snapshot is not { } snapshot)
            {
                ImGui.TextDisabled(availablePriceState.IsLoading
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

                if (availablePriceState.IsLoading)
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
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            foreach (var detailLine in detailLines.Where(line => !string.IsNullOrWhiteSpace(line)))
            {
                if (detailLine.Contains(':'))
                    DrawDetailLine(configuration, detailLine);
                else
                    DrawDetailHeader(configuration, detailLine);
            }
            ImGui.PopTextWrapPos();
        }

        if (fishTooltipInfo is not null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(configuration.AccentTextColor, "Fishing details");
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            DrawDetailRow(configuration, "Bait", fishTooltipInfo.BaitName);
            DrawDetailRow(configuration, "Fish type", fishTooltipInfo.FishType);
            DrawDetailRow(configuration, "Best zone", fishTooltipInfo.BestZone);
            DrawDetailRow(configuration, "Best spot", fishTooltipInfo.BestSpot);
            ImGui.PopTextWrapPos();
        }

        DrawLogStatusTooltipDetails(configuration, logStatusTooltipInfo);

        if (societyQuestTooltipInfo is not null &&
            (!isMarketboardAvailable || priceState is { IsMarketboardAvailable: false }))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(configuration.AccentTextColor, "Society quests");
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            foreach (var line in societyQuestTooltipInfo.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
                DrawDetailLine(configuration, line);
            ImGui.PopTextWrapPos();
        }

        if (cosmicExplorationTooltipInfo is not null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(configuration.AccentTextColor, "Cosmic Exploration");
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            DrawDetailLine(
                configuration,
                string.IsNullOrWhiteSpace(cosmicExplorationTooltipInfo.MissionName)
                    ? "Cosmic Exploration item"
                    : $"Mission: {cosmicExplorationTooltipInfo.MissionName}");
            ImGui.PopTextWrapPos();
        }

        if (questTooltipInfo is not null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(configuration.AccentTextColor, "Quest item");
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
            foreach (var line in questTooltipInfo.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
                DrawDetailLine(configuration, line);
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

    public static void DrawDetailHeader(Configuration configuration, string title)
    {
        ImGui.TextColored(configuration.AccentTextColor, title);
        AddDetailRowSpacing();
    }

    public static void DrawLogStatusTooltipDetails(
        Configuration configuration,
        LogStatusTooltipInfo? logStatusTooltipInfo)
    {
        if (logStatusTooltipInfo is null || logStatusTooltipInfo.Lines.Count == 0)
            return;

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextColored(configuration.AccentTextColor, "Log status");
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        foreach (var line in logStatusTooltipInfo.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
            DrawDetailLine(configuration, line);
        ImGui.PopTextWrapPos();
    }

    public static void DrawDetailLine(Configuration configuration, string line)
    {
        var separatorIndex = line.IndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == line.Length - 1)
        {
            ImGui.TextColored(WindowTheme.GetTooltipLabelTextColor(configuration), line);
            AddDetailRowSpacing();
            return;
        }

        DrawDetailRow(
            configuration,
            line[..separatorIndex].Trim(),
            line[(separatorIndex + 1)..].Trim());
    }

    public static void DrawDetailRow(Configuration configuration, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        DrawBoldTextColored(
            WindowTheme.GetTooltipDetailTextColor(configuration),
            $"{label}:");
        ImGui.SameLine(0f, ImGui.GetStyle().ItemInnerSpacing.X);
        ImGui.TextColored(WindowTheme.GetTooltipLabelTextColor(configuration), value);
        AddDetailRowSpacing();
    }

    private static void AddDetailRowSpacing() =>
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - (ImGui.GetStyle().ItemSpacing.Y * 0.5f));

    private static void DrawBoldTextColored(Vector4 color, string text)
    {
        var position = ImGui.GetCursorScreenPos();
        ImGui.TextColored(color, text);
        ImGui.GetWindowDrawList().AddText(
            position + new Vector2(0.55f, 0f),
            ImGui.GetColorU32(color),
            text);
    }
}
