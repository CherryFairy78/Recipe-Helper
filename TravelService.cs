using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace DalamudRecipeHelper;

public sealed unsafe class TravelService : IDisposable
{
    private readonly IDataManager dataManager;
    private readonly IAetheryteList aetheryteList;
    private readonly IFramework framework;
    private readonly ICondition condition;
    private readonly FileLogService fileLog;
    private readonly Dictionary<uint, IReadOnlyList<GatheringDestination>> destinationCache = [];
    private PendingMapFlag? pendingMapFlag;

    public TravelService(
        IDataManager dataManager,
        IAetheryteList aetheryteList,
        IFramework framework,
        ICondition condition,
        FileLogService fileLog)
    {
        this.dataManager = dataManager;
        this.aetheryteList = aetheryteList;
        this.framework = framework;
        this.condition = condition;
        this.fileLog = fileLog;
        this.framework.Update += this.OnFrameworkUpdate;
    }

    public void Dispose()
    {
        this.framework.Update -= this.OnFrameworkUpdate;
    }

    public IReadOnlyList<GatheringDestination> GetDestinations(uint itemId)
    {
        if (this.destinationCache.TryGetValue(itemId, out var cached))
        {
            this.fileLog.Info("Travel", $"Returned {cached.Count} cached destination(s) for item {itemId}.");
            return cached;
        }

        var rawLocations = this.FindMiningAndBotanyLocations(itemId)
            .Concat(this.FindFishingLocations(itemId))
            .Concat(this.FindSpearfishingLocations(itemId))
            .GroupBy(location => (location.TerritoryId, location.LocationName))
            .Select(group => group.First())
            .OrderBy(location => location.ZoneName)
            .ThenBy(location => location.LocationName)
            .ToList();

        var destinations = rawLocations
            .Select(location => this.AddNearestAetheryte(location, itemId))
            .ToList();

        this.destinationCache[itemId] = destinations;
        this.fileLog.Info("Travel", $"Found {destinations.Count} destination(s) for item {itemId}.");
        return destinations;
    }

    public bool Teleport(GatheringDestination destination)
    {
        if (destination.AetheryteId is not { } aetheryteId)
        {
            this.fileLog.Warning("Travel", $"Item {destination.ItemId} has no available aetheryte.");
            return false;
        }

        var telepo = Telepo.Instance();
        if (telepo is null || !telepo->Teleport(aetheryteId, destination.AetheryteSubIndex))
        {
            this.fileLog.Warning("Travel", $"Teleport failed for item {destination.ItemId}, aetheryte {aetheryteId}.");
            return false;
        }

        if (destination.ItemId <= ushort.MaxValue)
            this.pendingMapFlag = new PendingMapFlag(destination, DateTime.UtcNow);

        this.fileLog.Info("Travel", $"Teleport started for item {destination.ItemId}, aetheryte {aetheryteId}.");
        return true;
    }

    public bool ShowOnMap(GatheringDestination destination)
    {
        if (destination.ItemId <= ushort.MaxValue)
        {
            var gatheringNote = AgentGatheringNote.Instance();
            var gatheringMapAgent = AgentMap.Instance();
            if (gatheringNote is not null && gatheringMapAgent is not null)
            {
                gatheringNote->OpenGatherableByItemId((ushort)destination.ItemId);
                var gatheringArea = gatheringNote->GatheringAreaInfo;
                if (gatheringArea is not null && gatheringArea->OpenMapInfo.MapId != 0)
                {
                    gatheringMapAgent->OpenMap(&gatheringArea->OpenMapInfo);
                    Plugin.Log.Information(
                        "Opened Gathering Log map for item {ItemId}: territory {Territory}, map {Map}.",
                        destination.ItemId,
                        gatheringArea->OpenMapInfo.TerritoryId,
                        gatheringArea->OpenMapInfo.MapId);
                    this.fileLog.Info(
                        "Travel",
                        $"Opened Gathering Log map for item {destination.ItemId}, territory {gatheringArea->OpenMapInfo.TerritoryId}, map {gatheringArea->OpenMapInfo.MapId}.");
                    return true;
                }
            }
        }

        if (destination.MapId is not { } mapId ||
            destination.X is not { } x ||
            destination.Z is not { } z)
        {
            this.fileLog.Warning("Travel", $"Map coordinates were unavailable for item {destination.ItemId}.");
            return false;
        }

        var mapAgent = AgentMap.Instance();
        if (mapAgent is null)
        {
            this.fileLog.Warning("Travel", $"Map agent was unavailable for item {destination.ItemId}.");
            return false;
        }

        mapAgent->FlagMarkerCount = 0;
        mapAgent->SetFlagMapMarker(
            destination.TerritoryId,
            mapId,
            new Vector3(x, 0, z));
        mapAgent->OpenMap(mapId, destination.TerritoryId, destination.LocationName);
        Plugin.Log.Information(
            "Opened gathering map for {Location}: territory {Territory}, map {Map}, X {X}, Z {Z}.",
            destination.LocationName,
            destination.TerritoryId,
            mapId,
            x,
            z);
        this.fileLog.Info(
            "Travel",
            $"Opened map for item {destination.ItemId}, territory {destination.TerritoryId}, map {mapId}, X {x}, Z {z}.");
        return true;
    }

