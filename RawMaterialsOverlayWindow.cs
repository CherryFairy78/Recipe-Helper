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
    private IReadOnlyList<string> selectedRecipeNames = [];
    private string message = string.Empty;
    private bool messageIsError;
    private bool inventoryRefreshRequested;
    private bool materialsExpanded = true;
    private bool overlayBackgroundPushed;
    private float overlayWidth = 430;

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
            MinimumSize = new Vector2(330, 72),
            MaximumSize = new Vector2(620, 650),
        };
    }

    public override void PreDraw()
    {
        WindowTheme.Push(this.configuration);
        this.overlayBackgroundPushed =
            this.configuration.UseTransparentOverlayBackground;
        if (this.overlayBackgroundPushed)
        {
            var background = this.configuration.WindowBackgroundColor;
            ImGui.PushStyleColor(
                ImGuiCol.WindowBg,
                new Vector4(
                    background.X,
                    background.Y,
                    background.Z,
                    Math.Clamp(this.configuration.OverlayBackgroundOpacity, 0.20f, 1f)));
        }

        ImGui.SetNextWindowSize(
            new Vector2(this.overlayWidth, this.CalculateOverlayHeight()),
            ImGuiCond.Always);
    }

    public override void PostDraw()
    {
        if (this.overlayBackgroundPushed)
            ImGui.PopStyleColor();
        WindowTheme.Pop();
    }

    public void Dispose() =>
        this.inventoryService.InventoryChanged -= this.OnInventoryChanged;

    public void SetMaterials(
        IReadOnlyList<string> recipeNames,
        IReadOnlyList<IngredientNeed> rawMaterials)
    {
        this.selectedRecipeNames = recipeNames;
        this.materials = rawMaterials;
    }

    public override void Draw()
    {
        if (this.inventoryRefreshRequested)
        {
            this.inventoryRefreshRequested = false;
            this.RefreshOwnedQuantities();
        }

        this.overlayWidth = Math.Clamp(ImGui.GetWindowSize().X, 330, 620);
        if (this.selectedRecipeNames.Count > 0)
        {
            ImGui.TextDisabled(
                $"{this.selectedRecipeNames.Count} selected " +
                (this.selectedRecipeNames.Count == 1 ? "recipe" : "recipes"));
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextColored(this.configuration.AccentColor, "Selected recipes");
                foreach (var recipeName in this.selectedRecipeNames)
                    ImGui.BulletText(recipeName);
                ImGui.EndTooltip();
            }
        }

        var gatherableMaterials = this.GetGatherableMaterials();
        if (gatherableMaterials.Count == 0)
        {
            ImGui.TextDisabled("No missing gatherable raw materials.");
            return;
        }

        this.materialsExpanded = ImGui.CollapsingHeader(
            "MATERIALS##raw-overlay-materials",
            ImGuiTreeNodeFlags.DefaultOpen);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(6, 2));
        if (this.materialsExpanded &&
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

                ImGui.TableNextRow();
                if (this.aetherialReductionService.IsCurrentlyAvailable(
                        material.ItemId,
                        material.ReductionSources))
                {
                    ImGui.TableSetBgColor(
                        ImGuiTableBgTarget.RowBg0,
                        ImGui.GetColorU32(this.configuration.EnoughRowColor));
                }

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
                var accent = this.configuration.AccentColor;
                ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(accent, 0.72f));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AdjustColor(accent, 0.10f));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, AdjustColor(accent, -0.08f));
                var teleportClicked =
                    ImGui.SmallButton($"Teleport##raw-overlay-{material.ItemId}");
                ImGui.PopStyleColor(3);
                if (teleportClicked)
                {
                    var gathered = this.pluginIntegrationService.GatherWithGatherBuddy(
                        reductionSource?.Name ?? material.Name,
                        reductionSource?.IsFishing ?? material.IsFishing,
                        out var gatherMessage);
                    this.messageIsError = !gathered;
                    this.message = gathered ? string.Empty : gatherMessage;
                }
            }

            ImGui.EndTable();
        }
        ImGui.PopStyleVar();

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

    private IReadOnlyList<IngredientNeed> GetGatherableMaterials() =>
        this.materials
            .Where(material => material.IsGatherable && material.Missing > 0)
            .OrderBy(material =>
                this.aetherialReductionService.GetAvailabilitySortKey(
                    material.ItemId,
                    material.ReductionSources))
            .ThenBy(material => material.Name)
            .ToList();

    private float CalculateOverlayHeight()
    {
        var materialCount = this.GetGatherableMaterials().Count;
        var selectedRecipeHeight = this.selectedRecipeNames.Count > 0 ? 23f : 0f;
        if (materialCount == 0)
            return 72f + selectedRecipeHeight;

        if (!this.materialsExpanded)
            return 70f + selectedRecipeHeight;

        var tableHeight = 23f * (materialCount + 1);
        var messageHeight =
            this.messageIsError && !string.IsNullOrWhiteSpace(this.message)
                ? 28f
                : 0f;
        return Math.Clamp(
            76f + selectedRecipeHeight + tableHeight + messageHeight,
            96f,
            650f);
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);

    private static Vector4 AdjustColor(Vector4 color, float amount) =>
        new(
            Math.Clamp(color.X + amount, 0, 1),
            Math.Clamp(color.Y + amount, 0, 1),
            Math.Clamp(color.Z + amount, 0, 1),
            color.W);
}
