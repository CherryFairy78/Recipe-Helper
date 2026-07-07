using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DalamudRecipeHelper;

public sealed unsafe class GwenDreamService : IDisposable
{
    private static readonly TimeSpan FinalCloseQuietPeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultStepTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan RetainerSelectionTimeout = TimeSpan.FromSeconds(30);
    private const int RetainerSelectDelayMs = 600;
    private const int EntrustSelectDelayMs = 550;
    private const int QuantityFlowOpenDelayMs = 325;
    private const int WithdrawRequestDelayMs = 375;
    private const int QuantityConfirmDelayMs = 325;

    private static readonly InventoryType[] RetainerInventories =
    [
        InventoryType.RetainerPage1,
        InventoryType.RetainerPage2,
        InventoryType.RetainerPage3,
        InventoryType.RetainerPage4,
        InventoryType.RetainerPage5,
        InventoryType.RetainerPage6,
        InventoryType.RetainerPage7,
        InventoryType.RetainerCrystals,
    ];

    private readonly FileLogService fileLog;
    private readonly RecipeService recipeService;
    private readonly InventoryService inventoryService;
    private readonly ICommandManager commandManager;
    private readonly IFramework framework;
    private readonly IGameGui gameGui;
    private readonly ITargetManager targetManager;
    private readonly IObjectTable objectTable;
    private readonly ICallGateSubscriber<bool> autoRetainerIsBusy;
    private List<DreamTarget> pendingTargets = [];
    private int activeTargetIndex;
    private DreamTarget? activeTarget;
    private DreamStep step;
    private DateTime stepStartedAt;
    private DateTime nextUiAdvanceAt;
    private DateTime lastCloseDebugAt;
    private DateTime nextRetainerListDiagnosticAt;
    private int retainerSelectAttempt;
    private uint initialLiveQuantity;
    private bool withdrawIssued;
    private uint completionSequence;

