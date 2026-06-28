using System;
using System.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace DalamudRecipeHelper;

public sealed class PluginIntegrationService
{
    private readonly ICommandManager commandManager;
    private readonly ICallGateSubscriber<ushort, int, object> artisanCraftItem;
    private readonly FileLogService fileLog;

    public PluginIntegrationService(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        FileLogService fileLog)
    {
        this.commandManager = commandManager;
        this.fileLog = fileLog;
        this.artisanCraftItem =
            pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
    }

    public bool GatherWithGatherBuddy(string itemName, bool isFishing, out string message)
    {
        var safeName = itemName.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        if (safeName.Length == 0)
        {
            message = "The ingredient name is empty.";
            return false;
        }

        var command = isFishing ? "/gatherfish" : "/gather";
        if (!this.commandManager.ProcessCommand($"{command} {safeName}"))
        {
            this.fileLog.Warning("GatherBuddy", $"Command was unavailable for item '{safeName}'.");
            message = "GatherBuddy is not loaded. Enable it and try again.";
            return false;
        }

        message = isFishing
            ? $"GatherBuddy is finding a fishing location for {safeName}."
            : $"GatherBuddy is finding {safeName}.";
        this.fileLog.Info("GatherBuddy", $"Sent '{command} {safeName}'.");
        return true;
    }

    public bool CraftWithArtisan(uint recipeId, uint craftCount, out string message)
    {
        if (recipeId > ushort.MaxValue || craftCount > int.MaxValue)
        {
            message = "This recipe or amount cannot be sent to Artisan.";
            return false;
        }

        try
        {
            this.artisanCraftItem.InvokeAction((ushort)recipeId, (int)craftCount);
            this.fileLog.Info("Artisan", $"Sent recipe {recipeId}, craft count {craftCount}.");
            message = $"Sent {craftCount} craft(s) to Artisan.";
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Could not send recipe {RecipeId} to Artisan.", recipeId);
            this.fileLog.Error("Artisan", $"Could not send recipe {recipeId}.", exception);
            message = "Artisan is not loaded or did not accept this recipe.";
            return false;
        }
    }

    public bool OpenInTeamcraft(uint itemId, uint amount, out string message)
    {
        try
        {
            var listData = $"{itemId},null,{Math.Max(1u, amount)}";
            var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(listData));
            Util.OpenLink($"https://ffxivteamcraft.com/import/{encoded}");
            this.fileLog.Info("Teamcraft", $"Opened import for item {itemId}, amount {amount}.");
            message = "Opened this recipe as a Teamcraft list.";
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Could not open Teamcraft for item {ItemId}.", itemId);
            this.fileLog.Error("Teamcraft", $"Could not open item {itemId}.", exception);
            message = "Teamcraft could not be opened in your browser.";
            return false;
        }
    }
}
