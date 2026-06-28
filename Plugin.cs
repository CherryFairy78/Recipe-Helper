using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DalamudRecipeHelper;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/recipehelper";

    [PluginService]
    internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    [PluginService]
    internal static ICommandManager CommandManager { get; private set; } = null!;

    [PluginService]
    internal static IDataManager DataManager { get; private set; } = null!;

    [PluginService]
    internal static IChatGui ChatGui { get; private set; } = null!;

    [PluginService]
    internal static IPluginLog Log { get; private set; } = null!;

    [PluginService]
    internal static IAetheryteList AetheryteList { get; private set; } = null!;

    [PluginService]
    internal static IFramework Framework { get; private set; } = null!;

    [PluginService]
    internal static ICondition Condition { get; private set; } = null!;

    [PluginService]
    internal static IPlayerState PlayerState { get; private set; } = null!;

    [PluginService]
    internal static IGameInventory GameInventory { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("DalamudRecipeHelper");
    private readonly RecipeWindow recipeWindow;
    private readonly SettingsWindow settingsWindow;
    private readonly RawMaterialsOverlayWindow rawMaterialsOverlayWindow;
    private readonly Configuration configuration;
    private readonly FileLogService fileLog;

    public Plugin()
    {
        this.fileLog = new FileLogService(PluginInterface.GetPluginConfigDirectory());
        this.fileLog.Info("Plugin", $"Started. Logs: {this.fileLog.LogDirectory}");
        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.settingsWindow = new SettingsWindow(this.configuration, this.SaveConfiguration);
        var aetherialReductionService = new AetherialReductionService(DataManager, this.fileLog);
        var pluginIntegrationService =
            new PluginIntegrationService(PluginInterface, CommandManager, this.fileLog);
        var retainerSnapshotService = new RetainerSnapshotService(
            Framework,
            PlayerState,
            PluginInterface.GetPluginConfigDirectory(),
            this.fileLog);
        var inventoryService = new InventoryService(
            this.fileLog,
            retainerSnapshotService,
            GameInventory);
        var recipeService = new RecipeService(
            DataManager,
            this.fileLog,
            aetherialReductionService);
        this.rawMaterialsOverlayWindow = new RawMaterialsOverlayWindow(
            pluginIntegrationService,
            aetherialReductionService,
            inventoryService,
            recipeService,
            this.configuration);

        this.recipeWindow = new RecipeWindow(
            recipeService,
            inventoryService,
            new TravelService(DataManager, AetheryteList, Framework, Condition, this.fileLog),
            pluginIntegrationService,
            aetherialReductionService,
            this.configuration,
            this.OpenSettings,
            this.rawMaterialsOverlayWindow);
        this.windowSystem.AddWindow(this.recipeWindow);
        this.windowSystem.AddWindow(this.settingsWindow);
        this.windowSystem.AddWindow(this.rawMaterialsOverlayWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open Recipe Helper.",
        });

        PluginInterface.UiBuilder.Draw += this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi += this.Open;
    }

    public void Dispose()
    {
        this.fileLog.Info("Plugin", "Stopping.");
        PluginInterface.UiBuilder.Draw -= this.windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettings;
        PluginInterface.UiBuilder.OpenMainUi -= this.Open;
        CommandManager.RemoveHandler(CommandName);
        this.windowSystem.RemoveAllWindows();
        this.rawMaterialsOverlayWindow.Dispose();
        this.recipeWindow.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        this.fileLog.Info("Command", string.IsNullOrWhiteSpace(args)
            ? "Opened Recipe Helper."
            : $"Opened Recipe Helper with search text '{args.Trim()}'.");

        if (!string.IsNullOrWhiteSpace(args))
        {
            this.recipeWindow.SearchText = args.Trim();
            this.recipeWindow.RefreshSearch();
        }

        this.Open();
    }

    private void Open() => this.recipeWindow.IsOpen = true;

    private void OpenSettings() => this.settingsWindow.IsOpen = true;

    private void SaveConfiguration() => PluginInterface.SavePluginConfig(this.configuration);
}
