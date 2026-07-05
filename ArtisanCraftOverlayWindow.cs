using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class ArtisanCraftOverlayWindow : Window
{
    private const float CompactOverlayWidth = 380f;

    private sealed record ProgressRow(
        string RecipeName,
        string QuantityText,
        string CraftText,
        string StatusText,
        Vector4 RowColor,
        Vector4 StatusColor);

    private readonly PluginIntegrationService pluginIntegrationService;
    private readonly Configuration configuration;
    private readonly Action openRecipeHelper;
    private readonly Action saveConfiguration;
    private bool overlayBackgroundPushed;
    private Vector2? pendingOpenPosition;
    private int pendingOpenPositionFrames;
    private float overlayWidth = 420f;
    private bool compactMode;
    private bool recipesExpanded = true;
    private Vector2 lastSavedWindowPosition = new(float.NaN, float.NaN);

    public ArtisanCraftOverlayWindow(
        PluginIntegrationService pluginIntegrationService,
        Configuration configuration,
        Action openRecipeHelper,
        Action saveConfiguration)
        : base("Recipe Helper Crafting Progress###DalamudRecipeHelperArtisanProgress")
    {
        this.pluginIntegrationService = pluginIntegrationService;
        this.configuration = configuration;
        this.openRecipeHelper = openRecipeHelper;
        this.saveConfiguration = saveConfiguration;
        this.Size = new Vector2(420, 320);
        this.SizeCondition = ImGuiCond.FirstUseEver;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(380, 220),
            MaximumSize = new Vector2(620, float.MaxValue),
        };
        if (this.configuration.HasSavedArtisanPopupPosition)
        {
            this.lastSavedWindowPosition = new Vector2(
                this.configuration.ArtisanPopupPositionX,
                this.configuration.ArtisanPopupPositionY);
        }
    }

    public override void PreDraw()
    {
        WindowTheme.Push(this.configuration);
        this.overlayWidth = this.compactMode
            ? CompactOverlayWidth
            : Math.Clamp(this.overlayWidth, 380, 620);
        this.overlayBackgroundPushed = this.configuration.UseTransparentOverlayBackground;
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

        var calculatedHeight = this.CalculateOverlayHeight();
        if (this.pendingOpenPosition is { } requestedPosition)
        {
            ImGui.SetNextWindowPos(requestedPosition, ImGuiCond.Always);
            this.pendingOpenPosition = null;
        }

        ImGui.SetNextWindowSize(
            new Vector2(this.overlayWidth, calculatedHeight),
            ImGuiCond.Always);
    }

    public override void PostDraw()
    {
        if (this.overlayBackgroundPushed)
            ImGui.PopStyleColor();
        WindowTheme.Pop();
    }

    public override void Draw()
    {
        WindowTheme.ApplyTextScale(this.configuration);
        if (this.pendingOpenPositionFrames > 0 && this.pendingOpenPosition is { } requestedPosition)
        {
            ImGui.SetWindowPos(requestedPosition, ImGuiCond.Always);
            this.pendingOpenPositionFrames--;
            if (this.pendingOpenPositionFrames == 0)
                this.pendingOpenPosition = null;
        }
        else
        {
            this.RememberWindowPosition();
        }

        var snapshot = this.pluginIntegrationService.GetCraftAllProgressSnapshot();
        this.overlayWidth = this.compactMode
            ? CompactOverlayWidth
            : Math.Clamp(ImGui.GetWindowSize().X, 380, 620);

        if (!snapshot.IsActive && snapshot.PendingEntries.Count == 0 && snapshot.CompletedEntries.Count == 0)
        {
            ImGui.TextDisabled("No Artisan queue is running.");
            return;
        }

        if (this.compactMode)
        {
            this.DrawCompactView(snapshot);
            return;
        }

        this.DrawSessionTimerCard(snapshot.Elapsed);
        this.DrawOverlayActions();

        if (snapshot.PendingEntries.Any(entry => entry.IsIntermediate) &&
            snapshot.PendingEntries.Any(entry => !entry.IsIntermediate))
        {
            ImGui.Spacing();
            ImGui.TextColored(
                this.configuration.WarningTextColor,
                "Waiting on pre-crafts before final recipes continue.");
        }

        var rows = this.BuildRows(snapshot);
        if (rows.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("No Artisan recipe progress to show.");
            return;
        }

        this.recipesExpanded = ImGui.CollapsingHeader(
            "RECIPES##artisan-progress-recipes",
            ImGuiTreeNodeFlags.DefaultOpen);
        if (!this.recipesExpanded)
            return;

        if (!ImGui.BeginTable(
                "artisan-progress-table",
                4,
                ImGuiTableFlags.PadOuterX |
                ImGuiTableFlags.Resizable |
                ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Recipe", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 74);
        ImGui.TableSetupColumn("Crafts", ImGuiTableColumnFlags.WidthFixed, 74);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 126);

        ImGui.TableNextRow(ImGuiTableRowFlags.None, 22);
        ImGui.TableNextColumn();
        this.DrawHeaderCard("Recipe");
        ImGui.TableNextColumn();
        this.DrawHeaderCard("Qty");
        ImGui.TableNextColumn();
        this.DrawHeaderCard("Crafts");
        ImGui.TableNextColumn();
        this.DrawHeaderCard("Status");

        foreach (var row in rows)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 32);

            ImGui.TableNextColumn();
            this.DrawInfoCard(
                $"artisan-row-recipe-{row.RecipeName}-{row.StatusText}",
                new Vector2(-1, 28),
                row.RecipeName,
                string.Empty,
                row.RowColor,
                this.configuration.TextColor);

            ImGui.TableNextColumn();
            this.DrawValueCard(
                $"artisan-row-qty-{row.RecipeName}-{row.StatusText}",
                new Vector2(-1, 28),
                row.QuantityText,
                AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                this.configuration.TextColor);

            ImGui.TableNextColumn();
            this.DrawValueCard(
                $"artisan-row-crafts-{row.RecipeName}-{row.StatusText}",
                new Vector2(-1, 28),
                row.CraftText,
                AdjustColor(this.configuration.WindowBackgroundColor, 0.07f),
                this.configuration.TextColor);

            ImGui.TableNextColumn();
            this.DrawValueCard(
                $"artisan-row-status-{row.RecipeName}-{row.StatusText}",
                new Vector2(-1, 28),
                row.StatusText,
                WithAlpha(row.StatusColor, 0.18f),
                this.GetReadableStatusTextColor(WithAlpha(row.StatusColor, 0.18f), row.StatusColor));
        }

        ImGui.EndTable();
    }

    public void OpenOverMainWindow(Vector2 mainWindowPosition)
    {
        this.compactMode = false;
        var fallbackPosition = new Vector2(
            mainWindowPosition.X + 8f,
            mainWindowPosition.Y + 8f);
        this.QueueOpenPosition(this.ResolveOpenPosition(fallbackPosition));
    }

    public void OpenBesideMainWindow(Vector2 mainWindowPosition, Vector2 mainWindowSize)
    {
        this.compactMode = false;
        var fallbackPosition = new Vector2(
            mainWindowPosition.X + Math.Max(0f, mainWindowSize.X) + 12f,
            mainWindowPosition.Y);
        this.QueueOpenPosition(this.ResolveOpenPosition(fallbackPosition));
    }

    public void OpenCompactBesideMainWindow(Vector2 mainWindowPosition, Vector2 mainWindowSize)
    {
        this.compactMode = true;
        var fallbackPosition = new Vector2(
            mainWindowPosition.X + Math.Max(0f, mainWindowSize.X) + 12f,
            mainWindowPosition.Y);
        this.QueueOpenPosition(this.ResolveOpenPosition(fallbackPosition));
    }

    public void Expand()
    {
        this.compactMode = false;
        this.IsOpen = true;
    }

    private void QueueOpenPosition(Vector2 targetPosition)
    {
        this.pendingOpenPosition = targetPosition;
        this.pendingOpenPositionFrames = 3;
        this.IsOpen = true;
    }

    private List<ProgressRow> BuildRows(ArtisanCraftProgressSnapshot snapshot)
    {
        var rows = new List<ProgressRow>();
        if (snapshot.CurrentEntry is { } currentEntry)
        {
            var completedCrafts = Math.Min(snapshot.CurrentEntryCompletedCrafts, currentEntry.CraftCount);
            var remainingCrafts = Math.Max(0u, currentEntry.CraftCount - completedCrafts);
            var remainingQuantity = (ulong)remainingCrafts * currentEntry.ResultAmount;
            var currentCraftIndex = currentEntry.CraftCount == 0
                ? 0u
                : Math.Min(currentEntry.CraftCount, completedCrafts + 1);
            var currentStatus = snapshot.IsPausedForAutoRetainer
                ? $"Paused ({completedCrafts:N0}/{currentEntry.CraftCount:N0})"
                : snapshot.CurrentEntryStarted
                    ? $"Crafting ({currentCraftIndex:N0}/{currentEntry.CraftCount:N0})"
                    : $"Starting (0/{currentEntry.CraftCount:N0})";
            rows.Add(new ProgressRow(
                this.GetDisplayName(currentEntry),
                remainingQuantity.ToString("N0"),
                remainingCrafts.ToString("N0"),
                currentStatus,
                AdjustColor(this.configuration.WindowBackgroundColor, 0.10f),
                snapshot.IsPausedForAutoRetainer
                    ? this.configuration.WarningTextColor
                    : this.configuration.AccentColor));
        }

        rows.AddRange(snapshot.PendingEntries
            .Skip(snapshot.CurrentEntry is null ? 0 : 1)
            .Select(entry => new ProgressRow(
                this.GetDisplayName(entry),
                FormatQuantity(entry),
                FormatCrafts(entry),
                $"Queued (0/{entry.CraftCount:N0})",
                AdjustColor(this.configuration.WindowBackgroundColor, 0.05f),
                this.configuration.AccentColor)));

        rows.AddRange(snapshot.CompletedEntries
            .Select(entry => new ProgressRow(
                this.GetDisplayName(entry),
                "0",
                "0",
                $"Done ({entry.CraftCount:N0}/{entry.CraftCount:N0})",
                this.configuration.EnoughRowColor,
                this.configuration.SuccessTextColor)));

        return rows;
    }

    private string GetDisplayName(ArtisanCraftQueueEntry entry)
    {
        if (entry.IsIntermediate)
            return $"Pre-craft: {entry.ResultName}";

        return entry.ResultName;
    }

    private float CalculateOverlayHeight()
    {
        if (this.compactMode)
            return 122f;

        var snapshot = this.pluginIntegrationService.GetCraftAllProgressSnapshot();
        var rowCount = this.BuildRows(snapshot).Count;
        var style = ImGui.GetStyle();
        var titleBarHeight = ImGui.GetFrameHeight() + 6f;
        var windowPadding = style.WindowPadding.Y * 2;
        var timerHeight = 44f + style.ItemSpacing.Y;
        var actionsHeight = ImGui.GetFrameHeight() + style.ItemSpacing.Y;
        var waitingHeight =
            snapshot.PendingEntries.Any(entry => entry.IsIntermediate) &&
            snapshot.PendingEntries.Any(entry => !entry.IsIntermediate)
                ? ImGui.GetTextLineHeight() + style.ItemSpacing.Y
                : 0f;
        var collapsedHeaderHeight = ImGui.GetFrameHeight() + style.ItemSpacing.Y;
        var fixedHeight =
            titleBarHeight +
            windowPadding +
            timerHeight +
            actionsHeight +
            waitingHeight +
            collapsedHeaderHeight +
            12f;
        if (!this.recipesExpanded || rowCount == 0)
            return Math.Max(140f, fixedHeight);

        var tableHeaderHeight = 24f;
        var tableRowHeight = 36f;
        var tableHeight =
            tableHeaderHeight +
            (tableRowHeight * rowCount) +
            4f;
        return Math.Max(180f, fixedHeight + tableHeight);
    }

    private void DrawCompactView(ArtisanCraftProgressSnapshot snapshot)
    {
        var summaryText = snapshot.IsPausedForAutoRetainer
            ? "Paused for AutoRetainer"
            : snapshot.CurrentEntry is { } currentEntry
                ? TrimTextToWidth(currentEntry.ResultName, Math.Max(100f, ImGui.GetContentRegionAvail().X - 12f))
                : "Crafting in progress";

        ImGui.TextColored(this.configuration.AccentTextColor, summaryText);
        ImGui.Spacing();
        this.DrawSessionTimerCard(snapshot.Elapsed);
        this.DrawActionButtons(
            "Details",
            () => this.compactMode = false,
            "Open Recipe Helper",
            this.openRecipeHelper);
    }

    private void DrawOverlayActions()
    {
        this.DrawActionButtons(
            "Compact",
            () => this.compactMode = true,
            "Open Recipe Helper",
            this.openRecipeHelper);
    }

    private void DrawActionButtons(
        string leftLabel,
        Action leftAction,
        string rightLabel,
        Action rightAction)
    {
        var scale = WindowTheme.GetMainInterfaceScale(this.configuration);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var availableWidth = Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var leftWidth = this.GetActionButtonWidth(leftLabel, scale);
        var rightWidth = this.GetActionButtonWidth(rightLabel, scale);
        var totalWidth = leftWidth + spacing + rightWidth;
        if (totalWidth > availableWidth)
        {
            var halfWidth = Math.Max(
                96f * scale,
                (availableWidth - spacing) / 2f);
            leftWidth = halfWidth;
            rightWidth = halfWidth;
            totalWidth = leftWidth + spacing + rightWidth;
        }

        WindowTheme.PushButtonStyle(this.configuration, scale);
        ImGui.SetCursorPosX(Math.Max(
            ImGui.GetCursorPosX(),
            ImGui.GetWindowContentRegionMax().X - totalWidth));
        if (WindowTheme.ShadowedButton(leftLabel, new Vector2(leftWidth, 0)))
            leftAction();
        ImGui.SameLine();
        if (WindowTheme.ShadowedButton(rightLabel, new Vector2(rightWidth, 0)))
            rightAction();
        WindowTheme.PopButtonStyle();
        ImGui.Spacing();
    }

    private Vector4 GetReadableStatusTextColor(Vector4 backgroundColor, Vector4 preferredColor)
    {
        var renderedBackground = BlendColors(
            this.configuration.WindowBackgroundColor,
            this.ApplyOverlayOpacity(backgroundColor));
        var candidates = new[]
        {
            preferredColor with { W = 1f },
            this.configuration.TextColor with { W = 1f },
            new Vector4(0.97f, 0.98f, 0.99f, 1f),
            new Vector4(0.08f, 0.08f, 0.10f, 1f),
        };

        return candidates
            .OrderByDescending(candidate => GetContrastRatio(renderedBackground, candidate))
            .First();
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

    private void DrawSessionTimerCard(TimeSpan elapsed)
    {
        var resolvedSize = ResolveCardSize(new Vector2(-1, 42f), 42f);
        var position = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("artisan-timer", resolvedSize);
        var drawList = ImGui.GetWindowDrawList();
        var cardColor = AdjustColor(this.configuration.WindowBackgroundColor, 0.10f);
        drawList.AddRectFilled(
            position,
            position + resolvedSize,
            ImGui.GetColorU32(this.ApplyOverlayOpacity(cardColor)),
            12f);

        var label = "Time spent crafting this session";
        var labelColor = ImGui.GetColorU32(AdjustColor(this.configuration.TextColor, -0.12f));
        var timerText = FormatDuration(elapsed);
        var timerSize = ImGui.CalcTextSize(timerText);
        var pillPadding = new Vector2(14f, 4f);
        var pillSize = new Vector2(timerSize.X + (pillPadding.X * 2f), Math.Max(24f, timerSize.Y + (pillPadding.Y * 2f)));
        var pillMin = new Vector2(
            position.X + resolvedSize.X - pillSize.X - 8f,
            position.Y + ((resolvedSize.Y - pillSize.Y) / 2f));
        var pillMax = pillMin + pillSize;
        var pillColor = WithAlpha(this.configuration.AccentColor, 0.22f);
        drawList.AddRectFilled(
            pillMin,
            pillMax,
            ImGui.GetColorU32(this.ApplyOverlayOpacity(pillColor)),
            pillSize.Y / 2f);

        var timerColor = ImGui.GetColorU32(this.configuration.TextColor);
        drawList.AddText(
            new Vector2(
                pillMin.X + ((pillSize.X - timerSize.X) / 2f),
                pillMin.Y + ((pillSize.Y - timerSize.Y) / 2f)),
            timerColor,
            timerText);

        var labelMaxWidth = Math.Max(60f, pillMin.X - position.X - 24f);
        var displayLabel = TrimTextToWidth(label, labelMaxWidth);
        var labelSize = ImGui.CalcTextSize(displayLabel);
        drawList.AddText(
            new Vector2(position.X + 12f, position.Y + ((resolvedSize.Y - labelSize.Y) / 2f)),
            labelColor,
            displayLabel);

        if (displayLabel.Length < label.Length && ImGui.IsItemHovered())
            ImGui.SetTooltip(label);
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
        drawList.AddRectFilled(
            position,
            position + resolvedSize,
            ImGui.GetColorU32(this.ApplyOverlayOpacity(backgroundColor)),
            12f);
        var titleMax = Math.Max(14, resolvedSize.X < 150f ? 22 : 40);
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
                string.Equals(id, "artisan-timer", StringComparison.Ordinal));
        }

        if ((title.Length > safeTitle.Length || subtitle.Length > safeSubtitle.Length) && ImGui.IsItemHovered())
            ImGui.SetTooltip(string.IsNullOrWhiteSpace(subtitle) ? title : $"{title}\n{subtitle}");
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
        drawList.AddRectFilled(
            position,
            position + resolvedSize,
            ImGui.GetColorU32(this.ApplyOverlayOpacity(backgroundColor)),
            12f);
        var displayValue = TrimDisplayText(value, 18);
        var valueSize = ImGui.CalcTextSize(displayValue);
        drawList.AddText(
            position + new Vector2((resolvedSize.X - valueSize.X) / 2f, (resolvedSize.Y - valueSize.Y) / 2f),
            ImGui.GetColorU32(textColor),
            displayValue);
        if (value.Length > displayValue.Length && ImGui.IsItemHovered())
            ImGui.SetTooltip(value);
    }

    private static Vector2 ResolveCardSize(Vector2 size, float defaultHeight)
    {
        var width = size.X <= 0f ? Math.Max(1f, ImGui.GetContentRegionAvail().X) : size.X;
        var height = size.Y <= 0f ? defaultHeight : size.Y;
        return new Vector2(width, height);
    }

    private float GetActionButtonWidth(string label, float scale) =>
        Math.Max(
            84f * scale,
            ImGui.CalcTextSize(label).X + (26f * scale));

    private Vector2 ResolveOpenPosition(Vector2 fallbackPosition) =>
        this.configuration.HasSavedArtisanPopupPosition
            ? new Vector2(
                this.configuration.ArtisanPopupPositionX,
                this.configuration.ArtisanPopupPositionY)
            : fallbackPosition;

    private void RememberWindowPosition()
    {
        var currentPosition = ImGui.GetWindowPos();
        if (Vector2.DistanceSquared(currentPosition, this.lastSavedWindowPosition) < 1f)
            return;

        this.lastSavedWindowPosition = currentPosition;
        this.configuration.HasSavedArtisanPopupPosition = true;
        this.configuration.ArtisanPopupPositionX = currentPosition.X;
        this.configuration.ArtisanPopupPositionY = currentPosition.Y;
        this.saveConfiguration();
    }

    private Vector4 ApplyOverlayOpacity(Vector4 color)
    {
        if (!this.configuration.UseTransparentOverlayBackground)
            return color;

        return WithAlpha(
            color,
            Math.Clamp(color.W * this.configuration.OverlayBackgroundOpacity, 0.08f, 1f));
    }

    private static string FormatDuration(TimeSpan elapsed) =>
        elapsed.TotalHours >= 1
            ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
            : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

    private static string FormatQuantity(ArtisanCraftQueueEntry entry) =>
        entry.TotalQuantity.ToString("N0");

    private static string FormatCrafts(ArtisanCraftQueueEntry entry) =>
        entry.CraftCount.ToString("N0");

    private static string TrimDisplayText(string text, int maxLength) =>
        string.IsNullOrWhiteSpace(text) || text.Length <= maxLength
            ? text
            : $"{text[..Math.Max(0, maxLength - 3)]}...";

    private static string TrimTextToWidth(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        var trimmed = text;
        while (trimmed.Length > 0 && ImGui.CalcTextSize(trimmed + ellipsis).X > maxWidth)
            trimmed = trimmed[..^1];
        return trimmed.Length == 0 ? ellipsis : trimmed + ellipsis;
    }

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

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);

    private static Vector4 AdjustColor(Vector4 color, float amount) =>
        new(
            Math.Clamp(color.X + amount, 0, 1),
            Math.Clamp(color.Y + amount, 0, 1),
            Math.Clamp(color.Z + amount, 0, 1),
            color.W);

    private static float GetLuminance(Vector4 color) =>
        (0.2126f * color.X) + (0.7152f * color.Y) + (0.0722f * color.Z);

    private static float GetContrastRatio(Vector4 background, Vector4 foreground)
    {
        var backgroundLuminance = GetLuminance(background) + 0.05f;
        var foregroundLuminance = GetLuminance(foreground) + 0.05f;
        return Math.Max(backgroundLuminance, foregroundLuminance) /
               Math.Min(backgroundLuminance, foregroundLuminance);
    }

    private static Vector4 BlendColors(Vector4 baseColor, Vector4 overlayColor)
    {
        var alpha = Math.Clamp(overlayColor.W, 0f, 1f);
        return new Vector4(
            (overlayColor.X * alpha) + (baseColor.X * (1f - alpha)),
            (overlayColor.Y * alpha) + (baseColor.Y * (1f - alpha)),
            (overlayColor.Z * alpha) + (baseColor.Z * (1f - alpha)),
            1f);
    }
}
