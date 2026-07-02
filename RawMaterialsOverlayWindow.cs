using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class RawMaterialsOverlayWindow : Window, IDisposable
{
    private sealed record OverlayMaterialEntry(
        IngredientNeed Material,
        AetherialReductionSource? ReductionSource,
        string? AvailabilityText,
        bool CanGather,
        int SortCategory,
        int WaitSeconds);

    private readonly PluginIntegrationService pluginIntegrationService;
    private readonly AetherialReductionService aetherialReductionService;
    private readonly MarketboardPriceService marketboardPriceService;
    private readonly InventoryService inventoryService;
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private IReadOnlyList<IngredientNeed> materials = [];
    private IReadOnlyList<string> selectedRecipeNames = [];
    private string message = string.Empty;
    private bool messageIsError;
    private bool inventoryRefreshRequested;
    private bool materialsExpanded = true;
    private bool overlayBackgroundPushed;
    private float overlayWidth = 392;

    public RawMaterialsOverlayWindow(
        PluginIntegrationService pluginIntegrationService,
        AetherialReductionService aetherialReductionService,
        MarketboardPriceService marketboardPriceService,
        InventoryService inventoryService,
        Configuration configuration,
        Action saveConfiguration)
        : base("Missing Items Overlay###DalamudRecipeHelperRawOverlay")
    {
        this.pluginIntegrationService = pluginIntegrationService;
        this.aetherialReductionService = aetherialReductionService;
        this.marketboardPriceService = marketboardPriceService;
        this.inventoryService = inventoryService;
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.inventoryService.InventoryChanged += this.OnInventoryChanged;
        this.Size = new Vector2(372, 210);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(300, 72),
            MaximumSize = new Vector2(540, float.MaxValue),
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

        this.overlayWidth = Math.Clamp(ImGui.GetWindowSize().X, 300, 540);
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

        var hideVendoredItems = !this.configuration.ShowVendoredItemsInOverlay;
        if (ImGui.Checkbox("Hide vendored items", ref hideVendoredItems))
        {
            this.configuration.ShowVendoredItemsInOverlay = !hideVendoredItems;
            this.saveConfiguration();
        }

        var overlayMaterials = this.GetOverlayMaterials();
        if (overlayMaterials.Count == 0)
        {
            ImGui.TextDisabled("No missing overlay materials to show.");
            return;
        }

        this.materialsExpanded = ImGui.CollapsingHeader(
            "MATERIALS##raw-overlay-materials",
            ImGuiTreeNodeFlags.DefaultOpen);
        if (this.materialsExpanded &&
            ImGui.BeginTable(
                "raw-travel-items",
                4,
                ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Material", ImGuiTableColumnFlags.WidthFixed, 148);
            ImGui.TableSetupColumn("Missing", ImGuiTableColumnFlags.WidthFixed, 56);
            ImGui.TableSetupColumn("Available", ImGuiTableColumnFlags.WidthFixed, 88);
            ImGui.TableSetupColumn("Travel", ImGuiTableColumnFlags.WidthStretch, 1);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 22);
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Material");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Missing");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Available");
            ImGui.TableNextColumn();
            this.DrawHeaderCard("Action");

            foreach (var material in overlayMaterials)
            {
                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(material.ReductionSources);
                var availabilityText = this.GetOverlayAvailabilityText(material, reductionSource);
                var canGather = this.CanGatherMaterial(material, reductionSource, availabilityText);

                ImGui.TableNextRow(ImGuiTableRowFlags.None, 32);
                var rowColor = material.HasEnough ||
                               this.aetherialReductionService.IsCurrentlyAvailable(
                                   material.ItemId,
                                   material.ReductionSources)
                    ? this.configuration.EnoughRowColor
                    : AdjustColor(this.configuration.WindowBackgroundColor, 0.08f);

                ImGui.TableNextColumn();
                this.DrawInfoCard(
                    $"overlay-material-{material.ItemId}",
                    new Vector2(-1, 28),
                    material.Name,
                    string.Empty,
                    rowColor,
                    this.configuration.TextColor);
                MaterialUsageTooltip.Draw(
                    this.marketboardPriceService,
                    this.configuration,
                    material);

                ImGui.TableNextColumn();
                this.DrawValueCard(
                    $"overlay-missing-{material.ItemId}",
                    new Vector2(-1, 28),
                    material.Missing.ToString(),
                    WithAlpha(this.configuration.MissingTextColor, 0.18f),
                    this.configuration.MissingTextColor);

                ImGui.TableNextColumn();
                if (HasTimedAvailability(availabilityText))
                {
                    this.DrawAvailabilityCard(
                        $"overlay-available-{material.ItemId}",
                        availabilityText!);
                }
                else if (canGather)
                {
                    this.DrawValueCard(
                        $"overlay-available-{material.ItemId}",
                        new Vector2(-1, 28),
                        "Always Up",
                        AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                        AdjustColor(this.configuration.TextColor, -0.22f));
                }
                else if (IsVendorMaterial(material))
                {
                    this.DrawValueCard(
                        $"overlay-available-{material.ItemId}",
                        new Vector2(-1, 28),
                        "Vendor",
                        AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                        AdjustColor(this.configuration.TextColor, -0.22f));
                }
                else
                {
                    this.DrawValueCard(
                        $"overlay-available-{material.ItemId}",
                        new Vector2(-1, 28),
                        "Any time",
                        AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                        AdjustColor(this.configuration.TextColor, -0.22f));
                }

                ImGui.TableNextColumn();
                if (canGather)
                {
                    var gatherButtonWidth = 58f;
                    var actionWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
                    this.DrawDecorativeCardBackground(
                        new Vector2(actionWidth, 28),
                        WithAlpha(this.configuration.AccentColor, 0.12f));
                    var buttonCursor = ImGui.GetCursorPos();
                    ImGui.SetCursorPos(new Vector2(
                        buttonCursor.X + MathF.Max(0f, (actionWidth - gatherButtonWidth) / 2f),
                        buttonCursor.Y - 27f));
                    var accent = this.configuration.ButtonColor;
                    ImGui.PushStyleColor(ImGuiCol.Button, WithAlpha(accent, 0.72f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AdjustColor(accent, 0.10f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, AdjustColor(accent, -0.08f));
                    var gatherClicked =
                        ImGui.Button($"Gather##raw-overlay-{material.ItemId}", new Vector2(gatherButtonWidth, 0));
                    ImGui.PopStyleColor(3);
                    if (gatherClicked)
                    {
                        var gathered = this.pluginIntegrationService.GatherWithGatherBuddy(
                            reductionSource?.Name ?? material.Name,
                            reductionSource?.IsFishing ?? material.IsFishing,
                            out var gatherMessage);
                        this.messageIsError = !gathered;
                        this.message = gathered ? string.Empty : gatherMessage;
                    }

                    DrawTooltipIfHovered("Teleport to closest Aetheryte");
                }
                else
                {
                    this.DrawValueCard(
                        $"overlay-action-{material.ItemId}",
                        new Vector2(-1, 28),
                        "Vendor",
                        AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                        AdjustColor(this.configuration.TextColor, -0.22f));
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

    private IReadOnlyList<IngredientNeed> GetOverlayMaterials() =>
        this.materials
            .Select(material =>
            {
                var reductionSource =
                    this.aetherialReductionService.GetPreferredSource(material.ReductionSources);
                var availabilityText = this.GetOverlayAvailabilityText(material, reductionSource);
                var canGather = this.CanGatherMaterial(material, reductionSource, availabilityText);
                return new OverlayMaterialEntry(
                    material,
                    reductionSource,
                    availabilityText,
                    canGather,
                    GetOverlaySortCategory(canGather, availabilityText, IsVendorMaterial(material)),
                    GetOverlayWaitSeconds(availabilityText));
            })
            .Where(entry =>
                (entry.CanGather ||
                 (this.configuration.ShowVendoredItemsInOverlay && IsVendorMaterial(entry.Material))) &&
                entry.Material.Missing > 0)
            .OrderBy(entry => entry.SortCategory)
            .ThenBy(entry => entry.WaitSeconds)
            .ThenBy(entry => entry.Material.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(entry => entry.Material)
            .ToList();

    private bool CanGatherMaterial(
        IngredientNeed material,
        AetherialReductionSource? reductionSource,
        string? availabilityText) =>
        material.IsGatherable ||
        HasGatherableSourceLabel(material) ||
        reductionSource is not null ||
        HasTimedAvailability(availabilityText);

    private string? GetOverlayAvailabilityText(
        IngredientNeed material,
        AetherialReductionSource? reductionSource) =>
        reductionSource is not null
            ? this.aetherialReductionService.GetTimerText(reductionSource)
            : this.aetherialReductionService.GetGatheringTimerText(material.ItemId);

    private static bool HasTimedAvailability(string? availabilityText) =>
        !string.IsNullOrWhiteSpace(availabilityText) &&
        (availabilityText.StartsWith("Now", StringComparison.OrdinalIgnoreCase) ||
         availabilityText.StartsWith("In ", StringComparison.OrdinalIgnoreCase));

    private static int GetOverlaySortCategory(bool canGather, string? availabilityText, bool isVendor)
    {
        if (canGather && HasTimedAvailability(availabilityText))
            return 0;
        if (canGather)
            return 1;
        if (isVendor)
            return 3;
        return 2;
    }

    private static int GetOverlayWaitSeconds(string? availabilityText)
    {
        if (string.IsNullOrWhiteSpace(availabilityText))
            return int.MaxValue;

        if (availabilityText.StartsWith("Now", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (!availabilityText.StartsWith("In ", StringComparison.OrdinalIgnoreCase))
            return int.MaxValue;

        var durationText = availabilityText[3..].Trim();
        var parts = durationText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hours = parts.Where(part => part.EndsWith('h')).Select(part => ParseTimePart(part, 'h')).DefaultIfEmpty(0).First();
        var minutes = parts.Where(part => part.EndsWith('m')).Select(part => ParseTimePart(part, 'm')).DefaultIfEmpty(0).First();
        var seconds = parts.Where(part => part.EndsWith('s')).Select(part => ParseTimePart(part, 's')).DefaultIfEmpty(0).First();
        return (hours * 3600) + (minutes * 60) + seconds;
    }

    private static bool IsVendorMaterial(IngredientNeed material) =>
        material.Source.Contains("Vendor", StringComparison.OrdinalIgnoreCase);

    private static bool HasGatherableSourceLabel(IngredientNeed material) =>
        material.Source.Contains("Gatherable", StringComparison.OrdinalIgnoreCase) ||
        material.Source.Contains("Aetherial reduction", StringComparison.OrdinalIgnoreCase);

    private float CalculateOverlayHeight()
    {
        var materialCount = this.GetOverlayMaterials().Count;
        var style = ImGui.GetStyle();
        var titleBarHeight = ImGui.GetFrameHeight() + 6f;
        var windowPadding = style.WindowPadding.Y * 2;
        var selectedRecipeHeight = this.selectedRecipeNames.Count > 0
            ? ImGui.GetTextLineHeight() + style.ItemSpacing.Y
            : 0f;
        var collapsedHeaderHeight = ImGui.GetFrameHeight() + style.ItemSpacing.Y;
        var fixedHeight =
            titleBarHeight +
            windowPadding +
            selectedRecipeHeight +
            collapsedHeaderHeight +
            12f;
        if (materialCount == 0)
        {
            return
                titleBarHeight +
                windowPadding +
                selectedRecipeHeight +
                ImGui.GetTextLineHeight() +
                12f;
        }

        if (!this.materialsExpanded)
            return fixedHeight;

        var tableHeaderHeight = 24f;
        var tableRowHeight = 36f;
        var tableHeight =
            tableHeaderHeight +
            (tableRowHeight * materialCount) +
            4f;
        var messageHeight =
            this.messageIsError && !string.IsNullOrWhiteSpace(this.message)
                ? ImGui.GetTextLineHeight() + style.ItemSpacing.Y + 8f
                : 0f;
        return Math.Max(
            96f,
            fixedHeight + tableHeight + messageHeight);
    }

    private void DrawHeaderCard(string label)
    {
        var size = new Vector2(Math.Max(1f, ImGui.GetContentRegionAvail().X), 22f);
        var position = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilledMultiColor(
            position,
            position + size,
            ImGui.GetColorU32(this.ApplyOverlayOpacity(WithAlpha(AdjustColor(this.configuration.AccentColor, 0.15f), 0.92f))),
            ImGui.GetColorU32(this.ApplyOverlayOpacity(WithAlpha(AdjustColor(this.configuration.AccentColor, 0.04f), 0.92f))),
            ImGui.GetColorU32(this.ApplyOverlayOpacity(WithAlpha(AdjustColor(this.configuration.AccentColor, -0.02f), 0.78f))),
            ImGui.GetColorU32(this.ApplyOverlayOpacity(WithAlpha(AdjustColor(this.configuration.AccentColor, -0.10f), 0.78f))));
        var textSize = ImGui.CalcTextSize(label);
        drawList.AddText(
            position + new Vector2((size.X - textSize.X) / 2f, 4f),
            ImGui.GetColorU32(this.configuration.TextColor),
            label);
        ImGui.Dummy(size);
    }

    private void DrawInfoCard(
        string id,
        Vector2 size,
        string title,
        string subtitle,
        Vector4 backgroundColor,
        Vector4 textColor)
    {
        var resolvedSize = ResolveCardSize(size, 32f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, resolvedSize);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + resolvedSize, ImGui.GetColorU32(this.ApplyOverlayOpacity(backgroundColor)), 12f);
        var titleMax = Math.Max(14, resolvedSize.X < 150f ? 22 : 28);
        var safeTitle = TrimDisplayText(title, titleMax);
        var safeSubtitle = TrimDisplayText(subtitle, Math.Max(16, titleMax + 2));
        if (string.IsNullOrWhiteSpace(safeSubtitle))
        {
            var titleSize = ImGui.CalcTextSize(safeTitle);
            drawList.AddText(
                position + new Vector2(10f, (resolvedSize.Y - titleSize.Y) / 2f),
                ImGui.GetColorU32(textColor),
                safeTitle);
        }
        else
        {
            DrawStackedTextBlock(
                drawList,
                position + new Vector2(10f, 0f),
                new Vector2(Math.Max(1f, resolvedSize.X - 20f), resolvedSize.Y),
                safeTitle,
                safeSubtitle,
                ImGui.GetColorU32(textColor),
                ImGui.GetColorU32(AdjustColor(textColor, -0.24f)),
                2f,
                0f,
                false);
        }
        if ((title.Length > safeTitle.Length || subtitle.Length > safeSubtitle.Length) && ImGui.IsItemHovered())
            ImGui.SetTooltip($"{title}\n{subtitle}");
    }

    private void DrawValueCard(
        string id,
        Vector2 size,
        string value,
        Vector4 backgroundColor,
        Vector4 textColor)
    {
        var resolvedSize = ResolveCardSize(size, 32f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, resolvedSize);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(position, position + resolvedSize, ImGui.GetColorU32(this.ApplyOverlayOpacity(backgroundColor)), 12f);
        var displayValue = TrimDisplayText(value, 14);
        var valueSize = ImGui.CalcTextSize(displayValue);
        drawList.AddText(
            position + new Vector2((resolvedSize.X - valueSize.X) / 2f, (resolvedSize.Y - valueSize.Y) / 2f),
            ImGui.GetColorU32(textColor),
            displayValue);
        if (value.Length > displayValue.Length && ImGui.IsItemHovered())
            ImGui.SetTooltip(value);
    }

    private void DrawAvailabilityCard(string id, string timerText)
    {
        var normalized = timerText.Replace("Ã‚Â·", "-").Replace("Â·", "-").Trim();
        var isNow = normalized.StartsWith("Now", StringComparison.OrdinalIgnoreCase);
        var displayText = FormatOverlayAvailability(normalized, isNow);
        var waitSeconds = GetOverlayWaitSeconds(normalized);
        var isImminent = !isNow && waitSeconds is >= 0 and <= 60;
        var resolvedSize = ResolveCardSize(new Vector2(-1, 28), 28f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton(id, resolvedSize);
        var drawList = ImGui.GetWindowDrawList();
        var backgroundColor = isNow
            ? WithAlpha(this.configuration.SuccessTextColor, 0.20f)
            : isImminent
                ? WithAlpha(this.configuration.WarningTextColor, 0.22f)
                : WithAlpha(this.configuration.AccentColor, 0.16f);
        var textColor = isNow
            ? this.configuration.SuccessTextColor
            : isImminent
                ? this.configuration.WarningTextColor
                : this.configuration.TextColor;
        drawList.AddRectFilled(position, position + resolvedSize, ImGui.GetColorU32(this.ApplyOverlayOpacity(backgroundColor)), 12f);
        var displaySize = ImGui.CalcTextSize(displayText);
        drawList.AddText(
            position + new Vector2((resolvedSize.X - displaySize.X) / 2f, (resolvedSize.Y - displaySize.Y) / 2f),
            ImGui.GetColorU32(textColor),
            displayText);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(normalized);
    }

    private void DrawDecorativeCardBackground(Vector2 size, Vector4 backgroundColor)
    {
        var resolvedSize = ResolveCardSize(size, 32f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.Dummy(resolvedSize);
        ImGui.GetWindowDrawList().AddRectFilled(
            position,
            position + resolvedSize,
            ImGui.GetColorU32(this.ApplyOverlayOpacity(backgroundColor)),
            12f);
    }

    private static Vector2 ResolveCardSize(Vector2 size, float defaultHeight)
    {
        var width = size.X <= 0f ? Math.Max(1f, ImGui.GetContentRegionAvail().X) : size.X;
        var height = size.Y <= 0f ? defaultHeight : size.Y;
        return new Vector2(width, height);
    }

    private Vector4 ApplyOverlayOpacity(Vector4 color)
    {
        if (!this.configuration.UseTransparentOverlayBackground)
            return color;

        return WithAlpha(
            color,
            Math.Clamp(color.W * this.configuration.OverlayBackgroundOpacity, 0.08f, 1f));
    }

    private static string TrimDisplayText(string text, int maxLength) =>
        string.IsNullOrWhiteSpace(text) || text.Length <= maxLength
            ? text
            : $"{text[..Math.Max(0, maxLength - 3)]}...";

    private static void DrawStackedTextBlock(
        ImDrawListPtr drawList,
        Vector2 position,
        Vector2 size,
        string title,
        string subtitle,
        uint titleColor,
        uint subtitleColor,
        float gap,
        float minVerticalPadding,
        bool centeredHorizontally)
    {
        var titleSize = ImGui.CalcTextSize(title);
        var subtitleSize = ImGui.CalcTextSize(subtitle);
        var totalHeight = titleSize.Y + gap + subtitleSize.Y;
        var textTop = position.Y + MathF.Max(minVerticalPadding, (size.Y - totalHeight) / 2f);
        var titleX = centeredHorizontally
            ? position.X + MathF.Max(0f, (size.X - titleSize.X) / 2f)
            : position.X;
        var subtitleX = centeredHorizontally
            ? position.X + MathF.Max(0f, (size.X - subtitleSize.X) / 2f)
            : position.X;
        drawList.AddText(new Vector2(titleX, textTop), titleColor, title);
        drawList.AddText(new Vector2(subtitleX, textTop + titleSize.Y + gap), subtitleColor, subtitle);
    }

    private static string FormatOverlayAvailability(string normalized, bool isNow)
    {
        if (isNow)
            return "NOW";

        var durationText = normalized.StartsWith("In ", StringComparison.OrdinalIgnoreCase)
            ? normalized[3..].Trim()
            : normalized;
        var parts = durationText.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var hours = parts.Where(part => part.EndsWith('h')).Select(part => ParseTimePart(part, 'h')).DefaultIfEmpty(0).First();
        var minutes = parts.Where(part => part.EndsWith('m')).Select(part => ParseTimePart(part, 'm')).DefaultIfEmpty(0).First();
        var seconds = parts.Where(part => part.EndsWith('s')).Select(part => ParseTimePart(part, 's')).DefaultIfEmpty(0).First();
        if (hours == 0 && minutes == 0 && seconds == 0)
            return TrimDisplayText(normalized, 12);

        return hours > 0
            ? $"{hours:D2}:{minutes:D2}:{seconds:D2}"
            : $"{minutes:D2}:{seconds:D2}";
    }

    private static int ParseTimePart(string text, char suffix)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;

        var cleaned = text.Replace(suffix.ToString(), string.Empty, StringComparison.OrdinalIgnoreCase).Trim();
        return int.TryParse(cleaned, out var value) ? value : 0;
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);

    private static Vector4 AdjustColor(Vector4 color, float amount) =>
        new(
            Math.Clamp(color.X + amount, 0, 1),
            Math.Clamp(color.Y + amount, 0, 1),
            Math.Clamp(color.Z + amount, 0, 1),
            color.W);

    private static void DrawTooltipIfHovered(string text)
    {
        if (!ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }
}
