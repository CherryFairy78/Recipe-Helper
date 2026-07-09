using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Gui;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DalamudRecipeHelper;

public sealed unsafe class PluginIntegrationService : IDisposable
{
    private readonly ICommandManager commandManager;
    private readonly ICondition condition;
    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly IGameInventory gameInventory;
    private readonly ICallGateSubscriber<ushort, int, object> artisanCraftItem;
    private readonly ICallGateSubscriber<bool> artisanIsBusy;
    private readonly ICallGateSubscriber<bool, object> artisanSetEnduranceStatus;
    private readonly ICallGateSubscriber<bool> autoRetainerIsBusy;
    private readonly FileLogService fileLog;
    private readonly Queue<ArtisanCraftQueueEntry> craftAllQueue = new();
    private readonly List<ArtisanCraftQueueEntry> orderedCraftAllEntries = [];
    private readonly List<ArtisanCraftQueueEntry> completedCraftAllEntries = [];
    private RecipeService? recipeService;
    private InventoryService? inventoryService;
    private GwenDreamService? gwenDreamService;
    private ArtisanCraftQueueEntry? activeCraftAllEntry;
    private bool craftAllActive;
    private bool craftAllEntryStarted;
    private bool craftAllPausedForAutoRetainer;
    private bool craftAllPausedForRetainerRefill;
    private bool dreamRetainerRefillEnabled;
    private DateTime craftAllStartedAt;
    private DateTime craftAllFinishedAt;
    private DateTime activeCraftAllDispatchedAt;
    private DateTime activeEntryBusySince;
    private TimeSpan activeEntryBusyElapsed;
    private uint activeEntryCompletedCrafts;
    private uint activeEntryCompletedCraftsAtDispatchStart;
    private uint activeEntryResultQuantityAtDispatchStart;
    private bool activeEntryStopRequested;
    private bool stopAfterCurrentCraftRequested;
    private DateTime autoRetainerReleasedAt;
    private DateTime nextCraftAllCheck;
    private bool wasCraftingConditionActive;
    private uint observedDreamCompletionSequence;
    private static readonly TimeSpan AutoRetainerResumeGracePeriod = TimeSpan.FromSeconds(8);

    public uint CraftAllCompletionCount { get; private set; }

    public uint CraftAllStopCount { get; private set; }