    private IEnumerable<RawGatheringLocation> FindMiningAndBotanyLocations(uint itemId)
    {
        var gatheringItemIds = this.dataManager.GetExcelSheet<GatheringItem>()?
            .Where(item => item.Item.RowId == itemId)
            .Select(item => item.RowId)
            .ToHashSet() ?? [];
        if (gatheringItemIds.Count == 0)
            yield break;

        var pointBaseIds = this.dataManager.GetExcelSheet<GatheringPointBase>()?
            .Where(point => point.Item.Any(item => gatheringItemIds.Contains(item.RowId)))
            .Select(point => point.RowId)
            .ToHashSet() ?? [];

        var levels = this.dataManager.GetExcelSheet<Level>();
        var points = this.dataManager.GetExcelSheet<GatheringPoint>();
        if (points is null)
            yield break;

        foreach (var point in points.Where(point => pointBaseIds.Contains(point.GatheringPointBase.RowId)))
        {
            var territoryId = point.TerritoryType.RowId;
            var level = levels?.FirstOrDefault(level =>
                level.Object.RowId == point.RowId &&
                level.Territory.RowId == territoryId);
            var hasCoordinates = level?.RowId != 0;

            yield return new RawGatheringLocation(
                territoryId,
                this.GetZoneName(territoryId),
                this.GetPlaceName(point.PlaceName.RowId, "Gathering point"),
                hasCoordinates && level?.Map.RowId != 0
                    ? level?.Map.RowId
                    : this.GetTerritoryMapId(territoryId),
                hasCoordinates ? level?.X : null,
                hasCoordinates ? level?.Z : null);
        }
    }

    private IEnumerable<RawGatheringLocation> FindFishingLocations(uint itemId)
    {
        var spots = this.dataManager.GetExcelSheet<FishingSpot>();
        if (spots is null)
            yield break;

        foreach (var spot in spots.Where(spot => spot.Item.Any(item => item.RowId == itemId)))
        {
            yield return new RawGatheringLocation(
                spot.TerritoryType.RowId,
                this.GetZoneName(spot.TerritoryType.RowId),
                this.GetPlaceName(spot.PlaceName.RowId, "Fishing spot"),
                this.GetTerritoryMapId(spot.TerritoryType.RowId),
                spot.X,
                spot.Z);
        }
    }

    private IEnumerable<RawGatheringLocation> FindSpearfishingLocations(uint itemId)
    {
        var items = this.dataManager.GetExcelSheet<SpearfishingItem>();
        if (items is null)
            yield break;

        foreach (var item in items.Where(item => item.Item.RowId == itemId))
        {
            var territoryId = item.TerritoryType.RowId;
            yield return new RawGatheringLocation(
                territoryId,
                this.GetZoneName(territoryId),
                "Spearfishing waters",
                this.GetTerritoryMapId(territoryId),
                null,
                null);
        }
    }

    private GatheringDestination AddNearestAetheryte(RawGatheringLocation location, uint itemId)
    {
        var candidates = this.aetheryteList
            .Where(aetheryte => aetheryte.TerritoryId == location.TerritoryId)
            .Select(aetheryte =>
            {
                var position = this.GetAetherytePosition(aetheryte.AetheryteId, location.TerritoryId);
                var distance = location.X is { } x && location.Z is { } z && position is { } coordinates
                    ? MathF.Pow(coordinates.X - x, 2) + MathF.Pow(coordinates.Z - z, 2)
                    : float.MaxValue;

                return new
                {
                    Entry = aetheryte,
                    Name = this.GetAetheryteName(aetheryte.AetheryteId),
                    Distance = distance,
                };
            })
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Entry.GilCost)
            .FirstOrDefault();

