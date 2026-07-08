using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DalamudRecipeHelper;

public sealed class DebugWindow : Window
{
    private readonly Configuration configuration;
    private readonly FileLogService fileLog;
    private readonly InventoryService inventoryService;
    private readonly GwenDreamService gwenDreamService;
    private readonly string pluginConfigDirectory;
    private string debugReport = string.Empty;
    private DateTimeOffset generatedAtUtc;
    private DateTime copyMessageExpiresAtUtc;
    private DateTime clearMessageExpiresAtUtc;
    private Dictionary<string, string> retainerAliases = new(StringComparer.OrdinalIgnoreCase);

    public DebugWindow(
        Configuration configuration,
        FileLogService fileLog,
        InventoryService inventoryService,
        GwenDreamService gwenDreamService,
        string pluginConfigDirectory)
        : base("Recipe Helper Debug###DalamudRecipeHelperDebugWindow")
    {
        this.configuration = configuration;
        this.fileLog = fileLog;
        this.inventoryService = inventoryService;
        this.gwenDreamService = gwenDreamService;
        this.pluginConfigDirectory = pluginConfigDirectory;
        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(520, 380),
            MaximumSize = new Vector2(1100, 960),
        };
    }

    public override void PreDraw() => WindowTheme.Push(this.configuration);

    public override void PostDraw() => WindowTheme.Pop();

    public void OpenWithFreshReport()
    {
        this.RefreshReport();
        this.IsOpen = true;
    }

    public void ClearOnClose()
    {
        this.fileLog.ClearLogs();
        this.debugReport = string.Empty;
        this.generatedAtUtc = default;
        this.copyMessageExpiresAtUtc = default;
        this.clearMessageExpiresAtUtc = default;
    }

    public override void Draw()
    {
        WindowTheme.ApplyTextScale(this.configuration);
        if (string.IsNullOrWhiteSpace(this.debugReport))
            this.RefreshReport();

        ImGui.TextColored(this.configuration.AccentTextColor, "Support Debug Report");
        ImGui.TextDisabled("Refresh it, copy it, and send the full report back for troubleshooting.");
        ImGui.Spacing();

        var actionScale = WindowTheme.GetTextScale(this.configuration);
        var buttonPadding = 26f * actionScale;
        var refreshButtonWidth = Math.Max(118f * actionScale, ImGui.CalcTextSize("Refresh report").X + buttonPadding);
        var copyButtonWidth = Math.Max(104f * actionScale, ImGui.CalcTextSize("Copy report").X + buttonPadding);
        var clearButtonWidth = Math.Max(96f * actionScale, ImGui.CalcTextSize("Clear logs").X + buttonPadding);
        WindowTheme.PushButtonStyle(this.configuration, actionScale);
        if (WindowTheme.ShadowedButton("Refresh report", new Vector2(refreshButtonWidth, 0)))
            this.RefreshReport();

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Copy report", new Vector2(copyButtonWidth, 0)))
        {
            if (string.IsNullOrWhiteSpace(this.debugReport))
                this.RefreshReport();

            ImGui.SetClipboardText(this.debugReport);
            this.copyMessageExpiresAtUtc = DateTime.UtcNow.AddSeconds(3);
        }

        ImGui.SameLine();
        if (WindowTheme.ShadowedButton("Clear logs", new Vector2(clearButtonWidth, 0)))
        {
            this.fileLog.ClearLogs();
            this.RefreshReport();
            this.clearMessageExpiresAtUtc = DateTime.UtcNow.AddSeconds(3);
        }
        WindowTheme.PopButtonStyle();

        if (this.generatedAtUtc != default)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"Generated {this.generatedAtUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}");
        }

        if (DateTime.UtcNow <= this.copyMessageExpiresAtUtc)
        {
            ImGui.SameLine();
            ImGui.TextColored(this.configuration.SuccessTextColor, "Copied");
        }

        if (DateTime.UtcNow <= this.clearMessageExpiresAtUtc)
        {
            ImGui.SameLine();
            ImGui.TextColored(this.configuration.SuccessTextColor, "Logs cleared");
        }

        ImGui.Spacing();
        ImGui.BeginChild("debug-report", new Vector2(0, 0), true, ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.TextUnformatted(this.debugReport);
        ImGui.EndChild();
    }

    private void RefreshReport()
    {
        _ = this.inventoryService.GetOwnedItems();
        var storedRetainers = this.inventoryService.GetStoredRetainers();
        var dreamSnapshot = this.gwenDreamService.GetDebugSnapshot();
        var latestLogPath = this.fileLog.GetLatestLogPath();
        var recentLogLines = this.fileLog.GetRecentLines(80);
        this.retainerAliases = BuildRetainerAliases(storedRetainers);

        this.generatedAtUtc = DateTimeOffset.UtcNow;
        var builder = new StringBuilder();
        builder.AppendLine("=== Recipe Helper Debug Report ===");
        builder.AppendLine($"GeneratedUtc: {this.generatedAtUtc:O}");
        builder.AppendLine($"PluginVersion: {typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown"}");
        builder.AppendLine($"PluginMode: {(Plugin.PluginInterface.IsDev ? "Development" : "Published")}");
        builder.AppendLine($"ConfigDirectory: {SanitizePath(this.pluginConfigDirectory, this.pluginConfigDirectory)}");
        builder.AppendLine($"LogDirectory: {SanitizePath(this.fileLog.LogDirectory, this.pluginConfigDirectory)}");
        builder.AppendLine($"LatestLogFile: {ValueOrNone(SanitizeText(latestLogPath))}");
        builder.AppendLine($"RetainerSnapshotFile: {DescribeFile(this.inventoryService.RetainerSnapshotPath, this.pluginConfigDirectory)}");
        builder.AppendLine();
        builder.AppendLine("[Inventory]");
        builder.AppendLine($"LastScannedContainers: {this.inventoryService.LastScannedContainers}");
        builder.AppendLine($"LastScannedSlots: {this.inventoryService.LastScannedSlots}");
        builder.AppendLine($"LastTrackedStacks: {this.inventoryService.LastItemStacks}");
        builder.AppendLine($"StoredRetainers: {storedRetainers.Count}");
        foreach (var retainer in storedRetainers)
        {
            builder.AppendLine(
                $"- {this.GetRetainerAlias(retainer.Name)} | captured {retainer.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} | unique items {retainer.Items.Count}");
        }

        if (storedRetainers.Count == 0)
            builder.AppendLine("- none");

        builder.AppendLine();
        builder.AppendLine("[Dream]");
        builder.AppendLine($"AutoRetainerAvailable: {dreamSnapshot.AutoRetainerAvailable}");
        builder.AppendLine($"AutoRetainerBusy: {dreamSnapshot.AutoRetainerBusy}");
        builder.AppendLine($"IsActive: {dreamSnapshot.IsActive}");
        builder.AppendLine($"CurrentStep: {dreamSnapshot.StepName}");
        builder.AppendLine($"OpenRetainerName: {ValueOrNone(SanitizeText(dreamSnapshot.OpenRetainerName))}");
        builder.AppendLine($"PendingTargets: {dreamSnapshot.PendingTargets}");
        builder.AppendLine($"ActiveTargetIndex: {dreamSnapshot.ActiveTargetIndex}");
        builder.AppendLine($"ActiveTarget: {ValueOrNone(SanitizeText(dreamSnapshot.ActiveTargetSummary))}");
        builder.AppendLine($"RetainerSelectAttempt: {dreamSnapshot.RetainerSelectAttempt}");
        builder.AppendLine($"WithdrawIssued: {dreamSnapshot.WithdrawIssued}");
        builder.AppendLine($"StepElapsedSeconds: {dreamSnapshot.StepElapsed.TotalSeconds:F1}");
        builder.AppendLine($"LastRunSucceeded: {dreamSnapshot.LastRunSucceeded}");
        builder.AppendLine($"CompletionSequence: {dreamSnapshot.CompletionSequence}");
        builder.AppendLine($"StatusIsError: {dreamSnapshot.StatusIsError}");
        builder.AppendLine($"StatusMessage: {ValueOrNone(SanitizeText(dreamSnapshot.StatusMessage))}");
        builder.AppendLine($"VisibleRetainerAddons: {SanitizeText(dreamSnapshot.VisibleRetainerAddons)}");

        builder.AppendLine();
        builder.AppendLine("[Configuration]");
        builder.AppendLine($"UseTransparentOverlayBackground: {this.configuration.UseTransparentOverlayBackground}");
        builder.AppendLine($"OverlayBackgroundOpacity: {this.configuration.OverlayBackgroundOpacity:F2}");
        builder.AppendLine($"ShowVendoredItemsInOverlay: {this.configuration.ShowVendoredItemsInOverlay}");
        builder.AppendLine($"UseAccentForFolderHeaders: {this.configuration.UseAccentForFolderHeaders}");
        builder.AppendLine($"ShowObtainedRawMaterials: {this.configuration.ShowObtainedRawMaterials}");
        builder.AppendLine($"ShowObtainedElementalCatalysts: {this.configuration.ShowObtainedElementalCatalysts}");
        builder.AppendLine($"EstimatedSecondsPerCraft: {this.configuration.EstimatedSecondsPerCraft}");

        builder.AppendLine();
        builder.AppendLine("[RecentLogTail]");
        if (recentLogLines.Count == 0)
        {
            builder.AppendLine("(no log lines available)");
        }
        else
        {
            foreach (var line in recentLogLines)
                builder.AppendLine(SanitizeText(line));
        }

        this.debugReport = builder.ToString();
    }

    private static string DescribeFile(string path, string pluginConfigDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "none";

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
                return $"{SanitizePath(path, pluginConfigDirectory)} (missing)";

            return
                $"{SanitizePath(path, pluginConfigDirectory)} ({fileInfo.Length:N0} bytes, updated {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss})";
        }
        catch (Exception exception)
        {
            return $"{SanitizePath(path, pluginConfigDirectory)} (unavailable: {exception.GetType().Name}: {exception.Message})";
        }
    }

    private static Dictionary<string, string> BuildRetainerAliases(
        IReadOnlyList<StoredRetainerInventory> storedRetainers)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < storedRetainers.Count; index++)
        {
            var name = storedRetainers[index].Name;
            if (!string.IsNullOrWhiteSpace(name) && !aliases.ContainsKey(name))
                aliases[name] = $"Retainer {index + 1}";
        }

        return aliases;
    }

    private string GetRetainerAlias(string? retainerName)
    {
        if (string.IsNullOrWhiteSpace(retainerName))
            return "none";

        return this.retainerAliases.TryGetValue(retainerName, out var alias)
            ? alias
            : "Open Retainer";
    }

    private string SanitizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sanitized = SanitizePath(text, this.pluginConfigDirectory);
        foreach (var (retainerName, alias) in this.retainerAliases.OrderByDescending(entry => entry.Key.Length))
            sanitized = sanitized.Replace(retainerName, alias, StringComparison.OrdinalIgnoreCase);

        return sanitized;
    }

    private static string SanitizePath(string? path, string pluginConfigDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var sanitized = path;
        if (!string.IsNullOrWhiteSpace(pluginConfigDirectory))
            sanitized = sanitized.Replace(pluginConfigDirectory, "<PluginConfig>", StringComparison.OrdinalIgnoreCase);

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            sanitized = sanitized.Replace(userProfile, "<UserProfile>", StringComparison.OrdinalIgnoreCase);

        return sanitized;
    }

    private static string ValueOrNone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value;
}
