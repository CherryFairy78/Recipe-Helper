using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DalamudRecipeHelper;

public sealed unsafe class InventoryService : IDisposable
{
    private readonly FileLogService fileLog;
    private readonly RetainerSnapshotService retainerSnapshotService;
    private readonly IGameInventory gameInventory;
    private static readonly InventoryType[] PlayerInventories =
    [
        InventoryType.Inventory1,
        InventoryType.Inventory2,
        InventoryType.Inventory3,
        InventoryType.Inventory4,
        InventoryType.Crystals,
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2,
    ];

    public int LastScannedContainers { get; private set; }

    public int LastScannedSlots { get; private set; }

    public int LastItemStacks { get; private set; }

    public int LastStoredRetainers { get; private set; }

    public InventoryService(
        FileLogService fileLog,
        RetainerSnapshotService retainerSnapshotService,
        IGameInventory gameInventory)
    {
        this.fileLog = fileLog;
        this.retainerSnapshotService = retainerSnapshotService;
        this.gameInventory = gameInventory;
        this.retainerSnapshotService.SnapshotsChanged += this.OnInventoryChanged;
        this.gameInventory.InventoryChanged += this.OnGameInventoryChanged;
    }

    public event Action? InventoryChanged;

    public void Dispose()
    {
        this.gameInventory.InventoryChanged -= this.OnGameInventoryChanged;
        this.retainerSnapshotService.SnapshotsChanged -= this.OnInventoryChanged;
        this.retainerSnapshotService.Dispose();
    }

    public IReadOnlyDictionary<uint, OwnedInventoryItem> GetOwnedItems()
    {
        this.LastScannedContainers = 0;
        this.LastScannedSlots = 0;
        this.LastItemStacks = 0;
        this.LastStoredRetainers = 0;

        var nqQuantities = new Dictionary<uint, uint>();
        var hqQuantities = new Dictionary<uint, uint>();
        var nqLocations = new Dictionary<uint, HashSet<string>>();
        var hqLocations = new Dictionary<uint, HashSet<string>>();
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager is null)
        {
            this.fileLog.Warning("Inventory", "Inventory manager was unavailable.");
            return new Dictionary<uint, OwnedInventoryItem>();
        }

        foreach (var inventoryType in PlayerInventories)
        {
            var container = inventoryManager->GetInventoryContainer(inventoryType);
            if (container is null || !container->IsLoaded)
                continue;

            this.LastScannedContainers++;

            for (var i = 0; i < container->Size; i++)
            {
                this.LastScannedSlots++;

                var slot = container->GetInventorySlot(i);
                if (slot is null || slot->Quantity == 0)
                    continue;

                var itemId = slot->GetBaseItemId();
                if (itemId == 0)
                    itemId = NormalizeItemId(slot->GetItemId());

                if (itemId == 0)
                    continue;

                this.LastItemStacks++;
                var isHighQuality = slot->IsHighQuality();
                var quantities = isHighQuality ? hqQuantities : nqQuantities;
                var locations = isHighQuality ? hqLocations : nqLocations;
                quantities.TryGetValue(itemId, out var current);
                quantities[itemId] = (uint)Math.Min(
                    (ulong)current + (uint)slot->Quantity,
                    uint.MaxValue);

                if (!locations.TryGetValue(itemId, out var itemLocations))
                {
                    itemLocations = [];
                    locations[itemId] = itemLocations;
                }

                itemLocations.Add(GetInventoryName(inventoryType));
            }
        }

        var storedRetainers = this.retainerSnapshotService.GetSnapshots();
        this.LastStoredRetainers = storedRetainers.Count;
        foreach (var retainer in storedRetainers)
        {
            foreach (var (itemId, item) in retainer.Items)
            {
                var retainerLocation =
                    $"{retainer.Name} ({AddSaturating(item.NqQuantity, item.HqQuantity)})";
                if (item.NqQuantity > 0)
                {
                    nqQuantities[itemId] = AddSaturating(
                        nqQuantities.GetValueOrDefault(itemId),
                        item.NqQuantity);
                    AddLocation(nqLocations, itemId, retainerLocation);
                    this.LastItemStacks++;
                }

                if (item.HqQuantity > 0)
                {
                    hqQuantities[itemId] = AddSaturating(
                        hqQuantities.GetValueOrDefault(itemId),
                        item.HqQuantity);
                    AddLocation(hqLocations, itemId, retainerLocation);
                    this.LastItemStacks++;
                }
            }
        }

        var owned = new Dictionary<uint, OwnedInventoryItem>();
        foreach (var itemId in nqQuantities.Keys.Concat(hqQuantities.Keys).Distinct())
        {
            owned[itemId] = new OwnedInventoryItem(
                nqQuantities.GetValueOrDefault(itemId),
                hqQuantities.GetValueOrDefault(itemId),
                nqLocations.TryGetValue(itemId, out var nqItemLocations) ? [.. nqItemLocations] : [],
                hqLocations.TryGetValue(itemId, out var hqItemLocations) ? [.. hqItemLocations] : []);
        }

        this.fileLog.Info(
            "Inventory",
            $"Scanned {this.LastScannedContainers} live container(s), {this.LastScannedSlots} slot(s), {this.LastStoredRetainers} stored retainer(s), {this.LastItemStacks} occupied or stored stack(s), {owned.Count} unique item(s).");
        return owned;
    }

    private static void AddLocation(
        IDictionary<uint, HashSet<string>> locations,
        uint itemId,
        string location)
    {
        if (!locations.TryGetValue(itemId, out var itemLocations))
        {
            itemLocations = [];
            locations[itemId] = itemLocations;
        }

        itemLocations.Add(location);
    }

    private static uint AddSaturating(uint left, uint right) =>
        (uint)Math.Min((ulong)left + right, uint.MaxValue);

    private static string GetInventoryName(InventoryType inventoryType) => inventoryType switch
    {
        InventoryType.Inventory1 or
        InventoryType.Inventory2 or
        InventoryType.Inventory3 or
        InventoryType.Inventory4 => "Inventory",
        InventoryType.Crystals => "Crystals",
        InventoryType.SaddleBag1 => "Saddlebag 1",
        InventoryType.SaddleBag2 => "Saddlebag 2",
        InventoryType.PremiumSaddleBag1 => "Premium Saddlebag 1",
        InventoryType.PremiumSaddleBag2 => "Premium Saddlebag 2",
        _ => inventoryType.ToString(),
    };

    private static uint NormalizeItemId(uint itemId)
    {
        const uint highQualityOffset = 1_000_000;
        return itemId > highQualityOffset ? itemId - highQualityOffset : itemId;
    }

    private void OnGameInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events) =>
        this.OnInventoryChanged();

    private void OnInventoryChanged() => this.InventoryChanged?.Invoke();
}
