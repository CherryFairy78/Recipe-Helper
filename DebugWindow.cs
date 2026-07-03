using System;
using System.IO;
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

        ImGui.TextColored(this.configuration.AccentColor, "Support Debug Report");
        ImGui.TextDisabled("Refresh it, copy it, and send the full report back for troubleshooting.");
        ImGui.Spacing();

        if (ImGui.Button("Refresh report"))
            this.RefreshReport();

        ImGui.SameLine();
        if (ImGui.Button("Copy report"))
        {
            if (string.IsNullOrWhiteSpace(this.debugReport))
                this.RefreshReport();

            ImGui.SetClipboardText(this.debugReport);
            this.copyMessageExpiresAtUtc = DateTime.UtcNow.AddSeconds(3);
        }

        ImGui.SameLine();
        if (ImGui.Button("Clear logs"))
        {
            this.fileLog.ClearLogs();
            this.RefreshReport();
            this.clearMessageExpiresAtUtc = DateTime.UtcNow.AddSeconds(3);
        }

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

        this.generatedAtUtc = DateTimeOffset.UtcNow;
        var builder = new StringBuilder();
        builder.AppendLine("=== Recipe Helper Debug Report ===");
        builder.AppendLine($"GeneratedUtc: {this.generatedAtUtc:O}");
        builder.AppendLine($"PluginVersion: {typeof(Plugin).Assembly.GetName().Version?.ToString() ?? "unknown"}");
        builder.AppendLine($"PluginMode: {(Plugin.PluginInterface.IsDev ? "Development" : "Published")}");
        builder.AppendLine($"ConfigDirectory: {this.pluginConfigDirectory}");
        builder.AppendLine($"LogDirectory: {this.fileLog.LogDirectory}");
        builder.AppendLine($"LatestLogFile: {ValueOrNone(latestLogPath)}");
        builder.AppendLine($"RetainerSnapshotFile: {DescribeFile(this.inventoryService.RetainerSnapshotPath)}");
        builder.AppendLine();
        builder.AppendLine("[Inventory]");
        builder.AppendLine($"LastScannedContainers: {this.inventoryService.LastScannedContainers}");
        builder.AppendLine($"LastScannedSlots: {this.inventoryService.LastScannedSlots}");
        builder.AppendLine($"LastTrackedStacks: {this.inventoryService.LastItemStacks}");
        builder.AppendLine($"StoredRetainers: {storedRetainers.Count}");
        foreach (var retainer in storedRetainers)
        {
            builder.AppendLine(
                $"- {retainer.Name} | captured {retainer.CapturedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} | unique items {retainer.Items.Count}");
        }

        if (storedRetainers.Count == 0)
            builder.AppendLine("- none");

        builder.AppendLine();
        builder.AppendLine("[Dream]");
        builder.AppendLine($"AutoRetainerAvailable: {dreamSnapshot.AutoRetainerAvailable}");
        builder.AppendLine($"AutoRetainerBusy: {dreamSnapshot.AutoRetainerBusy}");
        builder.AppendLine($"IsActive: {dreamSnapshot.IsActive}");
        builder.AppendLine($"CurrentStep: {dreamSnapshot.StepName}");
        builder.AppendLine($"OpenRetainerName: {ValueOrNone(dreamSnapshot.OpenRetainerName)}");
        builder.AppendLine($"PendingTargets: {dreamSnapshot.PendingTargets}");
        builder.AppendLine($"ActiveTargetIndex: {dreamSnapshot.ActiveTargetIndex}");
        builder.AppendLine($"ActiveTarget: {ValueOrNone(dreamSnapshot.ActiveTargetSummary)}");
        builder.AppendLine($"RetainerSelectAttempt: {dreamSnapshot.RetainerSelectAttempt}");
        builder.AppendLine($"WithdrawIssued: {dreamSnapshot.WithdrawIssued}");
        builder.AppendLine($"StepElapsedSeconds: {dreamSnapshot.StepElapsed.TotalSeconds:F1}");
        builder.AppendLine($"LastRunSucceeded: {dreamSnapshot.LastRunSucceeded}");
        builder.AppendLine($"CompletionSequence: {dreamSnapshot.CompletionSequence}");
        builder.AppendLine($"StatusIsError: {dreamSnapshot.StatusIsError}");
        builder.AppendLine($"StatusMessage: {ValueOrNone(dreamSnapshot.StatusMessage)}");
        builder.AppendLine($"VisibleRetainerAddons: {dreamSnapshot.VisibleRetainerAddons}");

        builder.AppendLine();
        builder.AppendLine("[Configuration]");
        builder.AppendLine($"ThemePresetCount: {this.configuration.ThemePresets.Count}");
        builder.AppendLine($"UseTransparentOverlayBackground: {this.configuration.UseTransparentOverlayBackground}");
        builder.AppendLine($"OverlayBackgroundOpacity: {this.configuration.OverlayBackgroundOpacity:F2}");
        builder.AppendLine($"ShowVendoredItemsInOverlay: {this.configuration.ShowVendoredItemsInOverlay}");
        builder.AppendLine($"UseAccentForFolderHeaders: {this.configuration.UseAccentForFolderHeaders}");
        builder.AppendLine($"ShowObtainedRawMaterials: {this.configuration.ShowObtainedRawMaterials}");
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
                builder.AppendLine(line);
        }

        this.debugReport = builder.ToString();
    }

    private static string DescribeFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "none";

        try
        {
            var fileInfo = new FileInfo(path);
            if (!fileInfo.Exists)
                return $"{path} (missing)";

            return
                $"{path} ({fileInfo.Length:N0} bytes, updated {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss})";
        }
        catch (Exception exception)
        {
            return $"{path} (unavailable: {exception.GetType().Name}: {exception.Message})";
        }
    }

    private static string ValueOrNone(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "none" : value;
}
