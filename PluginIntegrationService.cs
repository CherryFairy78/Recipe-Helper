using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;

namespace DalamudRecipeHelper;

public sealed class PluginIntegrationService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly ICallGateSubscriber<ushort, int, object> artisanCraftItem;
    private readonly ICallGateSubscriber<bool> artisanIsBusy;
    private readonly FileLogService fileLog;
    private readonly Queue<(ushort RecipeId, int CraftCount)> craftAllQueue = new();
    private bool craftAllActive;
    private DateTime nextCraftAllCheck;

    public PluginIntegrationService(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IFramework framework,
        FileLogService fileLog)
    {
        this.commandManager = commandManager;
        this.framework = framework;
        this.fileLog = fileLog;
        this.artisanCraftItem =
            pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        this.artisanIsBusy =
            pluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public void Dispose() =>
        this.framework.Update -= this.OnFrameworkUpdate;

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

    public bool CraftAllWithArtisan(
        IReadOnlyList<ArtisanCraftQueueEntry> recipes,
        out string message)
    {
        if (recipes.Count == 0)
        {
            message = "There are no selected recipes to craft.";
            return false;
        }

        if (this.craftAllActive)
        {
            message = "A Craft All queue is already running in Artisan.";
            return false;
        }

        if (recipes.Any(recipe =>
                recipe.RecipeId > ushort.MaxValue ||
                recipe.CraftCount > int.MaxValue))
        {
            message = "One of the selected recipes cannot be sent to Artisan.";
            return false;
        }

        this.craftAllQueue.Clear();
        foreach (var recipe in recipes)
        {
            this.craftAllQueue.Enqueue((
                (ushort)recipe.RecipeId,
                (int)recipe.CraftCount));
        }

        this.craftAllActive = true;
        if (!this.TryDispatchNextCraft(out var dispatchError))
        {
            this.craftAllActive = false;
            this.craftAllQueue.Clear();
            message = dispatchError;
            return false;
        }

        this.fileLog.Info(
            "Artisan",
            $"Started Craft All queue with {recipes.Count} batch(es), including " +
            $"{recipes.Count(recipe => recipe.IsIntermediate)} intermediate batch(es).");
        message =
            $"Queued {recipes.Count} crafting batch(es) with Artisan, intermediates first.";
        return true;
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

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.craftAllActive || DateTime.UtcNow < this.nextCraftAllCheck)
            return;

        try
        {
            if (this.artisanIsBusy.InvokeFunc())
            {
                this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
                return;
            }
        }
        catch (Exception exception)
        {
            this.fileLog.Error(
                "Artisan",
                "Could not check Artisan while processing Craft All.",
                exception);
            this.craftAllActive = false;
            this.craftAllQueue.Clear();
            return;
        }

        if (this.craftAllQueue.Count == 0)
        {
            this.craftAllActive = false;
            this.fileLog.Info("Artisan", "Craft All queue completed.");
            return;
        }

        if (!this.TryDispatchNextCraft(out var error))
        {
            this.fileLog.Warning("Artisan", error);
            this.craftAllActive = false;
            this.craftAllQueue.Clear();
        }
    }

    private bool TryDispatchNextCraft(out string error)
    {
        if (!this.craftAllQueue.TryDequeue(out var next))
        {
            error = string.Empty;
            return true;
        }

        try
        {
            this.artisanCraftItem.InvokeAction(next.RecipeId, next.CraftCount);
            this.nextCraftAllCheck = DateTime.UtcNow.AddSeconds(2);
            this.fileLog.Info(
                "Artisan",
                $"Craft All sent recipe {next.RecipeId}, craft count {next.CraftCount}.");
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(
                exception,
                "Could not send Craft All recipe {RecipeId} to Artisan.",
                next.RecipeId);
            this.fileLog.Error(
                "Artisan",
                $"Could not send Craft All recipe {next.RecipeId}.",
                exception);
            error = "Artisan is not loaded or did not accept the Craft All queue.";
            return false;
        }
    }
}