    public GwenDreamService(
        FileLogService fileLog,
        RecipeService recipeService,
        InventoryService inventoryService,
        ICommandManager commandManager,
        IFramework framework,
        IGameGui gameGui,
        ITargetManager targetManager,
        IObjectTable objectTable)
    {
        this.fileLog = fileLog;
        this.recipeService = recipeService;
        this.inventoryService = inventoryService;
        this.commandManager = commandManager;
        this.framework = framework;
        this.gameGui = gameGui;
        this.targetManager = targetManager;
        this.objectTable = objectTable;
        this.autoRetainerIsBusy = Plugin.PluginInterface.GetIpcSubscriber<bool>("AutoRetainer.PluginState.IsBusy");
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public bool IsActive => this.step is not DreamStep.Idle and not DreamStep.Completed and not DreamStep.Failed;

    public bool IsAutoRetainerAvailable =>
        AppDomain.CurrentDomain
            .GetAssemblies()
            .Any(candidate => string.Equals(
                candidate.GetName().Name,
                "AutoRetainer",
                StringComparison.OrdinalIgnoreCase));

    public uint CompletionSequence => this.completionSequence;

    public bool LastRunSucceeded { get; private set; }

    public string StatusMessage { get; private set; } = string.Empty;

    public bool StatusIsError { get; private set; }

    public bool CanUseForSelection(RecipePlanDetails? details)
    {
        if (details is null)
            return false;

        return this.FindDreamTargets(details, out _, logErrors: false).Count > 0;
    }

    public DreamDebugSnapshot GetDebugSnapshot()
    {
        var openRetainerName = this.TryGetOpenRetainerName(out var currentRetainerName)
            ? currentRetainerName
            : string.Empty;
        var activeTargetSummary = this.activeTarget is null
            ? string.Empty
            : $"{this.activeTarget.RetainerName} -> {this.activeTarget.ItemName} x{this.activeTarget.WithdrawQuantity:N0}";

        return new DreamDebugSnapshot(
            this.step.ToString(),
            this.IsActive,
            this.IsAutoRetainerAvailable,
            this.IsAutoRetainerBusy(),
            this.LastRunSucceeded,
            this.StatusIsError,
            this.StatusMessage,
            openRetainerName,
            this.pendingTargets.Count,
            this.activeTargetIndex,
            activeTargetSummary,
            this.retainerSelectAttempt,
            this.withdrawIssued,
            this.step is DreamStep.Idle ? TimeSpan.Zero : DateTime.UtcNow - this.stepStartedAt,
            this.completionSequence,
            this.DescribeVisibleRetainerAddons());
    }

    public void Dispose() => this.framework.Update -= this.OnFrameworkUpdate;

    public bool TryStart(RecipePlanDetails? details)
    {
        this.LastRunSucceeded = false;
        if (details is null)
            return this.Fail("Select at least one recipe first.");

        var targets = this.FindDreamTargets(details, out var error);
        if (!string.IsNullOrWhiteSpace(error))
            return this.Fail(error);

        if (targets.Count == 0)
            return this.Fail("No retainer material is needed for the current recipe selection.");

        if (this.TryGetAutoRetainerBusyMessage(out var busyMessage))
            return this.Fail(busyMessage);

        if (!this.EnsureSummoningBellTargeted())
            return this.Fail("Stand near a summoning bell and try Gwen's Dream again.");

        this.pendingTargets = targets;
        this.activeTargetIndex = 0;
        this.SetActiveTarget(0);
        this.MoveToStep(DreamStep.WaitingForRetainerList, this.BuildStartMessage());

        if (!this.TryInteractWithTargetedBell())
            return this.Fail("Could not interact with the summoning bell.");

        this.fileLog.Info(
            "Dream",
            $"Started dream flow with {targets.Count} target(s). First target: {this.activeTarget!.RetainerName} -> {this.activeTarget.WithdrawQuantity} {this.activeTarget.ItemName}.");
        return true;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (this.activeTarget is null || this.step is DreamStep.Idle or DreamStep.Completed or DreamStep.Failed)
            return;

        if (this.TryFailIfAutoRetainerTookOver())
            return;

        switch (this.step)
        {
            case DreamStep.WaitingForRetainerList:
                if (this.TryHandleOpenRetainerBeforeList())
                    return;

                if (this.IsAddonVisible("RetainerList"))
                {
                    if (this.TrySelectRetainerFromList(this.activeTarget.RetainerName))
                    {
                        this.MoveToStep(
                            DreamStep.WaitingForRetainerSelection,
                            $"Selecting {this.activeTarget.RetainerName} from the retainer list.");
                        return;
                    }

                    this.TryLogRetainerListSelectionState(this.activeTarget.RetainerName);
                    this.MoveToStep(
                        DreamStep.WaitingForRetainerSelection,
                        $"Retainer list opened for {this.activeTarget.RetainerName}. Waiting for the retainer entry to become selectable.");
                    return;
                }

                if (!this.HasRetainerUiSignal() &&
                    DateTime.UtcNow >= this.nextUiAdvanceAt &&
                    this.TryInteractWithTargetedBell())
                {
                    this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(1000);
                    this.UpdateStatus(this.BuildStartMessage(), false);
                    return;
                }

                this.FailIfTimedOut("Timed out waiting for the retainer list.");
                return;

            case DreamStep.WaitingForRetainerSelection:
                if (this.IsAddonVisible("RetainerList"))
                {
                    if (this.TrySelectRetainerFromList(this.activeTarget.RetainerName))
                    {
                        this.UpdateStatus(
                            $"Selecting {this.activeTarget.RetainerName} from the retainer list.",
                            false);
                        return;
                    }

                    this.TryLogRetainerListSelectionState(this.activeTarget.RetainerName);
                    this.UpdateStatus(
                        $"Retainer list opened for {this.activeTarget.RetainerName}. Waiting for the retainer entry to become selectable.",
                        false);
                    return;
                }

                if (this.TryGetOpenRetainerName(out var openRetainerName) &&
                    openRetainerName.Equals(this.activeTarget.RetainerName, StringComparison.OrdinalIgnoreCase) &&
                    this.HasRetainerUiSignal())
                {
                    this.MoveToStep(
                        DreamStep.WaitingForPromptOrMenu,
                        $"Detected open retainer {this.activeTarget.RetainerName}. Waiting for the prompt or menu.");
                    return;
                }

                if (!this.IsAddonVisible("RetainerList"))
                {
                    this.MoveToStep(
                        DreamStep.WaitingForPromptOrMenu,
                        $"Retainer list closed. Waiting for {this.activeTarget.RetainerName} to continue opening.");
                }

                this.FailIfTimedOut("Timed out waiting for the retainer to be selected.");
                return;

            case DreamStep.WaitingForPromptOrMenu:
                if (this.IsAddonVisible("Talk"))
                {
                    if (this.TryAdvanceTalkPrompt())
                    {
                        this.UpdateStatus(
                            $"Talk prompt advanced for {this.activeTarget.RetainerName}. Waiting for the retainer menu.",
                            false);
                        return;
                    }

                    this.MoveToStep(
                        DreamStep.WaitingForEntrustMenu,
                        $"Talk prompt detected for {this.activeTarget.RetainerName}. Click it once, then Dream will continue.");
                    return;
                }

                if (this.IsAddonVisible("Dialogue"))
                {
                    if (this.TryAdvanceDialoguePrompt())
                    {
                        this.UpdateStatus(
                            $"Dialogue prompt advanced for {this.activeTarget.RetainerName}. Waiting for the retainer menu.",
                            false);
                        return;
                    }
                }

                if (this.IsAddonVisible("SelectString"))
                {
                    this.MoveToStep(
                        DreamStep.WaitingForEntrustMenu,
                        $"Retainer menu detected for {this.activeTarget.RetainerName}. Choose 'Entrust or withdraw items.'");
                    return;
                }

                if (this.TryGetOpenRetainerName(out openRetainerName) &&
                    openRetainerName.Equals(this.activeTarget.RetainerName, StringComparison.OrdinalIgnoreCase) &&
                    this.IsRetainerInventoryOpen())
                {
                    this.MoveToStep(
                        DreamStep.WaitingForWithdraw,
                        $"Retainer inventory opened for {this.activeTarget.RetainerName}. Withdrawing {this.activeTarget.WithdrawQuantity:N0} {this.activeTarget.ItemName}.");
                    return;
                }

                this.FailIfTimedOut("Timed out waiting for the retainer prompt.");
                return;

            case DreamStep.WaitingForEntrustMenu:
                if (this.IsRetainerInventoryOpen())
                {
                    this.MoveToStep(
                        DreamStep.WaitingForWithdraw,
                        $"Retainer inventory opened for {this.activeTarget.RetainerName}. Withdrawing {this.activeTarget.WithdrawQuantity:N0} {this.activeTarget.ItemName}.");
                    return;
                }

                if (this.IsAddonVisible("SelectString"))
                {
                    if (this.TrySelectEntrustItemsViaAutoRetainer())
                    {
                        this.UpdateStatus(
                            $"Selecting 'Entrust or withdraw items' for {this.activeTarget.RetainerName}.",
                            false);
                        this.stepStartedAt = DateTime.UtcNow;
                        return;
                    }

                    this.UpdateStatus(
                        $"Retainer menu detected for {this.activeTarget.RetainerName}. Choose 'Entrust or withdraw items.'",
                        false);
                    this.stepStartedAt = DateTime.UtcNow;
                    return;
                }

                if (this.IsAddonVisible("Talk"))
                {
                    if (this.TryAdvanceTalkPrompt())
                    {
                        this.UpdateStatus(
                            $"Talk prompt advanced for {this.activeTarget.RetainerName}. Waiting for the retainer menu.",
                            false);
                        return;
                    }

                    this.UpdateStatus(
                        $"Talk prompt detected for {this.activeTarget.RetainerName}. Click it once, then choose 'Entrust or withdraw items.'",
                        false);
                    this.stepStartedAt = DateTime.UtcNow;
                    return;
                }

                if (this.IsAddonVisible("Dialogue") &&
                    this.TryAdvanceDialoguePrompt())
                {
                    this.UpdateStatus(
                        $"Dialogue prompt advanced for {this.activeTarget.RetainerName}. Waiting for the retainer menu.",
                        false);
                    return;
                }

                this.FailIfTimedOut("Timed out waiting for the retainer inventory.");
                return;

            case DreamStep.WaitingForWithdraw:
                if (this.HasWithdrawCompleted())
                {
                    if (this.TryAdvanceToNextTarget())
                        return;

                    this.MoveToStep(
                        DreamStep.WaitingForFinalInventoryClose,
                        $"Withdrew all {this.pendingTargets.Count:N0} Dream target(s). Closing the retainer inventory.");
                    return;
                }

                if (this.IsAddonVisible("InputNumeric"))
                {
                    if (this.TryConfirmWithdrawQuantity(this.activeTarget.WithdrawQuantity))
                    {
                        this.UpdateStatus(
                            $"Confirming withdraw quantity for {this.activeTarget.ItemName}.",
                            false);
                        return;
                    }
                }

                if (this.IsRetainerInventoryOpen())
                {
                    if (!this.withdrawIssued && this.TryWithdrawTargetItem(this.activeTarget))
                    {
                        this.withdrawIssued = true;
                        this.stepStartedAt = DateTime.UtcNow;
                        this.UpdateStatus(
                            $"Selecting {this.activeTarget.ItemName} in {this.activeTarget.RetainerName}'s inventory.",
                            false);
                        return;
                    }

                    this.UpdateStatus(
                        $"Waiting for {this.activeTarget.ItemName} to finish withdrawing from {this.activeTarget.RetainerName}.",
                        false);
                }

                this.FailIfTimedOut($"Timed out withdrawing {this.activeTarget.ItemName} from {this.activeTarget.RetainerName}.");
                return;

            case DreamStep.WaitingForRetainerSwitch:
                if (this.IsAddonVisible("Talk"))
                {
                    if (this.TryAdvanceTalkPrompt())
                    {
                        this.UpdateStatus(
                            $"Closing {this.pendingTargets[this.activeTargetIndex - 1].RetainerName} and switching to {this.activeTarget.RetainerName}.",
                            false);
                        return;
                    }
                }

                if (this.IsAddonVisible("Dialogue") &&
                    this.TryAdvanceDialoguePrompt())
                {
                    this.UpdateStatus(
                        $"Closing {this.pendingTargets[this.activeTargetIndex - 1].RetainerName} and switching to {this.activeTarget.RetainerName}.",
                        false);
                    return;
                }

                if (this.IsAddonVisible("RetainerList"))
                {
                    this.MoveToStep(
                        DreamStep.WaitingForRetainerSelection,
                        $"Selecting {this.activeTarget.RetainerName} from the retainer list.");
                    return;
                }

                if (DateTime.UtcNow >= this.nextUiAdvanceAt)
                {
                    if (this.TryCloseRetainer())
                    {
                        this.UpdateStatus(
                            $"Closing {this.pendingTargets[this.activeTargetIndex - 1].RetainerName} and switching to {this.activeTarget.RetainerName}.",
                            false);
                        return;
                    }
                }

                this.FailIfTimedOut($"Timed out switching from {this.pendingTargets[this.activeTargetIndex - 1].RetainerName} to {this.activeTarget.RetainerName}.");
                return;

            case DreamStep.WaitingForFinalInventoryClose:
                this.LogCloseStateIfNeeded("final-inventory-close");

                if (this.IsAddonVisible("SelectString"))
                {
                    this.MoveToStep(
                        DreamStep.WaitingForFinalMenuClose,
                        "Retainer menu detected. Finishing the retainer exit.");
                    return;
                }

                if (this.IsAddonVisible("Talk") || this.IsAddonVisible("Dialogue"))
                {
                    this.MoveToStep(
                        DreamStep.WaitingForFinalPromptClose,
                        "Retainer prompt detected. Clearing the retainer prompt.");
                    return;
                }

                if (!this.HasBlockingRetainerInventoryUi() &&
                    !this.HasVisibleRetainerGrid() &&
                    !this.IsAddonVisible("RetainerItemTransferList"))
                {
                    if (this.IsActualRetainerListVisible())
                    {
                        this.MoveToStep(
                            DreamStep.WaitingForFinalListClose,
                            "Retainer inventory closed. Closing the retainer list.");
                        return;
                    }

                    if (this.IsRetainerWindowVisible())
                    {
                        this.MoveToStep(
                            DreamStep.WaitingForFinalMenuClose,
                            "Retainer inventory closed. Finishing the retainer exit.");
                        return;
                    }

                    this.MoveToStep(
                        DreamStep.WaitingForFinalMenuClose,
                        "Retainer inventory closed. Finishing the retainer exit.");
                    return;
                }

                if (DateTime.UtcNow >= this.nextUiAdvanceAt)
                {
                    if (this.TryCloseRetainerInventoryShell())
                    {
                        this.UpdateStatus("Closing the retainer inventory.", false);
                        return;
                    }
                }

                this.FailIfTimedOut($"Timed out closing the retainer inventory. Visible: {this.DescribeVisibleRetainerAddons()}");
                return;

            case DreamStep.WaitingForFinalMenuClose:
                this.LogCloseStateIfNeeded("final-menu-close");

                if (this.IsActualRetainerListVisible())
                {
                    this.MoveToStep(
                        DreamStep.WaitingForFinalListClose,
                        "Retainer closed. Closing the retainer list.");
                    return;
                }

                if (!this.HasRetainerUiSignal() &&
                    !this.HasAnyRetainerAddonVisible() &&
                    !this.IsRetainerAgentActive() &&
                    this.HasFinalCloseQuietPeriodElapsed())
                {
                    this.MoveToStep(
                        DreamStep.Completed,
                        $"Withdrew all {this.pendingTargets.Count:N0} Dream target(s) and closed the retainer.");
                    return;
                }

                if (this.IsAddonVisible("SelectString"))
                {
                    if (this.TrySelectQuitViaAutoRetainer())
                    {
                        this.MoveToStep(
                            DreamStep.WaitingForFinalPromptClose,
                            "Selected 'Quit'. Clearing the retainer prompt.");
                        return;
                    }
                }

                if (this.IsAddonVisible("Talk"))
                {
                    if (this.TryAdvanceTalkPrompt())
                    {
                        this.UpdateStatus("Closing the retainer.", false);
                        return;
                    }
                }

                if (this.IsAddonVisible("Dialogue") &&
                    this.TryAdvanceDialoguePrompt())
                {
                    this.UpdateStatus("Closing the retainer.", false);
                    return;
                }

                if (DateTime.UtcNow >= this.nextUiAdvanceAt)
                {
                    if (this.TryCloseOpenRetainer())
                    {
                        this.UpdateStatus("Closing the retainer.", false);
                        return;
                    }
                }

                this.FailIfTimedOut($"Timed out closing the retainer. Visible: {this.DescribeVisibleRetainerAddons()}");
                return;

            case DreamStep.WaitingForFinalPromptClose:
                this.LogCloseStateIfNeeded("final-prompt-close");

                if (this.IsActualRetainerListVisible())
                {
                    this.MoveToStep(
                        DreamStep.WaitingForFinalListClose,
                        "Retainer prompt cleared. Closing the retainer list.");
                    return;
                }

                if (this.IsAddonVisible("Talk"))
                {
                    if (this.TryAdvanceTalkPromptFast())
                    {
                        this.UpdateStatus("Clearing the retainer prompt.", false);
                        return;
                    }
                }

                if (this.IsAddonVisible("Dialogue"))
                {
                    if (this.TryAdvanceDialoguePromptFast())
                    {
                        this.UpdateStatus("Clearing the retainer prompt.", false);
                        return;
                    }
                }

                if (this.IsAddonVisible("SelectString"))
                {
                    if (this.TrySelectQuitViaAutoRetainer())
                    {
                        this.UpdateStatus("Selecting 'Quit' from the retainer menu.", false);
                        return;
                    }
                }

                this.FailIfTimedOut($"Timed out clearing the retainer prompt. Visible: {this.DescribeVisibleRetainerAddons()}");
                return;

            case DreamStep.WaitingForFinalListClose:
                this.LogCloseStateIfNeeded("final-list-close");

                if (!this.IsActualRetainerListVisible() &&
                    !this.HasRetainerUiSignal() &&
                    !this.HasAnyRetainerAddonVisible() &&
                    !this.IsRetainerAgentActive() &&
                    this.HasFinalCloseQuietPeriodElapsed())
                {
                    this.MoveToStep(
                        DreamStep.Completed,
                        $"Withdrew all {this.pendingTargets.Count:N0} Dream target(s) and closed the retainer.");
                    return;
                }

                if (this.IsAddonVisible("Talk"))
                {
                    if (this.TryAdvanceTalkPrompt())
                    {
                        this.UpdateStatus("Closing the retainer list.", false);
                        return;
                    }
                }

                if (this.IsAddonVisible("Dialogue") &&
                    this.TryAdvanceDialoguePrompt())
                {
                    this.UpdateStatus("Closing the retainer list.", false);
                    return;
                }

                if (DateTime.UtcNow >= this.nextUiAdvanceAt)
                {
                    if (this.IsRetainerShellVisible() && this.TryCloseRetainerShellDirect())
                    {
                        this.UpdateStatus("Closing the retainer shell.", false);
                        return;
                    }

                    if (this.TryCloseFinalRetainerList())
                    {
                        this.UpdateStatus("Closing the retainer list.", false);
                        return;
                    }
                }

                this.FailIfTimedOut($"Timed out closing the retainer list. Visible: {this.DescribeVisibleRetainerAddons()}");
                return;
        }
    }

    private List<DreamTarget> FindDreamTargets(RecipePlanDetails details, out string error, bool logErrors = true)
    {
        var storedRetainers = this.inventoryService.GetStoredRetainers();
        if (storedRetainers.Count == 0)
        {
            error = string.Empty;
            return [];
        }

        var liveOwnedItems = this.inventoryService.GetLiveOwnedItems();
        if (!this.recipeService.TryBuildDreamTargets(
                details,
                liveOwnedItems,
                storedRetainers,
                out var plannedTargets,
                out error))
        {
            if (logErrors)
                this.fileLog.Warning("Dream", $"Could not build Dream withdrawal plan: {error}");
            return [];
        }

        error = string.Empty;
        return plannedTargets
            .Select(target => new DreamTarget(
                target.RetainerId,
                target.RetainerName,
                target.ItemId,
                target.ItemName,
                target.WithdrawQuantity,
                target.SnapshotQuantity))
            .ToList();
    }

    private bool EnsureSummoningBellTargeted()
    {
        if (this.IsSummoningBellTargeted(this.targetManager.Target))
            return true;

        var nearestBell = this.objectTable
            .Where(obj => obj is not null && this.IsSummoningBellTarget(obj))
            .FirstOrDefault();
        if (nearestBell is null)
            return false;

        this.targetManager.Target = nearestBell;
        this.fileLog.Info("Dream", $"Auto-targeted summoning bell '{nearestBell.Name.TextValue}'.");
        return true;
    }

    private bool TryInteractWithTargetedBell()
    {
        var target = this.targetManager.Target;
        if (target is null || !this.IsSummoningBellTarget(target))
            return false;

        if (target.Address == nint.Zero)
            return false;

        TargetSystem.Instance()->InteractWithObject((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)target.Address, false);
        this.fileLog.Info("Dream", $"Interacted with summoning bell '{target.Name.TextValue}'.");
        return true;
    }

    private bool TryAdvanceTalkPrompt()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (!this.TryGetVisibleReadyAddon("Talk", out var unitBase))
            return false;

        unitBase->FireCallbackInt(0);
        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(750);
        this.fileLog.Info("Dream", "Advanced Talk prompt.");
        return true;
    }