        return new GatheringDestination(
            itemId,
            location.TerritoryId,
            location.ZoneName,
            location.LocationName,
            candidates?.Entry.AetheryteId,
            candidates?.Entry.SubIndex ?? 0,
            candidates?.Name,
            candidates?.Entry.GilCost ?? 0,
            location.MapId,
            location.X,
            location.Z);
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (this.pendingMapFlag is not { } pending)
            return;

        var elapsed = DateTime.UtcNow - pending.StartedAt;
        if (elapsed > TimeSpan.FromSeconds(45))
        {
            Plugin.Log.Warning("Timed out while waiting to open the gathering map flag.");
            this.fileLog.Warning("Travel", $"Timed out waiting to open the map for item {pending.Destination.ItemId}.");
            this.pendingMapFlag = null;
            return;
        }

        var isBetweenAreas =
            this.condition[ConditionFlag.BetweenAreas] ||
            this.condition[ConditionFlag.BetweenAreas51];
        if (isBetweenAreas)
        {
            pending.TransitionStarted = true;
            pending.StableSince = null;
            return;
        }

        pending.StableSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - pending.StableSince < TimeSpan.FromSeconds(2))
            return;

        if (!pending.TransitionStarted && elapsed < TimeSpan.FromSeconds(10))
            return;

        if (pending.LastAttemptAt is { } lastAttempt &&
            DateTime.UtcNow - lastAttempt < TimeSpan.FromSeconds(1))
            return;

        var destination = pending.Destination;
        pending.LastAttemptAt = DateTime.UtcNow;
        if (this.ShowOnMap(destination))
        {
            this.pendingMapFlag = null;
        }
    }

    private (float X, float Z)? GetAetherytePosition(uint aetheryteId, uint territoryId)
    {
        var aetheryte = this.dataManager.GetExcelSheet<Aetheryte>()?.GetRowOrDefault(aetheryteId);
        var levels = this.dataManager.GetExcelSheet<Level>();
        if (aetheryte is not { } aetheryteRow || levels is null)
            return null;

        foreach (var levelReference in aetheryteRow.Level)
        {
            if (levelReference.RowId == 0)
                continue;

            var level = levels.GetRowOrDefault(levelReference.RowId);
            if (level is { } levelRow && levelRow.Territory.RowId == territoryId)
                return (levelRow.X, levelRow.Z);
        }

        return null;
    }

    private string GetAetheryteName(uint aetheryteId)
    {
        var aetheryte = this.dataManager.GetExcelSheet<Aetheryte>()?.GetRowOrDefault(aetheryteId);
        return aetheryte is { } row
            ? this.GetPlaceName(row.PlaceName.RowId, $"Aetheryte #{aetheryteId}")
            : $"Aetheryte #{aetheryteId}";
    }

    private string GetZoneName(uint territoryId)
    {
        var territory = this.dataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return territory is { } row
            ? this.GetPlaceName(row.PlaceName.RowId, $"Territory #{territoryId}")
            : $"Territory #{territoryId}";
    }

    private uint? GetTerritoryMapId(uint territoryId)
    {
        var territory = this.dataManager.GetExcelSheet<TerritoryType>()?.GetRowOrDefault(territoryId);
        return territory is { } row && row.Map.RowId != 0 ? row.Map.RowId : null;
    }

    private string GetPlaceName(uint placeNameId, string fallback)
    {
        if (placeNameId == 0)
            return fallback;

        var placeName = this.dataManager.GetExcelSheet<PlaceName>()?.GetRowOrDefault(placeNameId);
        var name = placeName?.Name.ToString();
        return string.IsNullOrWhiteSpace(name) ? fallback : name;
    }

    private sealed record RawGatheringLocation(
        uint TerritoryId,
        string ZoneName,
        string LocationName,
        uint? MapId,
        float? X,
        float? Z);

    private sealed class PendingMapFlag
    {
        public PendingMapFlag(GatheringDestination destination, DateTime startedAt)
        {
            this.Destination = destination;
            this.StartedAt = startedAt;
        }

        public GatheringDestination Destination { get; }

        public DateTime StartedAt { get; }

        public bool TransitionStarted { get; set; }

        public DateTime? StableSince { get; set; }

        public DateTime? LastAttemptAt { get; set; }
    }
}
