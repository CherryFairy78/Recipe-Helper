using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class RawMaterialsOverlayWindow : Window, IDisposable
{
    private readonly PluginIntegrationService pluginIntegrationService;
    private readonly AetherialReductionService aetherialReductionService;
    private readonly InventoryService inventoryService;
    private readonly RecipeService recipeService;
    private readonly Configuration configuration;
    private IReadOnlyList<IngredientNeed> materials = [];
    private string recipeName = string.Empty;
    private string message = string.Empty;
    private bool messageIsError;
    private bool inventoryRefreshRequested;

    public RawMaterialsOverlayWindow(
        PluginIntegrationService pluginIntegrationService,
        AetherialReductionService aetherialReductionService,
        InventoryService inventoryService,
        RecipeService recipeService,
        Configuration configuration)
        : base("Missing Items Overlay###DalamudRecipeHelperRawOverlay")
    {
        this.pluginIntegrationService = pluginIntegrationService;
        this.aetherialReductionService = aetherialReductionService;
        this.inventoryService = inventoryService;
        this.recipeService = recipeService;
        this.configuration = configuration;
        this.inventoryService.InventoryChanged += this.OnInventoryChanged;
        this.Size = new Vector2(430, 260);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(330, 140),
            MaximumSize = new Vector2(620, 650),
        };
    }

    public override void PreDraw() => WindowTheme.Push(this.configuration);

    public override void PostDraw() => WindowTheme.Pop();

    public void Dispose() =>
        this.inventoryService.InventoryChanged -= this.OnInventoryChanged;

    public void SetMaterials(string selectedRecipeName, IReadOnlyList<IngredientNeed> rawMaterials)
    {
        this.recipeName = selectedRecipeName;
        this.materials = rawMaterials;
    }

    public override void Draw()
    {
        if (this.inventoryRefreshRequested)
        {
            this.inventoryRefreshRequested = false;
            this.RefreshOwnedQuantities();
        }

        ImGui.TextColored(this.configuration.AccentColor, "MISSING ITEMS OVERLAY");
        if (!string.IsNullOrWhiteSpace(this.recipeName))
            ImGui.TextDisabled(this.recipeName);
        ImGui.Spacing();

        var gatherableMaterials = this.materials
            .Where(material => material.IsGatherable && material.Missing > 0)
            .OrderBy(material =>
                this.aetherialReductionService.GetAvailabilitySortKey(
                    material.ItemId,
                    material.ReductionSources))
            .ThenBy(material => material.Name)
            .ToList();
        if (gatherableMaterials.Count == 0)
        {
            ImGui.TextDisabled("No missing gatherable raw materials.");
            return;
        }

        var showMaterials = ImGui.CollapsingHeader(
            "MATERIALS##raw-overlay-materials",
            ImGuiTreeNodeFlags.DefaultOpen);
        if (showMaterials &&
            ImGui.BeginTable(
                "raw-travel-items",
                4,
                ImGuiTableFlags.RowBg |
                ImGuiTableFlags.BordersInnerH |
                ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 65);
            ImGui.TableSetupColumn("Available", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Travel", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableHeadersRow();

            foreach (var material in gatherableMaterials)
            {
                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(material.ReductionSources);

                ImGui.TableNextRow(ImGuiTableRowFlags.None, 26);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(material.Name);
                MaterialUsageTooltip.Draw(
                    this.recipeService,
                    this.configuration,
                    material);
                ImGui.TableNextColumn();
                ImGui.TextColored(this.configuration.MissingTextColor, material.Missing.ToString());
                ImGui.TableNextColumn();
                if (reductionSource is not null)
                    ImGui.TextUnformatted(this.aetherialReductionService.GetTimerText(reductionSource));
                else if (this.aetherialReductionService.GetGatheringTimerText(material.ItemId) is { } timer)
                    ImGui.TextUnformatted(timer);
                else
                    ImGui.TextDisabled("Any time");

                ImGui.TableNextColumn();
                var teleportButtonWidth = ImGui.CalcTextSize("Teleport").X + 14;
                var travelColumnWidth = ImGui.GetContentRegionAvail().X;
                ImGui.SetCursorPosX(
                    ImGui.GetCursorPosX() +
                    MathF.Max(0, (travelColumnWidth - teleportButtonWidth) / 2));
                if (ImGui.SmallButton($"Teleport##raw-overlay-{material.ItemId}"))
                {
                    this.messageIsError = !this.pluginIntegrationService.GatherWithGatherBuddy(
                        reductionSource?.Name ?? material.Name,
                        reductionSource?.IsFishing ?? material.IsFishing,
                        out this.message);
                }
            }

            ImGui.EndTable();
        }

        if (!string.IsNullOrWhiteSpace(this.message))
        {
            ImGui.Spacing();
            ImGui.TextColored(
                this.messageIsError
                    ? this.configuration.MissingTextColor
                    : this.configuration.SuccessTextColor,
                this.message);
        }
    }

    private void RefreshOwnedQuantities()
    {
        var ownedItems = this.inventoryService.GetOwnedItems();
        this.materials = this.materials.Select(material =>
        {
            ownedItems.TryGetValue(material.ItemId, out var owned);
            return material with
            {
                OwnedNq = owned?.NqQuantity ?? 0,
                OwnedHq = owned?.HqQuantity ?? 0,
                NqLocations = owned?.NqLocations ?? [],
                HqLocations = owned?.HqLocations ?? [],
            };
        }).ToList();
    }

    private void OnInventoryChanged() => this.inventoryRefreshRequested = true;
}