    public PluginIntegrationService(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        ICondition condition,
        IFramework framework,
        IGameInventory gameInventory,
        IGameGui gameGui,
        FileLogService fileLog)
    {
        this.commandManager = commandManager;
        this.condition = condition;
        this.framework = framework;
        this.gameInventory = gameInventory;
        this.gameGui = gameGui;
        this.fileLog = fileLog;
        this.artisanCraftItem =
            pluginInterface.GetIpcSubscriber<ushort, int, object>("Artisan.CraftItem");
        this.artisanIsBusy =
            pluginInterface.GetIpcSubscriber<bool>("Artisan.IsBusy");
        this.artisanSetEnduranceStatus =
            pluginInterface.GetIpcSubscriber<bool, object>("Artisan.SetEnduranceStatus");
        this.autoRetainerIsBusy =
            pluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy");
        this.framework.Update += this.OnFrameworkUpdate;
        this.gameInventory.InventoryChanged += this.OnInventoryChanged;
    }

    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;
        this.gameInventory.InventoryChanged -= this.OnInventoryChanged;
    }

    public void AttachDreamSupport(
        RecipeService recipeService,
        InventoryService inventoryService,
        GwenDreamService gwenDreamService)
    {
        this.recipeService = recipeService;
        this.inventoryService = inventoryService;
        this.gwenDreamService = gwenDreamService;
        this.observedDreamCompletionSequence = gwenDreamService.CompletionSequence;
    }

    public ArtisanCraftProgressSnapshot GetCraftAllProgressSnapshot()
    {
        var pendingEntries = new List<ArtisanCraftQueueEntry>();
        if (this.activeCraftAllEntry is { } activeEntry)
            pendingEntries.Add(activeEntry);
        pendingEntries.AddRange(this.craftAllQueue);
        var elapsed = TimeSpan.Zero;
        var currentEntryElapsed = this.activeEntryBusyElapsed;
        if (this.activeEntryBusySince != DateTime.MinValue)
            currentEntryElapsed += DateTime.UtcNow - this.activeEntryBusySince;
        if (this.craftAllStartedAt != DateTime.MinValue)
        {
            var elapsedUntil = this.craftAllActive
                ? DateTime.UtcNow
                : this.craftAllFinishedAt != DateTime.MinValue
                    ? this.craftAllFinishedAt
                    : DateTime.UtcNow;
            if (elapsedUntil >= this.craftAllStartedAt)
                elapsed = elapsedUntil - this.craftAllStartedAt;
        }

        return new ArtisanCraftProgressSnapshot(
            this.craftAllActive || pendingEntries.Count > 0,
            this.activeCraftAllEntry,
            this.craftAllEntryStarted,
            this.activeEntryCompletedCrafts,
            this.craftAllPausedForAutoRetainer,
            this.craftAllPausedForRetainerRefill,
            this.stopAfterCurrentCraftRequested,
            elapsed,
            currentEntryElapsed,
            this.orderedCraftAllEntries.ToList(),
            pendingEntries,
            this.completedCraftAllEntries.ToList());
    }

    public bool RequestStopAfterCurrentCraft(out string message)
    {
        if (!this.craftAllActive || this.activeCraftAllEntry is null)
        {
            message = "There is no active Artisan craft to stop.";
            return false;
        }

        if (this.stopAfterCurrentCraftRequested)
        {
            message = "Artisan is already set to stop after the current craft.";
            return false;
        }

        try
        {
            this.artisanSetEnduranceStatus.InvokeAction(false);
            this.stopAfterCurrentCraftRequested = true;
            this.activeEntryStopRequested = true;
            this.fileLog.Info(
                "Artisan",
                $"Requested stop after the current craft for recipe {this.activeCraftAllEntry.RecipeId}.");
            message = "Artisan will stop after the current craft.";
            return true;
        }
        catch (Exception exception)
        {
            Plugin.Log.Warning(exception, "Could not request Artisan stop after the current craft.");
            this.fileLog.Error("Artisan", "Could not request stop after the current craft.", exception);
            message = "Artisan did not accept the stop request.";
            return false;
        }
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

    public bool CraftWithArtisan(ArtisanCraftQueueEntry recipe, out string message)
    {
        if (recipe.RecipeId > ushort.MaxValue || recipe.CraftCount > int.MaxValue)
        {
            message = "This recipe or amount cannot be sent to Artisan.";
            return false;
        }

        try
        {
            this.ResetTrackedCraftState();
            this.activeCraftAllEntry = recipe with { QueueSequence = 1 };
            this.orderedCraftAllEntries.Add(this.activeCraftAllEntry);
            this.craftAllActive = true;
            this.PrepareDispatchInventoryBaseline();
            this.activeCraftAllDispatchedAt = DateTime.UtcNow;
            this.nextCraftAllCheck = DateTime.UtcNow.AddSeconds(2);
            this.artisanCraftItem.InvokeAction((ushort)recipe.RecipeId, (int)recipe.CraftCount);
            this.fileLog.Info("Artisan", $"Sent recipe {recipe.RecipeId}, craft count {recipe.CraftCount}.");
            message = $"Sent {recipe.CraftCount} craft(s) to Artisan.";
            return true;
        }
        catch (Exception exception)
        {
            this.craftAllActive = false;
            this.activeCraftAllEntry = null;
            this.craftAllFinishedAt = DateTime.UtcNow;
            this.activeEntryBusySince = DateTime.MinValue;
            this.activeEntryBusyElapsed = TimeSpan.Zero;
            this.activeEntryCompletedCraftsAtDispatchStart = 0;
            this.activeEntryResultQuantityAtDispatchStart = 0;
            this.activeEntryStopRequested = false;
            this.stopAfterCurrentCraftRequested = false;
            Plugin.Log.Warning(exception, "Could not send recipe {RecipeId} to Artisan.", recipe.RecipeId);
            this.fileLog.Error("Artisan", $"Could not send recipe {recipe.RecipeId}.", exception);
            message = "Artisan is not loaded or did not accept this recipe.";
            return false;
        }
    }

    public bool CraftWithArtisan(uint recipeId, uint craftCount, out string message) =>
        this.CraftWithArtisan(
            new ArtisanCraftQueueEntry(
                recipeId,
                0,
                $"Recipe {recipeId}",
                1,
                craftCount,
                false,
                1),
            out message);

    public bool CraftAllWithArtisan(
        IReadOnlyList<ArtisanCraftQueueEntry> recipes,
        int recipePlanCount,
        bool enableDreamRetainerRefill,
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

        this.ResetTrackedCraftState();
        this.dreamRetainerRefillEnabled = enableDreamRetainerRefill;
        var queueSequence = 1;
        foreach (var recipe in recipes)
        {
            var queuedRecipe = recipe with { QueueSequence = queueSequence++ };
            this.craftAllQueue.Enqueue(queuedRecipe);
            this.orderedCraftAllEntries.Add(queuedRecipe);
        }

        this.craftAllActive = true;
        if (!this.TryDispatchNextCraft(out var dispatchError))
        {
            this.craftAllActive = false;
            this.craftAllQueue.Clear();
            this.activeCraftAllEntry = null;
            this.orderedCraftAllEntries.Clear();
            this.completedCraftAllEntries.Clear();
            this.craftAllEntryStarted = false;
            this.craftAllPausedForAutoRetainer = false;
            this.craftAllFinishedAt = DateTime.UtcNow;
            this.autoRetainerReleasedAt = DateTime.MinValue;
            this.stopAfterCurrentCraftRequested = false;
            message = dispatchError;
            return false;
        }

        var preCraftCount = recipes
            .Where(recipe => recipe.IsIntermediate)
            .Aggregate(
                0UL,
                (total, recipe) => total + recipe.CraftCount);
        var finalRecipeCount = recipes.Count(recipe => !recipe.IsIntermediate);
        var queuedItemCount = recipePlanCount > 0
            ? recipePlanCount
            : finalRecipeCount;
        var queuedItemName = recipePlanCount > 0
            ? queuedItemCount == 1 ? "recipe plan" : "recipe plans"
            : queuedItemCount == 1 ? "recipe" : "recipes";
        this.fileLog.Info(
            "Artisan",
            $"Started Craft All queue with {recipes.Count} batch(es), including " +
            $"{recipes.Count(recipe => recipe.IsIntermediate)} pre-craft batch(es).");
        message = preCraftCount > 0
            ? $"Queued {queuedItemCount} {queuedItemName} with " +
              $"{preCraftCount:N0} pre-craft{(preCraftCount == 1 ? string.Empty : "s")} in Artisan."
            : $"Queued {queuedItemCount} {queuedItemName} with Artisan.";
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
        this.UpdateCraftingStateFromGameState();

        if (!this.craftAllActive)
            return;

        if (this.TryHandleDreamRetainerRefillPause())
            return;

        if (DateTime.UtcNow < this.nextCraftAllCheck)
            return;

        bool artisanBusy;
        try
        {
            artisanBusy = this.artisanIsBusy.InvokeFunc();
        }
        catch (Exception exception)
        {
            this.fileLog.Error(
                "Artisan",
                "Could not check Artisan while processing Craft All.",
                exception);
            this.craftAllActive = false;
            this.craftAllQueue.Clear();
            this.activeCraftAllEntry = null;
            this.orderedCraftAllEntries.Clear();
            this.completedCraftAllEntries.Clear();
            this.craftAllEntryStarted = false;
            this.craftAllPausedForAutoRetainer = false;
            this.craftAllFinishedAt = DateTime.UtcNow;
            this.activeEntryBusySince = DateTime.MinValue;
            this.activeEntryBusyElapsed = TimeSpan.Zero;
            this.activeEntryStopRequested = false;
            this.stopAfterCurrentCraftRequested = false;
            this.autoRetainerReleasedAt = DateTime.MinValue;
            return;
        }

        var autoRetainerBusy = this.IsAutoRetainerBusy();

        if (this.activeCraftAllEntry is { } activeEntry)
        {
            if (autoRetainerBusy)
            {
                if (!this.craftAllPausedForAutoRetainer)
                {
                    this.fileLog.Info(
                        "Artisan",
                        "Craft All paused while AutoRetainer is processing retainers.");
                }

                this.craftAllPausedForAutoRetainer = true;
                this.UpdateActiveEntryBusyTime(false);
                this.autoRetainerReleasedAt = DateTime.MinValue;
                this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
                return;
            }

            if (this.craftAllPausedForAutoRetainer)
            {
                if (this.autoRetainerReleasedAt == DateTime.MinValue)
                {
                    this.autoRetainerReleasedAt = DateTime.UtcNow;
                    this.fileLog.Info(
                        "Artisan",
                        "AutoRetainer finished. Waiting for Artisan to resume Craft All.");
                }

                if (artisanBusy)
                {
                    this.craftAllPausedForAutoRetainer = false;
                    this.autoRetainerReleasedAt = DateTime.MinValue;
                    this.craftAllEntryStarted = true;
                    this.UpdateActiveEntryBusyTime(true);
                    this.fileLog.Info(
                        "Artisan",
                        "Artisan resumed after AutoRetainer finished.");
                    this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
                    return;
                }

                if (DateTime.UtcNow - this.autoRetainerReleasedAt < AutoRetainerResumeGracePeriod)
                {
                    this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
                    return;
                }

                this.craftAllPausedForAutoRetainer = false;
                this.autoRetainerReleasedAt = DateTime.MinValue;
                this.SyncActiveEntryProgressFromInventory();

                if (this.stopAfterCurrentCraftRequested)
                {
                    this.StopCraftAllQueue(false);
                    return;
                }

                var remainingCraftsAfterPause = this.GetRemainingCraftCount(activeEntry);
                if (remainingCraftsAfterPause == 0)
                {
                    this.fileLog.Info(
                        "Artisan",
                        $"AutoRetainer release found recipe {activeEntry.RecipeId} already complete. Finalizing entry.");
                    this.CompleteActiveCraftAllEntry();
                    return;
                }

                this.fileLog.Info(
                    "Artisan",
                    $"AutoRetainer interrupted recipe {activeEntry.RecipeId}. Retrying the remaining {remainingCraftsAfterPause:N0} craft(s).");
                if (!this.TryDispatchNextCraft(out var retryAfterPauseError))
                {
                    this.fileLog.Warning("Artisan", retryAfterPauseError);
                    this.craftAllActive = false;
                    this.craftAllQueue.Clear();
                    this.activeCraftAllEntry = null;
                    this.orderedCraftAllEntries.Clear();
                    this.completedCraftAllEntries.Clear();
                    this.craftAllEntryStarted = false;
                    this.craftAllFinishedAt = DateTime.UtcNow;
                    this.activeEntryBusySince = DateTime.MinValue;
                    this.activeEntryBusyElapsed = TimeSpan.Zero;
                    this.activeEntryCompletedCrafts = 0;
                    this.activeEntryCompletedCraftsAtDispatchStart = 0;
                    this.activeEntryResultQuantityAtDispatchStart = 0;
                    this.activeEntryStopRequested = false;
                    this.stopAfterCurrentCraftRequested = false;
                }

                return;
            }

            if (artisanBusy)
            {
                this.craftAllEntryStarted = true;
                this.UpdateActiveEntryBusyTime(true);
                this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
                return;
            }

            this.UpdateActiveEntryBusyTime(false);
            this.SyncActiveEntryProgressFromInventory();

            if (!this.craftAllEntryStarted &&
                DateTime.UtcNow - this.activeCraftAllDispatchedAt < TimeSpan.FromSeconds(8))
            {
                this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
                return;
            }

            if (!this.craftAllEntryStarted)
            {
                if (this.stopAfterCurrentCraftRequested)
                {
                    this.StopCraftAllQueue(false);
                    return;
                }

                if (this.recipeService is not null && this.inventoryService is not null)
                {
                    var maxCraftableNow = this.recipeService.GetMaximumCraftableCountFromCurrentInventory(
                        activeEntry.RecipeId,
                        1,
                        this.inventoryService.GetImmediatelyUsableItems());
                    if (maxCraftableNow == 0)
                    {
                        this.fileLog.Warning(
                            "Artisan",
                            $"Craft All batch {activeEntry.RecipeId} could not start because the required materials are no longer in inventory. Stopping the queue.");
                        this.StopCraftAllQueue(false);
                        return;
                    }
                }

                this.fileLog.Warning(
                    "Artisan",
                    $"Craft All batch {activeEntry.RecipeId} did not start. Retrying.");
                if (!this.TryDispatchNextCraft(out var retryError))
                {
                    this.fileLog.Warning("Artisan", retryError);
                    this.craftAllActive = false;
                    this.craftAllQueue.Clear();
                    this.activeCraftAllEntry = null;
                    this.orderedCraftAllEntries.Clear();
                    this.completedCraftAllEntries.Clear();
                    this.craftAllEntryStarted = false;
                    this.craftAllPausedForAutoRetainer = false;
                    this.activeEntryBusySince = DateTime.MinValue;
                    this.activeEntryBusyElapsed = TimeSpan.Zero;
                    this.activeEntryCompletedCrafts = 0;
                    this.activeEntryCompletedCraftsAtDispatchStart = 0;
                    this.activeEntryResultQuantityAtDispatchStart = 0;
                    this.activeEntryStopRequested = false;
                    this.stopAfterCurrentCraftRequested = false;
                    this.autoRetainerReleasedAt = DateTime.MinValue;
                }

                return;
            }

            if (this.stopAfterCurrentCraftRequested)
            {
                var completedActiveEntry = this.activeEntryCompletedCrafts >= activeEntry.CraftCount;
                this.StopCraftAllQueue(completedActiveEntry);
                return;
            }

            var remainingCrafts = this.GetRemainingCraftCount(activeEntry);
            if (remainingCrafts > 0)
            {
                this.fileLog.Info(
                    "Artisan",
                    $"Craft All batch for recipe {activeEntry.RecipeId} finished {this.activeEntryCompletedCrafts:N0}/{activeEntry.CraftCount:N0} craft(s). Continuing the remaining {remainingCrafts:N0}.");
                if (!this.TryDispatchNextCraft(out var partialError))
                {
                    this.fileLog.Warning("Artisan", partialError);
                    this.AbortCraftAllQueue();
                }

                return;
            }

            this.fileLog.Info(
                "Artisan",
                $"Craft All completed recipe {activeEntry.RecipeId}, craft count {activeEntry.CraftCount}.");
            this.CompleteActiveCraftAllEntry();
            if (!this.craftAllActive)
                return;

            if (!this.TryDispatchNextCraft(out var error))
            {
                this.fileLog.Warning("Artisan", error);
                this.craftAllActive = false;
                this.craftAllQueue.Clear();
                this.orderedCraftAllEntries.Clear();
                this.completedCraftAllEntries.Clear();
                this.craftAllPausedForAutoRetainer = false;
                this.craftAllFinishedAt = DateTime.UtcNow;
                this.activeEntryBusySince = DateTime.MinValue;
                this.activeEntryBusyElapsed = TimeSpan.Zero;
                this.activeEntryCompletedCrafts = 0;
                this.activeEntryCompletedCraftsAtDispatchStart = 0;
                this.activeEntryResultQuantityAtDispatchStart = 0;
                this.activeEntryStopRequested = false;
                this.stopAfterCurrentCraftRequested = false;
                this.autoRetainerReleasedAt = DateTime.MinValue;
            }

            return;
        }

        if (artisanBusy)
            this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
    }

    private bool TryDispatchNextCraft(out string error)
    {
        if (this.activeCraftAllEntry is null)
        {
            if (!this.craftAllQueue.TryPeek(out var queuedEntry))
            {
                error = string.Empty;
                return true;
            }

            this.activeCraftAllEntry = queuedEntry;
            this.craftAllEntryStarted = false;
            this.activeEntryBusySince = DateTime.MinValue;
            this.activeEntryBusyElapsed = TimeSpan.Zero;
            this.activeEntryCompletedCrafts = 0;
            this.activeEntryCompletedCraftsAtDispatchStart = 0;
            this.activeEntryResultQuantityAtDispatchStart = 0;
            this.activeEntryStopRequested = false;
            this.stopAfterCurrentCraftRequested = false;
        }

        var next = this.activeCraftAllEntry!;
        var shouldDequeueQueuedEntry =
            this.craftAllQueue.TryPeek(out var queuedEntryForDispatch) &&
            queuedEntryForDispatch.QueueSequence == next.QueueSequence;
        var dispatchCraftCount = this.GetRemainingCraftCount(next);
        if (!this.TryPrepareDispatchCraftCount(next, ref dispatchCraftCount, out var pausedForRetainerRefill, out error))
            return false;

        if (pausedForRetainerRefill)
            return true;

        if (dispatchCraftCount == 0)
        {
            error = string.Empty;
            this.CompleteActiveCraftAllEntry();
            return true;
        }

        try
        {
            this.craftAllEntryStarted = false;
            this.activeEntryBusySince = DateTime.MinValue;
            this.PrepareDispatchInventoryBaseline();
            this.artisanCraftItem.InvokeAction((ushort)next.RecipeId, (int)dispatchCraftCount);
            this.activeCraftAllDispatchedAt = DateTime.UtcNow;
            this.nextCraftAllCheck = DateTime.UtcNow.AddSeconds(2);
            if (shouldDequeueQueuedEntry)
                this.craftAllQueue.Dequeue();
            this.fileLog.Info(
                "Artisan",
                $"Craft All sent recipe {next.RecipeId}, craft count {dispatchCraftCount}.");
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
            this.activeCraftAllEntry = null;
            this.orderedCraftAllEntries.Clear();
            this.completedCraftAllEntries.Clear();
            this.craftAllEntryStarted = false;
            this.craftAllPausedForAutoRetainer = false;
            this.craftAllFinishedAt = DateTime.UtcNow;
            this.activeEntryBusySince = DateTime.MinValue;
            this.activeEntryBusyElapsed = TimeSpan.Zero;
            this.activeEntryCompletedCrafts = 0;
            this.activeEntryCompletedCraftsAtDispatchStart = 0;
            this.activeEntryResultQuantityAtDispatchStart = 0;
            this.activeEntryStopRequested = false;
            this.stopAfterCurrentCraftRequested = false;
            this.autoRetainerReleasedAt = DateTime.MinValue;
            return false;
        }
    }

    private bool TryPrepareDispatchCraftCount(
        ArtisanCraftQueueEntry entry,
        ref uint dispatchCraftCount,
        out bool pausedForRetainerRefill,
        out string error)
    {
        pausedForRetainerRefill = false;
        error = string.Empty;

        if (!this.dreamRetainerRefillEnabled ||
            this.recipeService is null ||
            this.inventoryService is null ||
            this.gwenDreamService is null)
        {
            return true;
        }

        var liveOwnedItems = this.inventoryService.GetImmediatelyUsableItems();
        var maxCraftableNow = this.recipeService.GetMaximumCraftableCountFromCurrentCatalysts(
            entry.RecipeId,
            dispatchCraftCount,
            liveOwnedItems,
            out var usesCatalysts);
        if (!usesCatalysts)
            return true;

        if (maxCraftableNow > 0)
        {
            if (maxCraftableNow < dispatchCraftCount)
            {
                this.fileLog.Info(
                    "Artisan",
                    $"Limiting recipe {entry.RecipeId} to {maxCraftableNow:N0} craft(s) until more crystals are withdrawn.");
                dispatchCraftCount = maxCraftableNow;
            }

            return true;
        }

        if (!this.recipeService.TryBuildDreamCatalystTopUpTargets(
                entry.RecipeId,
                dispatchCraftCount,
                liveOwnedItems,
                this.inventoryService.GetStoredRetainers(),
                out var targets,
                out error))
        {
            return false;
        }

        if (targets.Count == 0)
        {
            error = $"No retainer crystal top-up is available for {entry.ResultName}.";
            return false;
        }

        if (!this.gwenDreamService.TryStartTargets(targets))
        {
            error = string.IsNullOrWhiteSpace(this.gwenDreamService.StatusMessage)
                ? $"Could not start a crystal top-up for {entry.ResultName}."
                : this.gwenDreamService.StatusMessage;
            return false;
        }

        this.craftAllPausedForRetainerRefill = true;
        this.observedDreamCompletionSequence = this.gwenDreamService.CompletionSequence;
        this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
        this.fileLog.Info(
            "Artisan",
            $"Paused Craft All so Gwen's Dream can top up crystals for recipe {entry.RecipeId}.");
        pausedForRetainerRefill = true;
        return true;
    }

    private void ResetTrackedCraftState()
    {
        this.craftAllQueue.Clear();
        this.orderedCraftAllEntries.Clear();
        this.completedCraftAllEntries.Clear();
        this.activeCraftAllEntry = null;
        this.craftAllEntryStarted = false;
        this.craftAllPausedForAutoRetainer = false;
        this.craftAllPausedForRetainerRefill = false;
        this.dreamRetainerRefillEnabled = false;
        this.craftAllStartedAt = DateTime.UtcNow;
        this.craftAllFinishedAt = DateTime.MinValue;
        this.activeEntryBusySince = DateTime.MinValue;
        this.activeEntryBusyElapsed = TimeSpan.Zero;
        this.activeEntryCompletedCrafts = 0;
        this.activeEntryCompletedCraftsAtDispatchStart = 0;
        this.activeEntryResultQuantityAtDispatchStart = 0;
        this.activeEntryStopRequested = false;
        this.stopAfterCurrentCraftRequested = false;
        this.autoRetainerReleasedAt = DateTime.MinValue;
        this.wasCraftingConditionActive = false;
    }

    private void UpdateCraftingStateFromGameState()
    {
        var isCraftingNow =
            this.condition[ConditionFlag.Crafting] ||
            this.condition[ConditionFlag.ExecutingCraftingAction];

        if (this.activeCraftAllEntry is not null && isCraftingNow)
            this.craftAllEntryStarted = true;

        this.wasCraftingConditionActive = isCraftingNow;
    }

    private void OnInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        if (this.activeCraftAllEntry is null ||
            !this.craftAllEntryStarted ||
            this.activeCraftAllEntry.ResultItemId == 0 ||
            this.activeCraftAllEntry.ResultAmount == 0)
        {
            return;
        }

        uint craftedQuantity = 0;
        foreach (var inventoryEvent in events)
        {
            craftedQuantity += this.GetCraftedQuantityDelta(inventoryEvent, this.activeCraftAllEntry.ResultItemId);
        }

        if (craftedQuantity < this.activeCraftAllEntry.ResultAmount)
            return;

        var completedCrafts = craftedQuantity / this.activeCraftAllEntry.ResultAmount;
        if (completedCrafts == 0)
            return;

        this.activeEntryCompletedCrafts = Math.Min(
            this.activeCraftAllEntry.CraftCount,
            this.activeEntryCompletedCrafts + completedCrafts);
        this.SyncActiveEntryProgressFromInventory();

        if (this.activeEntryCompletedCrafts >= this.activeCraftAllEntry.CraftCount)
            this.TryStopArtisanForCompletedEntry();
    }

    private void PrepareDispatchInventoryBaseline()
    {
        this.activeEntryCompletedCraftsAtDispatchStart = this.activeEntryCompletedCrafts;
        this.activeEntryResultQuantityAtDispatchStart = this.GetLiveResultQuantityForActiveEntry();
    }

    private void SyncActiveEntryProgressFromInventory()
    {
        if (this.activeCraftAllEntry is not { } activeEntry ||
            activeEntry.ResultItemId == 0 ||
            activeEntry.ResultAmount == 0)
        {
            return;
        }

        var currentQuantity = this.GetLiveResultQuantityForActiveEntry();
        if (currentQuantity < this.activeEntryResultQuantityAtDispatchStart)
            return;

        var producedQuantity = currentQuantity - this.activeEntryResultQuantityAtDispatchStart;
        var observedCrafts = producedQuantity / activeEntry.ResultAmount;
        if (observedCrafts == 0)
            return;

        var syncedCrafts = Math.Min(
            activeEntry.CraftCount,
            this.activeEntryCompletedCraftsAtDispatchStart + observedCrafts);
        if (syncedCrafts <= this.activeEntryCompletedCrafts)
            return;

        this.activeEntryCompletedCrafts = syncedCrafts;
    }

    private uint GetLiveResultQuantityForActiveEntry()
    {
        if (this.inventoryService is null || this.activeCraftAllEntry is not { } activeEntry)
            return 0;

        return this.inventoryService
            .GetImmediatelyUsableItems()
            .GetValueOrDefault(activeEntry.ResultItemId)?
            .Quantity ?? 0;
    }

    private uint GetCraftedQuantityDelta(InventoryEventArgs inventoryEvent, uint resultItemId)
    {
        return inventoryEvent switch
        {
            InventoryItemAddedArgs addedArgs => this.GetMatchingQuantity(addedArgs.Item, resultItemId),
            InventoryItemChangedArgs changedArgs => this.GetChangedQuantityDelta(changedArgs, resultItemId),
            _ => 0,
        };
    }

    private uint GetChangedQuantityDelta(InventoryItemChangedArgs changedArgs, uint resultItemId)
    {
        if (changedArgs.Item.BaseItemId != resultItemId ||
            changedArgs.OldItemState.BaseItemId != resultItemId ||
            changedArgs.Item.Quantity <= changedArgs.OldItemState.Quantity)
        {
            return 0;
        }

        return (uint)(changedArgs.Item.Quantity - changedArgs.OldItemState.Quantity);
    }

    private uint GetMatchingQuantity(GameInventoryItem item, uint resultItemId) =>
        item.BaseItemId == resultItemId ? (uint)item.Quantity : 0;

    private void TryStopArtisanForCompletedEntry()
    {
        if (this.activeCraftAllEntry is null || this.activeEntryStopRequested)
            return;

        try
        {
            this.artisanSetEnduranceStatus.InvokeAction(false);
            this.activeEntryStopRequested = true;
            this.fileLog.Info(
                "Artisan",
                $"Requested Artisan stop after recipe {this.activeCraftAllEntry.RecipeId} reached its planned craft count.");
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Artisan", $"Could not request Artisan stop after recipe completion: {exception.Message}");
        }
    }

    private void UpdateActiveEntryBusyTime(bool artisanBusy)
    {
        if (artisanBusy)
        {
            if (this.activeEntryBusySince == DateTime.MinValue)
                this.activeEntryBusySince = DateTime.UtcNow;
            return;
        }

        if (this.activeEntryBusySince == DateTime.MinValue)
            return;

        this.activeEntryBusyElapsed += DateTime.UtcNow - this.activeEntryBusySince;
        this.activeEntryBusySince = DateTime.MinValue;
    }

    private uint GetRemainingCraftCount(ArtisanCraftQueueEntry entry) =>
        this.activeCraftAllEntry is not null && this.activeCraftAllEntry.RecipeId == entry.RecipeId
            ? Math.Max(0u, entry.CraftCount - this.activeEntryCompletedCrafts)
            : entry.CraftCount;

    private void CompleteActiveCraftAllEntry()
    {
        if (this.activeCraftAllEntry is not { } activeEntry)
            return;

        this.completedCraftAllEntries.Add(activeEntry);
        this.activeCraftAllEntry = null;
        this.craftAllEntryStarted = false;
        this.activeEntryBusySince = DateTime.MinValue;
        this.activeEntryBusyElapsed = TimeSpan.Zero;
        this.activeEntryCompletedCrafts = 0;
        this.activeEntryCompletedCraftsAtDispatchStart = 0;
        this.activeEntryResultQuantityAtDispatchStart = 0;
        this.activeEntryStopRequested = false;
        this.stopAfterCurrentCraftRequested = false;
        this.craftAllPausedForRetainerRefill = false;
        if (this.craftAllQueue.Count != 0)
            return;

        this.craftAllActive = false;
        this.dreamRetainerRefillEnabled = false;
        this.CraftAllCompletionCount++;
        this.craftAllFinishedAt = DateTime.UtcNow;
        this.TryCloseCraftingWindows();
        this.fileLog.Info("Artisan", "Craft All queue completed.");
    }

    private void StopCraftAllQueue(bool includeActiveEntryAsCompleted)
    {
        if (includeActiveEntryAsCompleted && this.activeCraftAllEntry is { } activeEntry)
            this.completedCraftAllEntries.Add(activeEntry);

        this.craftAllQueue.Clear();
        this.activeCraftAllEntry = null;
        this.craftAllActive = false;
        this.craftAllEntryStarted = false;
        this.craftAllPausedForAutoRetainer = false;
        this.craftAllPausedForRetainerRefill = false;
        this.craftAllFinishedAt = DateTime.UtcNow;
        this.activeEntryBusySince = DateTime.MinValue;
        this.activeEntryBusyElapsed = TimeSpan.Zero;
        this.activeEntryCompletedCrafts = 0;
        this.activeEntryCompletedCraftsAtDispatchStart = 0;
        this.activeEntryResultQuantityAtDispatchStart = 0;
        this.activeEntryStopRequested = false;
        this.stopAfterCurrentCraftRequested = false;
        this.autoRetainerReleasedAt = DateTime.MinValue;
        this.dreamRetainerRefillEnabled = false;
        this.CraftAllStopCount++;
        this.TryCloseCraftingWindows();
        this.fileLog.Info("Artisan", "Stopped Craft All queue after the current craft.");
    }

    private void AbortCraftAllQueue()
    {
        this.craftAllQueue.Clear();
        this.activeCraftAllEntry = null;
        this.craftAllActive = false;
        this.craftAllEntryStarted = false;
        this.craftAllPausedForAutoRetainer = false;
        this.craftAllPausedForRetainerRefill = false;
        this.craftAllFinishedAt = DateTime.UtcNow;
        this.activeEntryBusySince = DateTime.MinValue;
        this.activeEntryBusyElapsed = TimeSpan.Zero;
        this.activeEntryCompletedCrafts = 0;
        this.activeEntryCompletedCraftsAtDispatchStart = 0;
        this.activeEntryResultQuantityAtDispatchStart = 0;
        this.activeEntryStopRequested = false;
        this.stopAfterCurrentCraftRequested = false;
        this.autoRetainerReleasedAt = DateTime.MinValue;
        this.dreamRetainerRefillEnabled = false;
        this.TryCloseCraftingWindows();
    }

    private bool TryHandleDreamRetainerRefillPause()
    {
        if (!this.craftAllPausedForRetainerRefill || this.gwenDreamService is null)
            return false;

        if (this.gwenDreamService.IsActive)
        {
            this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
            return true;
        }

        if (this.observedDreamCompletionSequence == this.gwenDreamService.CompletionSequence)
        {
            this.nextCraftAllCheck = DateTime.UtcNow.AddMilliseconds(500);
            return true;
        }

        this.observedDreamCompletionSequence = this.gwenDreamService.CompletionSequence;
        if (!this.gwenDreamService.LastRunSucceeded)
        {
            this.fileLog.Warning("Artisan", "Crystal top-up failed. Stopping Craft All.");
            this.AbortCraftAllQueue();
            return true;
        }

        this.craftAllPausedForRetainerRefill = false;
        this.fileLog.Info("Artisan", "Crystal top-up completed. Resuming Craft All.");
        this.nextCraftAllCheck = DateTime.UtcNow;
        return false;
    }

    private void TryCloseCraftingWindows()
    {
        var closedAnyWindow =
            this.TryCloseAddon("RecipeNote") |
            this.TryCloseAddon("WKSRecipeNotebook") |
            this.TryCloseAddon("RecipeTree") |
            this.TryCloseAddon("RecipeMaterialList");
        if (closedAnyWindow)
            this.fileLog.Info("Artisan", "Closed the in-game crafting windows after Artisan stopped.");
    }

    private bool TryCloseAddon(string addonName)
    {
        try
        {
            var addon = this.gameGui.GetAddonByName(addonName, 1);
            if (addon.Address == IntPtr.Zero)
                return false;

            var unitBase = (AtkUnitBase*)addon.Address;
            if (!unitBase->IsVisible)
                return false;

            unitBase->Close(true);
            return true;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Artisan", $"Could not close {addonName}: {exception.Message}");
            return false;
        }
    }

    private bool IsAutoRetainerBusy()
    {
        try
        {
            if (this.autoRetainerIsBusy.InvokeFunc())
                return true;
        }
        catch
        {
            // Fall back to reflection for older or partially-loaded AutoRetainer builds.
        }

        return this.TryIsAutoRetainerBusyViaReflection();
    }

    private bool TryIsAutoRetainerBusyViaReflection()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    "AutoRetainer",
                    StringComparison.OrdinalIgnoreCase));
            if (assembly is null)
                return false;

            var pluginType = assembly.GetType("AutoRetainer.AutoRetainer", false);
            var pluginField = pluginType?.GetField("P", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var pluginInstance = pluginField?.GetValue(null);
            var taskManagerInstance = pluginType?.GetField("TaskManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(pluginInstance);
            var taskManagerBusy = taskManagerInstance?.GetType().GetProperty("IsBusy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(taskManagerInstance);
            if (taskManagerBusy is bool { } taskQueueBusy && taskQueueBusy)
                return true;

            var schedulerType = assembly.GetType("AutoRetainer.Scheduler.SchedulerMain", false);
            var retainerPostProcessLocked = schedulerType?.GetField("RetainerPostProcessLocked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            if (retainerPostProcessLocked is bool { } retainerLocked && retainerLocked)
                return true;

            var characterPostProcessLocked = schedulerType?.GetField("CharacterPostProcessLocked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            return characterPostProcessLocked is bool { } characterLocked && characterLocked;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Artisan", $"Could not inspect AutoRetainer state: {exception.Message}");
            return false;
        }
    }
}