    private bool TryAdvanceDialoguePrompt()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (!this.TryGetVisibleReadyAddon("Dialogue", out var unitBase))
            return false;

        unitBase->FireCallbackInt(0);
        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(750);
        this.fileLog.Info("Dream", "Advanced Dialogue prompt.");
        return true;
    }

    private bool TryAdvanceTalkPromptFast()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (!this.TryGetVisibleReadyAddon("Talk", out var unitBase))
            return false;

        unitBase->FireCallbackInt(0);
        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(150);
        this.fileLog.Info("Dream", "Advanced Talk prompt (fast).");
        return true;
    }

    private bool TryAdvanceDialoguePromptFast()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (!this.TryGetVisibleReadyAddon("Dialogue", out var unitBase))
            return false;

        unitBase->FireCallbackInt(0);
        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(150);
        this.fileLog.Info("Dream", "Advanced Dialogue prompt (fast).");
        return true;
    }

    private bool TrySelectRetainerFromList(string retainerName)
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (this.TrySelectRetainerViaAutoRetainer(retainerName))
        {
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(1000);
            return true;
        }

        if (!this.TryGetVisibleReadyAddon("RetainerList", out var unitBase))
            return false;

        if (!this.IsRetainerListReadyForSelection(unitBase, retainerName))
            return false;

        var matchIndex = this.FindRetainerListIndex(unitBase, retainerName);
        if (matchIndex is null)
            return false;

        switch (this.retainerSelectAttempt % 3)
        {
            case 0:
                unitBase->FireCallbackInt(matchIndex.Value);
                break;

            case 1:
            {
                var oneValue = stackalloc AtkValue[1]
                {
                    new() { Type = AtkValueType.Int, Int = matchIndex.Value }
                };

                unitBase->FireCallback(1, oneValue);
                break;
            }

            default:
            {
                var twoValues = stackalloc AtkValue[2]
                {
                    new() { Type = AtkValueType.Int, Int = 0 },
                    new() { Type = AtkValueType.UInt, UInt = (uint)matchIndex.Value }
                };

                unitBase->FireCallback(2, twoValues);
                break;
            }
        }

        this.retainerSelectAttempt++;
        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(RetainerSelectDelayMs);
        this.fileLog.Info(
            "Dream",
            $"Attempted retainer-list selection for '{retainerName}' at index {matchIndex.Value} with pattern {this.retainerSelectAttempt}.");
        return true;
    }

    private bool TrySelectRetainerViaAutoRetainer(string retainerName)
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

            var type = assembly.GetType("AutoRetainer.Scheduler.Handlers.RetainerListHandlers", false);
            var method = type?.GetMethod(
                "SelectRetainerByName",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                [typeof(string)]);
            if (method is null)
                return false;

            var result = method.Invoke(null, [retainerName]);
            var succeeded =
                (result is bool booleanResult && booleanResult) ||
                string.Equals(result?.ToString(), "True", StringComparison.OrdinalIgnoreCase);

            if (succeeded)
            {
                this.fileLog.Info("Dream", $"AutoRetainer selected retainer '{retainerName}'.");
                return true;
            }
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"AutoRetainer retainer selection failed: {exception.Message}");
        }

        return false;
    }

    private bool TrySelectEntrustItemsViaAutoRetainer()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

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

            var type = assembly.GetType("AutoRetainer.Scheduler.Handlers.RetainerHandlers", false);
            var method = type?.GetMethod(
                "SelectEntrustItems",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
                return false;

            var result = method.Invoke(null, null);
            var succeeded =
                (result is bool booleanResult && booleanResult) ||
                string.Equals(result?.ToString(), "True", StringComparison.OrdinalIgnoreCase);
            if (!succeeded)
                return false;

            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(EntrustSelectDelayMs);
            this.fileLog.Info("Dream", "AutoRetainer selected 'Entrust or withdraw items'.");
            return true;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"AutoRetainer entrust-item selection failed: {exception.Message}");
            return false;
        }
    }

    private bool TryWithdrawTargetItem(DreamTarget target)
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (!this.TryFindRetainerItemSlot(target.ItemId, out var inventoryType, out var slot, out var slotQuantity, out _))
            return false;

        if (target.WithdrawQuantity < slotQuantity && this.TryOpenRetrieveQuantity(slot, inventoryType, target.ItemName))
        {
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(QuantityFlowOpenDelayMs);
            return true;
        }

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
            if (pluginInstance is null)
                return false;

            var memoryField = pluginType?.GetField("Memory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var memoryInstance = memoryField?.GetValue(pluginInstance);
            if (memoryInstance is null)
                return false;

            var inventorySpaceManagerType = assembly.GetType("AutoRetainer.Internal.InventoryManagement.InventorySpaceManager", false);
            var moduleProperty = inventorySpaceManagerType?.GetProperty(
                "AgentRetainerItemCommandModule",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var moduleAddress = moduleProperty?.GetValue(null);
            if (moduleAddress is not nint agentModule || agentModule == nint.Zero)
                return false;

            var commandType = assembly.GetType("AutoRetainer.Internal.InventoryManagement.RetainerItemCommand", false);
            var command = commandType is null
                ? null
                : Enum.Parse(commandType, "RetrieveFromRetainer");
            if (command is null)
                return false;

            var method = memoryInstance.GetType().GetMethod(
                "RetainerItemCommandDetour",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
                return false;

            method.Invoke(memoryInstance, [agentModule, slot, inventoryType, 0u, command]);
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(WithdrawRequestDelayMs);
            this.fileLog.Info(
                "Dream",
                $"Requested withdraw for item {target.ItemName} from {inventoryType} slot {slot} (stack {slotQuantity}).");
            return true;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"Withdraw automation failed: {exception.Message}");
            return false;
        }
    }

    private bool TryFindRetainerItemSlot(
        uint itemId,
        out InventoryType inventoryType,
        out uint slot,
        out uint quantity,
        out uint listIndex)
    {
        inventoryType = default;
        slot = 0;
        quantity = 0;
        listIndex = 0;

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager is null)
            return false;

        var currentListIndex = 0u;
        foreach (var candidateType in RetainerInventories)
        {
            var container = inventoryManager->GetInventoryContainer(candidateType);
            if (container is null || !container->IsLoaded)
                continue;

            for (var i = 0; i < container->Size; i++)
            {
                var item = container->GetInventorySlot(i);
                if (item is null || item->Quantity == 0)
                    continue;

                var candidateId = item->GetBaseItemId();
                if (candidateId == 0)
                    candidateId = item->GetItemId();
                if (candidateId != itemId)
                {
                    currentListIndex++;
                    continue;
                }

                inventoryType = candidateType;
                slot = (uint)i;
                quantity = (uint)item->Quantity;
                listIndex = currentListIndex;
                return true;
            }
        }

        return false;
    }

    private bool TryOpenRetrieveQuantity(uint slot, InventoryType inventoryType, string itemName)
    {
        var agent = AgentRetainer.Instance();
        if (agent is null)
            return false;

        agent->HandleCallback(slot, inventoryType, 0, 3);
        this.fileLog.Info("Dream", $"Opened retrieve-quantity flow for {itemName} from {inventoryType} slot {slot}.");
        return true;
    }

    private bool TryConfirmWithdrawQuantity(uint quantity)
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (!this.TryGetVisibleReadyAddon("InputNumeric", out var unitBase))
            return false;

        if (unitBase->AtkValuesCount <= 3)
            return false;

        var maxQuantity = unitBase->AtkValuesCount > 3
            ? unitBase->AtkValues[3].UInt
            : quantity;
        var requestedQuantity = (int)Math.Clamp(quantity, 1u, maxQuantity == 0 ? quantity : maxQuantity);
        this.FireIntCallback(unitBase, requestedQuantity, true);

        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(QuantityConfirmDelayMs);
        this.fileLog.Info("Dream", $"Confirmed withdraw quantity {requestedQuantity}.");
        return true;
    }

    private bool HasWithdrawCompleted()
    {
        if (this.activeTarget is null)
            return false;

        var updatedQuantity = this.inventoryService.GetLiveOwnedItems()
            .GetValueOrDefault(this.activeTarget.ItemId)?.Quantity ?? 0;
        return updatedQuantity >= this.initialLiveQuantity + this.activeTarget.WithdrawQuantity;
    }

    private bool TryAdvanceToNextTarget()
    {
        var completedTarget = this.activeTarget;
        if (completedTarget is null)
            return false;

        var nextIndex = this.activeTargetIndex + 1;
        if (nextIndex >= this.pendingTargets.Count)
            return false;

        this.SetActiveTarget(nextIndex);
        if (completedTarget.RetainerId == this.activeTarget!.RetainerId && this.IsRetainerInventoryOpen())
        {
            this.MoveToStep(
                DreamStep.WaitingForWithdraw,
                $"Continuing in {this.activeTarget.RetainerName}: withdraw {this.activeTarget.WithdrawQuantity:N0} {this.activeTarget.ItemName} ({nextIndex + 1}/{this.pendingTargets.Count}).");
            return true;
        }

        this.MoveToStep(
            DreamStep.WaitingForRetainerSwitch,
            $"Closing {completedTarget.RetainerName} and switching to {this.activeTarget.RetainerName}.");
        return true;
    }

    private void SetActiveTarget(int index)
    {
        this.activeTargetIndex = index;
        this.activeTarget = this.pendingTargets[index];
        this.retainerSelectAttempt = 0;
        this.withdrawIssued = false;
        this.initialLiveQuantity = this.inventoryService.GetLiveOwnedItems()
            .GetValueOrDefault(this.activeTarget.ItemId)?.Quantity ?? 0;
    }

    private string BuildStartMessage()
    {
        var target = this.activeTarget!;
        return $"Dream target {this.activeTargetIndex + 1}/{this.pendingTargets.Count}: withdraw {target.WithdrawQuantity:N0} {target.ItemName} from {target.RetainerName}.";
    }

    private bool TryHandleOpenRetainerBeforeList()
    {
        if (this.activeTarget is null)
            return false;

        if (this.IsActualRetainerListVisible())
            return false;

        if (this.TryGetOpenRetainerName(out var openRetainerName) &&
            openRetainerName.Equals(this.activeTarget.RetainerName, StringComparison.OrdinalIgnoreCase) &&
            this.IsRetainerInventoryOpen())
        {
            this.MoveToStep(
                DreamStep.WaitingForWithdraw,
                $"Retainer inventory was already open for {this.activeTarget.RetainerName}. Withdrawing {this.activeTarget.WithdrawQuantity:N0} {this.activeTarget.ItemName}.");
            return true;
        }

        if (this.TryGetOpenRetainerName(out openRetainerName) &&
            openRetainerName.Equals(this.activeTarget.RetainerName, StringComparison.OrdinalIgnoreCase) &&
            (this.IsAddonVisible("Talk") ||
             this.IsAddonVisible("Dialogue") ||
             this.IsAddonVisible("SelectString")))
        {
            this.MoveToStep(
                DreamStep.WaitingForPromptOrMenu,
                $"Retainer {this.activeTarget.RetainerName} was already open. Waiting for the prompt or menu.");
            return true;
        }

        var hasPreOpenRetainerState =
            this.IsRetainerInventoryShellVisible() ||
            this.HasVisibleRetainerGrid() ||
            this.IsRetainerAgentActive() ||
            this.TryGetOpenRetainerName(out _);

        if (hasPreOpenRetainerState &&
            DateTime.UtcNow >= this.nextUiAdvanceAt)
        {
            if (this.TryCloseRetainer())
            {
                this.UpdateStatus("A retainer window was already open. Closing it before starting Gwen's Dream.", false);
                return true;
            }
        }

        if (hasPreOpenRetainerState)
        {
            this.UpdateStatus("A retainer window is already open. Closing it before starting Gwen's Dream.", false);
            return true;
        }

        return false;
    }

    private bool TryCloseRetainer()
    {
        try
        {
            if (this.IsActualRetainerListVisible() && this.TryCloseRetainerListHard())
                return true;

            if (this.TryCloseOpenRetainer())
                return true;
            
            if (this.TryCloseRetainerListHard())
                return true;

            return false;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"Could not close retainer cleanly: {exception.Message}");
            return false;
        }
    }

    private bool TryCloseOpenRetainer()
    {
        try
        {
            if (this.IsAddonVisible("SelectString") && this.TrySelectQuitViaAutoRetainer())
                return true;

            if (this.TryCloseRetainerInventoryShell())
                return true;

            if (this.TryHideRetainerAgent())
                return true;

            var agent = AgentRetainer.Instance();
            if (agent is not null)
            {
                agent->HandleClose();
                this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(300);
                this.fileLog.Info("Dream", "Closed active retainer.");
                return true;
            }

            return false;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"Could not close retainer cleanly: {exception.Message}");
            return false;
        }
    }

    private bool TryCloseRetainerInventoryShell()
    {
        if (this.TryCloseVisibleRetainerAddon())
            return true;

        if (!this.IsAddonVisible("SelectString") &&
            !this.IsAddonVisible("Talk") &&
            !this.IsAddonVisible("Dialogue") &&
            this.TryCloseRetainerShellDirect())
            return true;

        return false;
    }

    private bool TryCloseRetainerShellDirect()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        foreach (var addonName in new[] { "InventoryRetainerLarge", "InventoryRetainer", "Retainer" })
        {
            var addon = this.gameGui.GetAddonByName(addonName, 1);
            if (addon.Address == IntPtr.Zero)
                continue;

            var unitBase = (AtkUnitBase*)addon.Address;
            if (!unitBase->IsVisible)
                continue;

            unitBase->FireCallbackInt(-1);
            unitBase->Close(true);
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(250);
            this.fileLog.Info("Dream", $"Closed retainer shell directly via {addonName}.");
            return true;
        }

        return false;
    }

    private bool TryHideRetainerAgent()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        var uiModule = Framework.Instance()->UIModule;
        if (uiModule is null)
            return false;

        var agent = uiModule->GetAgentModule()->GetAgentByInternalId(AgentId.Retainer);
        if (agent is null || !agent->IsAgentActive())
            return false;

        agent->Hide();
        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(750);
        this.fileLog.Info("Dream", "Hid retainer agent.");
        return true;
    }

    private bool IsRetainerAgentActive()
    {
        var uiModule = Framework.Instance()->UIModule;
        if (uiModule is null)
            return false;

        var agent = uiModule->GetAgentModule()->GetAgentByInternalId(AgentId.Retainer);
        return agent is not null && agent->IsAgentActive();
    }

    private bool HasFinalCloseQuietPeriodElapsed() =>
        DateTime.UtcNow - this.stepStartedAt >= FinalCloseQuietPeriod;

    private bool TrySelectQuitViaAutoRetainer()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt || !this.IsAddonVisible("SelectString"))
            return false;

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

            var type = assembly.GetType("AutoRetainer.Scheduler.Handlers.RetainerHandlers", false);
            var method = type?.GetMethod(
                "SelectQuit",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
                return false;

            var result = method.Invoke(null, null);
            var succeeded =
                result is null ||
                (result is bool booleanResult && booleanResult) ||
                string.Equals(result?.ToString(), "True", StringComparison.OrdinalIgnoreCase);
            if (!succeeded)
                return false;

            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(1000);
            this.fileLog.Info("Dream", "Selected retainer Quit via AutoRetainer.");
            return true;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"AutoRetainer quit selection failed: {exception.Message}");
        }

        if (this.TrySelectSelectStringEntryByText("Quit"))
            return true;

        return this.TrySelectQuitDirect();
    }

    private bool TrySelectSelectStringEntryByText(string targetText)
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        try
        {
            var addon = this.gameGui.GetAddonByName("SelectString", 1);
            if (addon.Address == IntPtr.Zero)
                return false;

            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.GetName().Name,
                    "ECommons",
                    StringComparison.OrdinalIgnoreCase));
            if (assembly is null)
                return false;

            var type = assembly.GetType("ECommons.UIHelpers.AddonMasterImplementations.AddonMaster+SelectString", false);
            if (type is null)
                return false;

            var instance = Activator.CreateInstance(type, addon.Address);
            var entries = type.GetProperty("Entries")?.GetValue(instance) as System.Collections.IEnumerable;
            if (entries is null)
                return false;

            foreach (var entry in entries)
            {
                var text = entry?.GetType().GetProperty("Text")?.GetValue(entry)?.ToString();
                if (!this.MatchesSelectStringEntry(text, targetText))
                    continue;

                var selectMethod = entry!.GetType().GetMethod("Select", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (selectMethod is null)
                    return false;

                selectMethod.Invoke(entry, null);
                this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(300);
                this.fileLog.Info("Dream", $"Selected SelectString entry '{targetText}' via AddonMaster.");
                return true;
            }
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"Direct SelectString text selection failed: {exception.Message}");
        }

        return false;
    }

    private bool TrySelectQuitDirect()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt)
            return false;

        if (!this.TryGetVisibleReadyAddon("SelectString", out var unitBase))
            return false;

        var selectString = (AddonSelectString*)unitBase;
        var entryCount = selectString->PopupMenu.PopupMenu.EntryCount;
        if (entryCount <= 0)
            return false;

        var lastIndex = entryCount - 1;
        selectString->AtkUnitBase.FireCallbackInt(lastIndex);
        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(300);
        this.fileLog.Info("Dream", $"Selected final SelectString entry {lastIndex} directly.");
        return true;
    }

    private bool MatchesSelectStringEntry(string? entryText, string targetText)
    {
        if (string.IsNullOrWhiteSpace(entryText))
            return false;

        var trimmedEntry = entryText.Trim();
        var trimmedTarget = targetText.Trim();
        if (string.Equals(trimmedEntry, trimmedTarget, StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedEntry = trimmedEntry.TrimEnd('.', '!', '?', ':', ';');
        var normalizedTarget = trimmedTarget.TrimEnd('.', '!', '?', ':', ';');
        return string.Equals(normalizedEntry, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
               normalizedEntry.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryCloseVisibleRetainerAddon()
    {
        foreach (var addonName in new[]
                 {
                     "InventoryRetainerLarge",
                     "InventoryRetainer",
                     "RetainerItemTransferList",
                     "Retainer",
                 })
        {
            var addon = this.gameGui.GetAddonByName(addonName, 1);
            if (addon.Address == IntPtr.Zero)
                continue;

            var unitBase = (AtkUnitBase*)addon.Address;
            if (!unitBase->IsVisible || !unitBase->IsReady)
                continue;

            unitBase->Close(true);
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(150);
            this.fileLog.Info("Dream", $"Closed {addonName}.");
            return true;
        }

        foreach (var addonName in new[]
                 {
                     "InputNumeric",
                     "RetainerGrid0",
                     "RetainerGrid1",
                     "RetainerGrid2",
                     "RetainerGrid3",
                     "RetainerGrid4",
                     "RetainerCrystalGrid",
                 })
        {
            var addon = this.gameGui.GetAddonByName(addonName, 1);
            if (addon.Address == IntPtr.Zero)
                continue;

            var unitBase = (AtkUnitBase*)addon.Address;
            if (!unitBase->IsVisible || !unitBase->IsReady)
                continue;

            unitBase->Close(true);
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(750);
            this.fileLog.Info("Dream", $"Closed {addonName}.");
            return true;
        }

        return false;
    }

    private bool TryCloseRetainerList()
    {
        if (this.TryCloseRetainerListViaAutoRetainer())
            return true;

        if (!this.TryGetVisibleReadyAddon("RetainerList", out var unitBase))
            return false;

        unitBase->FireCallbackInt(-1);

        var values = stackalloc AtkValue[1]
        {
            new()
            {
                Type = AtkValueType.Int,
                Int = -1,
            }
        };

        unitBase->FireCallback(1, values);

        this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(250);
        this.fileLog.Info("Dream", "Requested retainer list close.");
        return true;
    }

    private bool TryCloseRetainerListHard()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt || !this.IsActualRetainerListVisible())
            return false;

        if (this.TryCloseRetainerList())
            return true;

        foreach (var addonName in new[] { "RetainerList", "Retainer" })
        {
            var addon = this.gameGui.GetAddonByName(addonName, 1);
            if (addon.Address == IntPtr.Zero)
                continue;

            var unitBase = (AtkUnitBase*)addon.Address;
            if (!unitBase->IsVisible || !unitBase->IsReady)
                continue;

            unitBase->Close(true);
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(250);
            this.fileLog.Info("Dream", $"Closed {addonName} directly.");
            return true;
        }

        return false;
    }

    private bool TryCloseFinalRetainerList()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt || !this.IsActualRetainerListVisible())
            return false;

        if (this.TryCloseRetainerListHard())
            return true;

        if (this.TryHideRetainerAgent())
        {
            this.fileLog.Info("Dream", "Hid retainer agent while closing final retainer list.");
            return true;
        }

        var agent = AgentRetainer.Instance();
        if (agent is not null)
        {
            agent->HandleClose();
            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(250);
            this.fileLog.Info("Dream", "Requested final retainer-list close via AgentRetainer.");
            return true;
        }

        var addon = this.gameGui.GetAddonByName("RetainerList", 1);
        if (addon.Address != IntPtr.Zero)
        {
            var unitBase = (AtkUnitBase*)addon.Address;
            if (unitBase->IsVisible && unitBase->IsReady)
            {
                unitBase->FireCallbackInt(-1);
                unitBase->Close(true);
                this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(250);
                this.fileLog.Info("Dream", "Closed final retainer list directly.");
                return true;
            }
        }

        return false;
    }

    private bool TryCloseRetainerListViaAutoRetainer()
    {
        if (DateTime.UtcNow < this.nextUiAdvanceAt || !this.IsAddonVisible("RetainerList"))
            return false;

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

            var type = assembly.GetType("AutoRetainer.Scheduler.Handlers.RetainerListHandlers", false);
            var method = type?.GetMethod(
                "CloseRetainerList",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
                return false;

            var result = method.Invoke(null, null);
            var succeeded =
                result is null ||
                (result is bool booleanResult && booleanResult) ||
                string.Equals(result?.ToString(), "True", StringComparison.OrdinalIgnoreCase);
            if (!succeeded)
                return false;

            this.nextUiAdvanceAt = DateTime.UtcNow.AddMilliseconds(250);
            this.fileLog.Info("Dream", "Closed retainer list via AutoRetainer.");
            return true;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"AutoRetainer retainer-list close failed: {exception.Message}");
            return false;
        }
    }

    private int? FindRetainerListIndex(AtkUnitBase* unitBase, string retainerName)
    {
        var normalizedTarget = NormalizeRetainerListText(retainerName);
        if (normalizedTarget.Length == 0)
            return null;

        var agentEntries = this.GetAgentRetainerListEntries();
        var agentMatch = agentEntries.FirstOrDefault(
            entry => IsRetainerListNameMatch(entry.Name, normalizedTarget));
        if (!string.IsNullOrWhiteSpace(agentMatch.Name))
            return agentMatch.Index;

        var knownRetainerNames = this.inventoryService.GetStoredRetainers()
            .Select(retainer => NormalizeRetainerListText(retainer.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        knownRetainerNames.Add(normalizedTarget);

        var currentIndex = 0;
        for (var i = 0; i < unitBase->UldManager.NodeListCount; i++)
        {
            var node = unitBase->UldManager.NodeList[i];
            if (node is null || node->Type != NodeType.Text)
                continue;

            var textNode = (AtkTextNode*)node;
            var text = NormalizeRetainerListText(textNode->NodeText.ToString());
            if (!knownRetainerNames.Contains(text))
                continue;

            if (IsRetainerListNameMatch(text, normalizedTarget))
                return currentIndex;

            currentIndex++;
        }

        return null;
    }

    private bool IsRetainerListReadyForSelection(string retainerName)
    {
        var addon = this.gameGui.GetAddonByName("RetainerList", 1);
        if (addon.Address == IntPtr.Zero)
            return false;

        var unitBase = (AtkUnitBase*)addon.Address;
        return unitBase->IsVisible && this.IsRetainerListReadyForSelection(unitBase, retainerName);
    }

    private bool IsRetainerListReadyForSelection(AtkUnitBase* unitBase, string retainerName)
    {
        if (unitBase is null || !unitBase->IsVisible)
            return false;

        if (unitBase->UldManager.NodeListCount == 0)
            return false;

        return this.FindRetainerListIndex(unitBase, retainerName) is not null;
    }

    private void TryLogRetainerListSelectionState(string retainerName)
    {
        if (DateTime.UtcNow < this.nextRetainerListDiagnosticAt)
            return;

        this.nextRetainerListDiagnosticAt = DateTime.UtcNow.AddSeconds(2);
        var addon = this.gameGui.GetAddonByName("RetainerList", 1);
        if (addon.Address == IntPtr.Zero)
            return;

        var unitBase = (AtkUnitBase*)addon.Address;
        if (!unitBase->IsVisible)
            return;

        var normalizedTarget = NormalizeRetainerListText(retainerName);
        var visibleEntries = this.GetVisibleRetainerListEntries(unitBase);
        var entrySummary = visibleEntries.Count == 0
            ? "none"
            : string.Join(" | ", visibleEntries.Select(entry => $"[{entry.Index}] {entry.Name}"));
        this.fileLog.Info(
            "Dream",
            $"Retainer list visible but '{normalizedTarget}' is not selectable yet. Visible entries: {entrySummary}");
    }

    private List<(int Index, string Name)> GetVisibleRetainerListEntries(AtkUnitBase* unitBase)
    {
        var agentEntries = this.GetAgentRetainerListEntries();
        if (agentEntries.Count > 0)
            return agentEntries;

        var entries = new List<(int Index, string Name)>();
        var knownRetainerNames = this.inventoryService.GetStoredRetainers()
            .Select(retainer => NormalizeRetainerListText(retainer.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var currentIndex = 0;
        for (var i = 0; i < unitBase->UldManager.NodeListCount; i++)
        {
            var node = unitBase->UldManager.NodeList[i];
            if (node is null || node->Type != NodeType.Text)
                continue;

            var textNode = (AtkTextNode*)node;
            var text = NormalizeRetainerListText(textNode->NodeText.ToString());
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (knownRetainerNames.Count > 0)
            {
                if (!knownRetainerNames.Contains(text))
                    continue;
            }
            else if (!LooksLikeRetainerListEntry(text))
            {
                continue;
            }

            if (entries.Any(entry => entry.Name.Equals(text, StringComparison.OrdinalIgnoreCase)))
                continue;

            entries.Add((currentIndex, text));
            currentIndex++;
        }

        return entries;
    }

    private List<(int Index, string Name)> GetAgentRetainerListEntries()
    {
        var uiModule = Framework.Instance()->UIModule;
        if (uiModule is null)
            return [];

        var agent = uiModule->GetAgentModule()->GetAgentByInternalId(AgentId.RetainerList);
        if (agent is null || !agent->IsAgentActive())
            return [];

        var agentList = (AgentRetainerList*)agent;
        var retainerManager = RetainerManager.Instance();
        if (retainerManager is null)
            return [];

        var entries = new List<(int Index, string Name)>();
        var count = Math.Min(agentList->RetainerCount, (byte)10);
        for (var i = 0; i < count; i++)
        {
            var entryAddress = (byte*)agentList + 0x50 + (i * 0x70);
            var sortedIndex = *(byte*)(entryAddress + 0x6D);
            var retainer = retainerManager->GetRetainerBySortedIndex(sortedIndex);
            if (retainer is null || retainer->RetainerId == 0)
                continue;

            var name = NormalizeRetainerListText(retainer->NameString);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (entries.Any(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                continue;

            entries.Add((i, name));
        }

        return entries;
    }

    private static bool IsRetainerListNameMatch(string visibleName, string targetName)
    {
        if (visibleName.Equals(targetName, StringComparison.OrdinalIgnoreCase))
            return true;

        return visibleName.StartsWith(targetName + " ", StringComparison.OrdinalIgnoreCase) ||
               visibleName.StartsWith(targetName + "\u00A0", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRetainerListText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleanText = text
            .Replace('\u00A0', ' ')
            .Replace('\r', ' ')
            .Trim();
        var firstLine = cleanText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return firstLine ?? cleanText;
    }

    private static bool LooksLikeRetainerListEntry(string text) =>
        text.Length > 1 &&
        text.Length <= 32 &&
        text.Any(char.IsLetterOrDigit);

    private bool IsSummoningBellTarget(IGameObject target)
    {
        var name = target.Name.TextValue?.Trim();
        return !string.IsNullOrWhiteSpace(name) &&
               name.Contains("Summoning Bell", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSummoningBellTargeted(IGameObject? target)
    {
        return target is not null && this.IsSummoningBellTarget(target);
    }

    private bool IsAddonVisible(string name)
    {
        var addon = this.gameGui.GetAddonByName(name, 1);
        if (addon.Address == IntPtr.Zero)
            return false;

        var unitBase = (AtkUnitBase*)addon.Address;
        return unitBase->IsVisible;
    }

    private bool TryGetVisibleReadyAddon(string name, out AtkUnitBase* unitBase)
    {
        unitBase = null;
        var addon = this.gameGui.GetAddonByName(name, 1);
        if (addon.Address == IntPtr.Zero)
            return false;

        unitBase = (AtkUnitBase*)addon.Address;
        return unitBase is not null &&
               unitBase->IsVisible &&
               unitBase->IsReady;
    }

    private void FireIntCallback(AtkUnitBase* unitBase, int value, bool updateState = false)
    {
        Span<AtkValue> values = stackalloc AtkValue[1];
        values[0] = new AtkValue
        {
            Type = AtkValueType.Int,
            Int = value,
        };

        fixed (AtkValue* valuePointer = values)
        {
            unitBase->FireCallback(1, valuePointer, updateState);
        }
    }

    private bool IsActualRetainerListVisible() => this.IsAddonVisible("RetainerList");

    private bool IsRetainerShellVisible() => this.IsAddonVisible("Retainer");

    private bool IsRetainerWindowVisible() =>
        this.IsRetainerShellVisible() ||
        this.IsAddonVisible("InventoryRetainerLarge") ||
        this.IsAddonVisible("InventoryRetainer") ||
        this.IsActualRetainerListVisible();

    private bool IsRetainerInventoryShellVisible() =>
        this.IsAddonVisible("InventoryRetainerLarge") ||
        this.IsAddonVisible("InventoryRetainer") ||
        (this.IsRetainerShellVisible() && this.HasVisibleRetainerGrid());

    private bool HasBlockingRetainerInventoryUi() =>
        this.IsAddonVisible("InventoryRetainerLarge") ||
        this.IsAddonVisible("InventoryRetainer") ||
        (this.IsRetainerShellVisible() &&
         (this.HasVisibleRetainerGrid() ||
          this.IsAddonVisible("RetainerItemTransferList") ||
          this.IsAddonVisible("InputNumeric")));

    private bool IsRetainerInventoryOpen() => this.TryGetOpenRetainerName(out _) &&
        (this.HasVisibleRetainerGrid() || this.IsRetainerInventoryShellVisible());

    private bool HasRetainerUiSignal() =>
        this.IsAddonVisible("Talk") ||
        this.IsAddonVisible("SelectString") ||
        this.IsRetainerInventoryOpen();

    private bool HasAnyRetainerAddonVisible() =>
        this.IsRetainerWindowVisible() ||
        this.HasVisibleRetainerGrid() ||
        this.IsAddonVisible("RetainerItemTransferList") ||
        this.IsAddonVisible("InputNumeric");

    private bool IsAutoRetainerBusy() => this.TryGetAutoRetainerBusyMessage(out _);

    private bool TryFailIfAutoRetainerTookOver()
    {
        if (!this.TryGetAutoRetainerBusyMessage(out var busyMessage))
            return false;

        this.Fail(busyMessage);
        return true;
    }

    private bool TryGetAutoRetainerBusyMessage(out string message)
    {
        message = string.Empty;
        if (!this.IsAutoRetainerAvailable)
            return false;

        if (!this.TryIsAutoRetainerBusy(out var stateDescription))
            return false;

        message = string.IsNullOrWhiteSpace(stateDescription)
            ? "AutoRetainer is currently processing retainers. Wait for it to finish, then try Gwen's Dream again."
            : $"AutoRetainer is currently processing retainers ({stateDescription}). Wait for it to finish, then try Gwen's Dream again.";
        return true;
    }

    private bool TryIsAutoRetainerBusy(out string stateDescription)
    {
        stateDescription = string.Empty;

        try
        {
            if (this.autoRetainerIsBusy.InvokeFunc())
            {
                stateDescription = this.DescribeAutoRetainerActivity();
                return true;
            }
        }
        catch
        {
            // Fall back to reflection for older or partially-loaded AutoRetainer builds.
        }

        return this.TryIsAutoRetainerBusyViaReflection(out stateDescription);
    }

    private bool TryIsAutoRetainerBusyViaReflection(out string stateDescription)
    {
        stateDescription = string.Empty;

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

            var activeStates = new List<string>();
            var isBusy = false;

            var pluginType = assembly.GetType("AutoRetainer.AutoRetainer", false);
            var pluginField = pluginType?.GetField("P", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var pluginInstance = pluginField?.GetValue(null);
            var taskManagerInstance = pluginType?.GetField("TaskManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(pluginInstance);
            var taskManagerBusy = taskManagerInstance?.GetType().GetProperty("IsBusy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(taskManagerInstance);
            if (taskManagerBusy is bool { } taskQueueBusy && taskQueueBusy)
            {
                activeStates.Add("task queue busy");
                isBusy = true;
            }

            var schedulerType = assembly.GetType("AutoRetainer.Scheduler.SchedulerMain", false);
            var pluginEnabled = schedulerType?.GetProperty("PluginEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            var schedulerEnabled = pluginEnabled is bool { } schedulerRunning && schedulerRunning;

            var retainerPostProcessLocked = schedulerType?.GetField("RetainerPostProcessLocked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            if (retainerPostProcessLocked is bool { } retainerLocked && retainerLocked)
            {
                activeStates.Add("retainer post-process");
                isBusy = true;
            }

            var characterPostProcessLocked = schedulerType?.GetField("CharacterPostProcessLocked", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(null);
            if (characterPostProcessLocked is bool { } characterLocked && characterLocked)
            {
                activeStates.Add("character post-process");
                isBusy = true;
            }

            if (isBusy && schedulerEnabled)
                activeStates.Add("scheduler enabled");

            stateDescription = string.Join(", ", activeStates);
            return isBusy;
        }
        catch (Exception exception)
        {
            this.fileLog.Warning("Dream", $"Could not inspect AutoRetainer state: {exception.Message}");
            return false;
        }
    }

    private string DescribeAutoRetainerActivity()
    {
        this.TryIsAutoRetainerBusyViaReflection(out var stateDescription);
        return stateDescription;
    }

    private bool HasVisibleRetainerGrid() =>
        this.IsAddonVisible("RetainerGrid0") ||
        this.IsAddonVisible("RetainerGrid1") ||
        this.IsAddonVisible("RetainerGrid2") ||
        this.IsAddonVisible("RetainerGrid3") ||
        this.IsAddonVisible("RetainerGrid4") ||
        this.IsAddonVisible("RetainerCrystalGrid");

    private string DescribeVisibleRetainerAddons()
    {
        var visible = new List<string>();
        foreach (var addonName in new[]
                 {
                     "Retainer",
                     "InventoryRetainerLarge",
                     "InventoryRetainer",
                     "RetainerList",
                     "RetainerItemTransferList",
                     "RetainerItemTransferProgress",
                     "RetainerGrid0",
                     "RetainerGrid1",
                     "RetainerGrid2",
                     "RetainerGrid3",
                     "RetainerGrid4",
                     "RetainerCrystalGrid",
                     "SelectString",
                     "Talk",
                     "Dialogue",
                     "InputNumeric",
                 })
        {
            if (this.IsAddonVisible(addonName))
                visible.Add(addonName);
        }

        return visible.Count == 0 ? "none" : string.Join(", ", visible);
    }

    private void LogCloseStateIfNeeded(string phase)
    {
        var now = DateTime.UtcNow;
        if (now < this.lastCloseDebugAt.AddSeconds(1))
            return;

        this.lastCloseDebugAt = now;
        this.fileLog.Info("Dream", $"Close phase {phase}; visible add-ons: {this.DescribeVisibleRetainerAddons()}");
    }

    private bool TryGetOpenRetainerName(out string retainerName)
    {
        retainerName = string.Empty;
        var retainerManager = RetainerManager.Instance();
        var retainer = retainerManager is null ? null : retainerManager->GetActiveRetainer();
        if (retainer is null || retainer->RetainerId == 0)
            return false;

        var name = retainer->NameString;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        retainerName = name;
        return true;
    }

    private void MoveToStep(DreamStep nextStep, string message)
    {
        this.step = nextStep;
        this.stepStartedAt = DateTime.UtcNow;
        if (nextStep == DreamStep.Completed)
        {
            this.LastRunSucceeded = true;
            this.completionSequence++;
        }
        if (nextStep != DreamStep.WaitingForRetainerSelection)
            this.retainerSelectAttempt = 0;
        if (nextStep != DreamStep.WaitingForRetainerSelection)
            this.nextRetainerListDiagnosticAt = DateTime.MinValue;
        if (nextStep != DreamStep.WaitingForWithdraw)
            this.withdrawIssued = false;
        this.UpdateStatus(message, false);
        this.fileLog.Info("Dream", $"Step -> {nextStep}: {message}");
    }

    private bool Fail(string message)
    {
        this.activeTarget = null;
        this.pendingTargets.Clear();
        this.activeTargetIndex = 0;
        this.retainerSelectAttempt = 0;
        this.nextRetainerListDiagnosticAt = DateTime.MinValue;
        this.withdrawIssued = false;
        this.LastRunSucceeded = false;
        this.completionSequence++;
        this.step = DreamStep.Failed;
        this.UpdateStatus(message, true);
        this.fileLog.Warning("Dream", message);
        return false;
    }

    private void FailIfTimedOut(string message)
    {
        var timeout = this.step == DreamStep.WaitingForRetainerSelection
            ? RetainerSelectionTimeout
            : DefaultStepTimeout;
        if (DateTime.UtcNow - this.stepStartedAt > timeout)
            this.Fail(message);
    }

    private void UpdateStatus(string message, bool isError)
    {
        this.StatusMessage = message;
        this.StatusIsError = isError;
    }

    private enum DreamStep
    {
        Idle,
        WaitingForRetainerList,
        WaitingForRetainerSelection,
        WaitingForPromptOrMenu,
        WaitingForEntrustMenu,
        WaitingForWithdraw,
        WaitingForRetainerSwitch,
        WaitingForFinalInventoryClose,
        WaitingForFinalMenuClose,
        WaitingForFinalPromptClose,
        WaitingForFinalListClose,
        Completed,
        Failed,
    }

    private sealed record DreamTarget(
        ulong RetainerId,
        string RetainerName,
        uint ItemId,
        string ItemName,
        uint WithdrawQuantity,
        uint SnapshotQuantity);
}

public sealed record DreamDebugSnapshot(
    string StepName,
    bool IsActive,
    bool AutoRetainerAvailable,
    bool AutoRetainerBusy,
    bool LastRunSucceeded,
    bool StatusIsError,
    string StatusMessage,
    string OpenRetainerName,
    int PendingTargets,
    int ActiveTargetIndex,
    string ActiveTargetSummary,
    int RetainerSelectAttempt,
    bool WithdrawIssued,
    TimeSpan StepElapsed,
    uint CompletionSequence,
    string VisibleRetainerAddons);
