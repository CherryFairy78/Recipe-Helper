using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace DalamudRecipeHelper;

public sealed unsafe class RetainerSnapshotService : IDisposable
{
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly IFramework framework;
    private readonly IPlayerState playerState;
    private readonly FileLogService fileLog;
    private readonly string snapshotPath;
    private readonly object snapshotLock = new();
    private Dictionary<ulong, PersistedRetainerSnapshot> snapshots = [];
    private ulong openRetainerId;
    private DateTime nextCaptureAt;
    private DateTime nextNameRefreshAt;
    private bool forceCapture;

    public RetainerSnapshotService(
        IFramework framework,
        IPlayerState playerState,
        string pluginConfigDirectory,
        FileLogService fileLog)
    {
        this.framework = framework;
        this.playerState = playerState;
        this.fileLog = fileLog;
        var dataDirectory = Path.Combine(pluginConfigDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        this.snapshotPath = Path.Combine(dataDirectory, "retainer-inventory.json");
        this.Load();
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public event Action? SnapshotsChanged;

    public string SnapshotPath => this.snapshotPath;

    public IReadOnlyList<StoredRetainerInventory> GetSnapshots()
    {
        this.TryRefreshSnapshotNames();

        lock (this.snapshotLock)
        {
            return this.snapshots.Values
                .Where(snapshot => snapshot.OwnerContentId == this.playerState.ContentId)
                .OrderBy(snapshot => snapshot.Name)
                .Select(snapshot => new StoredRetainerInventory(
                    snapshot.RetainerId,
                    snapshot.Name,
                    snapshot.CapturedAt,
                    snapshot.Items.ToDictionary(
                        item => item.Key,
                        item => new StoredRetainerItem(
                            item.Value.NqQuantity,
                            item.Value.HqQuantity))))
                .ToList();
        }
    }

    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;
        if (this.TryGetOpenRetainer(out var retainerId, out var retainerName))
            this.Capture(retainerId, retainerName, true);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!this.TryGetOpenRetainer(out var retainerId, out var retainerName))
        {
            this.openRetainerId = 0;
            this.forceCapture = false;
            return;
        }

        if (DateTime.UtcNow >= this.nextNameRefreshAt)
            this.TryRefreshSnapshotNames();

        var now = DateTime.UtcNow;
        if (this.openRetainerId != retainerId)
        {
            this.openRetainerId = retainerId;
            this.nextCaptureAt = now.AddMilliseconds(750);
            this.forceCapture = true;
            return;
        }

        if (now < this.nextCaptureAt)
            return;

        this.Capture(retainerId, retainerName, this.forceCapture);
        this.forceCapture = false;
        this.nextCaptureAt = now.AddSeconds(1);
    }

    private bool TryGetOpenRetainer(out ulong retainerId, out string retainerName)
    {
        retainerId = 0;
        retainerName = string.Empty;

        var retainerManager = RetainerManager.Instance();
        var retainer = retainerManager is null ? null : retainerManager->GetActiveRetainer();
        if (retainer is null || retainer->RetainerId == 0)
            return false;

        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager is null)
            return false;

        foreach (var inventoryType in RetainerInventories)
        {
            var container = inventoryManager->GetInventoryContainer(inventoryType);
            if (container is null || !container->IsLoaded)
                return false;
        }

