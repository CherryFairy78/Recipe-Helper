using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace DalamudRecipeHelper;

public sealed class Plugin : IDalamudPlugin
{
    private static readonly string[] PublishedMainCommands = ["/recipehelper", "/rchelp"];
    private static readonly string[] DevelopmentMainCommands = ["/recipehelperdev", "/rchelpdev"];
    private const string PublishedOverlayCommand = "/rhoverlay";
    private const string DevelopmentOverlayCommand = "/rhoverlaydev";

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
    private readonly PluginIntegrationService pluginIntegrationService;
    private readonly Configuration configuration;
    private readonly FileLogService fileLog;
    private readonly SavedPlanStorageService savedPlanStorage;
    private readonly string[] mainCommands;
    private readonly string overlayCommand;

    public Plugin()
    {
        this.fileLog = new FileLogService(PluginInterface.GetPluginConfigDirectory());
        this.fileLog.Info("Plugin", $"Started. Logs: {this.fileLog.LogDirectory}");
        this.mainCommands = PluginInterface.IsDev
            ? DevelopmentMainCommands
            : PublishedMainCommands;
        this.overlayCommand = PluginInterface.IsDev
            ? DevelopmentOverlayCommand
            : PublishedOverlayCommand;
        this.fileLog.Info(
            "Command",
            PluginInterface.IsDev
                ? "Loaded as a development plugin; registering development-only commands."
                : "Loaded as a published plugin; registering published commands.");
        this.configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.configuration.SavedRecipePlans ??= [];
        this.savedPlanStorage = new SavedPlanStorageService(
            PluginInterface.GetPluginConfigDirectory(),
            this.fileLog);
        if (this.savedPlanStorage.RestoreOrMirror(this.configuration))
            PluginInterface.SavePluginConfig(this.configuration);
        this.settingsWindow = new SettingsWindow(this.configuration, this.SaveConfiguration);
        var aetherialReductionService = new AetherialReductionService(DataManager, this.fileLog);
        this.pluginIntegrationService =
            new PluginIntegrationService(
                PluginInterface,
                CommandManager,
                Framework,
                this.fileLog);
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
            this.pluginIntegrationService,
            aetherialReductionService,
            inventoryService,
            recipeService,
            this.configuration);

        this.recipeWindow = new RecipeWindow(
            recipeService,
            inventoryService,
            new TravelService(DataManager, AetheryteList, Framework, Condition, this.fileLog),
            this.pluginIntegrationService,
            aetherialReductionService,
            this.configuration,
            this.OpenSettings,
            this.SaveConfiguration,
            this.rawMaterialsOverlayWindow);
        this.windowSystem.AddWindow(this.recipeWindow);
        this.windowSystem.AddWindow(this.settingsWindow);
        this.windowSystem.AddWindow(this.rawMaterialsOverlayWindow);

        foreach (var command in this.mainCommands)
        {
            CommandManager.AddHandler(command, new CommandInfo(this.OnCommand)
            {
                HelpMessage = "Open Recipe Helper.",
            });
        }

        CommandManager.AddHandler(this.overlayCommand, new CommandInfo(this.OnOverlayCommand)
        {
            HelpMessage = "Toggle the Recipe Helper Missing Items Overlay.",
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
        foreach (var command in this.mainCommands)
            CommandManager.RemoveHandler(command);
        CommandManager.RemoveHandler(this.overlayCommand);
        this.windowSystem.RemoveAllWindows();
        this.pluginIntegrationService.Dispose();
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

    private void OnOverlayCommand(string command, string args)
    {
        this.rawMaterialsOverlayWindow.IsOpen =
            !this.rawMaterialsOverlayWindow.IsOpen;
        this.fileLog.Info(
            "Command",
            this.rawMaterialsOverlayWindow.IsOpen
                ? $"Opened Missing Items Overlay with {command}."
                : $"Closed Missing Items Overlay with {command}.");
    }

    private void Open() => this.recipeWindow.IsOpen = true;

    private void OpenSettings() => this.settingsWindow.IsOpen = true;

    private void SaveConfiguration()
    {
        this.savedPlanStorage.Save(this.configuration.SavedRecipePlans);
        PluginInterface.SavePluginConfig(this.configuration);
    }
}