        var name = ReadRetainerName(retainer);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        retainerId = retainer->RetainerId;
        retainerName = name;
        return true;
    }

    private void Capture(ulong retainerId, string retainerName, bool forceSave)
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager is null)
            return;

        var items = new Dictionary<uint, PersistedRetainerItem>();
        foreach (var inventoryType in RetainerInventories)
        {
            var container = inventoryManager->GetInventoryContainer(inventoryType);
            if (container is null || !container->IsLoaded)
                return;

            for (var i = 0; i < container->Size; i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot is null || slot->Quantity == 0)
                    continue;

                var itemId = slot->GetBaseItemId();
                if (itemId == 0)
                    itemId = NormalizeItemId(slot->GetItemId());
                if (itemId == 0)
                    continue;

                if (!items.TryGetValue(itemId, out var storedItem))
                {
                    storedItem = new PersistedRetainerItem();
                    items[itemId] = storedItem;
                }

                if (slot->IsHighQuality())
                    storedItem.HqQuantity = AddSaturating(storedItem.HqQuantity, (uint)slot->Quantity);
                else
                    storedItem.NqQuantity = AddSaturating(storedItem.NqQuantity, (uint)slot->Quantity);
            }
        }

        var changed = false;
        lock (this.snapshotLock)
        {
            if (!this.snapshots.TryGetValue(retainerId, out var previous) ||
                !ItemsEqual(previous.Items, items))
            {
                changed = true;
            }

            if (!changed && !forceSave)
                return;

            this.snapshots[retainerId] = new PersistedRetainerSnapshot
            {
                RetainerId = retainerId,
                OwnerContentId = this.playerState.ContentId,
                Name = retainerName,
                CapturedAt = DateTimeOffset.UtcNow,
                Items = items,
            };
            this.SaveLocked();
        }

        this.fileLog.Info(
            "RetainerSnapshots",
            $"Saved {items.Count} unique item(s) for retainer '{retainerName}'.");
        this.SnapshotsChanged?.Invoke();
    }

    private void Load()
    {
        if (!File.Exists(this.snapshotPath))
            return;

        try
        {
            var json = File.ReadAllText(this.snapshotPath);
            var store = JsonSerializer.Deserialize<PersistedRetainerStore>(json, JsonOptions);
            this.snapshots = store?.Retainers?
                .Where(snapshot => snapshot.RetainerId != 0)
                .ToDictionary(snapshot => snapshot.RetainerId) ?? [];
            this.fileLog.Info(
                "RetainerSnapshots",
                $"Loaded {this.snapshots.Count} stored retainer snapshot(s).");
        }
        catch (Exception exception)
        {
            this.fileLog.Error(
                "RetainerSnapshots",
                "Could not load stored retainer inventory data.",
                exception);
            this.snapshots = [];
        }
    }

    private void SaveLocked()
    {
        try
        {
            var store = new PersistedRetainerStore
            {
                Version = 1,
                Retainers = this.snapshots.Values.OrderBy(snapshot => snapshot.Name).ToList(),
            };
            var temporaryPath = $"{this.snapshotPath}.tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(store, JsonOptions));
            File.Move(temporaryPath, this.snapshotPath, true);
        }
        catch (Exception exception)
        {
            this.fileLog.Error(
                "RetainerSnapshots",
                "Could not save retainer inventory data.",
                exception);
        }
    }

    private void TryRefreshSnapshotNames()
    {
        this.nextNameRefreshAt = DateTime.UtcNow.AddSeconds(10);

        var retainerManager = RetainerManager.Instance();
        if (retainerManager is null)
            return;

        Dictionary<ulong, string> liveNames = [];
        for (var i = 0; i < retainerManager->Retainers.Length; i++)
        {
            var retainer = retainerManager->Retainers[i];
            if (retainer.RetainerId == 0)
                continue;

            var name = ReadRetainerName(&retainer);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            liveNames[retainer.RetainerId] = name;
        }

        if (liveNames.Count == 0)
            return;

        var changed = false;
        lock (this.snapshotLock)
        {
            foreach (var snapshot in this.snapshots.Values)
            {
                if (!liveNames.TryGetValue(snapshot.RetainerId, out var liveName) ||
                    string.Equals(snapshot.Name, liveName, StringComparison.Ordinal))
                {
                    continue;
                }

                snapshot.Name = liveName;
                changed = true;
            }

            if (changed)
                this.SaveLocked();
        }

        if (changed)
        {
            this.fileLog.Info(
                "RetainerSnapshots",
                "Refreshed stored retainer names from the live retainer list.");
            this.SnapshotsChanged?.Invoke();
        }
    }

    private static string ReadRetainerName(RetainerManager.Retainer* retainer)
    {
        if (retainer is null)
            return string.Empty;

        var rawName = retainer->Name;
        var length = rawName.IndexOf((byte)0);
        if (length < 0)
            length = rawName.Length;

        var name = length > 0
            ? Encoding.UTF8.GetString(rawName[..length])
            : string.Empty;
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        return retainer->NameString;
    }

    private static bool ItemsEqual(
        IReadOnlyDictionary<uint, PersistedRetainerItem> left,
        IReadOnlyDictionary<uint, PersistedRetainerItem> right) =>
        left.Count == right.Count &&
        left.All(item =>
            right.TryGetValue(item.Key, out var other) &&
            item.Value.NqQuantity == other.NqQuantity &&
            item.Value.HqQuantity == other.HqQuantity);

    private static uint AddSaturating(uint left, uint right) =>
        (uint)Math.Min((ulong)left + right, uint.MaxValue);

    private static uint NormalizeItemId(uint itemId)
    {
        const uint highQualityOffset = 1_000_000;
        return itemId > highQualityOffset ? itemId - highQualityOffset : itemId;
    }

    private sealed class PersistedRetainerStore
    {
        public int Version { get; set; } = 1;

        public List<PersistedRetainerSnapshot> Retainers { get; set; } = [];
    }

    private sealed class PersistedRetainerSnapshot
    {
        public ulong RetainerId { get; set; }

        public ulong OwnerContentId { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTimeOffset CapturedAt { get; set; }

        public Dictionary<uint, PersistedRetainerItem> Items { get; set; } = [];
    }

    private sealed class PersistedRetainerItem
    {
        public uint NqQuantity { get; set; }

        public uint HqQuantity { get; set; }
    }
}
