using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace DalamudRecipeHelper;

public sealed class AetherialReductionService
{
    private static readonly ReductionMapping[] Mappings =
    [
        new(12936, 12968, "Granular Clay", false),
        new(12936, 5218, "Lightning Moraine", false),
        new(12936, 33148, "Pot Marjoram", false),
        new(12936, 5214, "Fire Moraine", false),
        new(12937, 12969, "Peat Moss", false),
        new(12937, 12967, "Bright Lightning Rock", false),
        new(12937, 33149, "Water Mint", false),
        new(12937, 12966, "Bright Fire Rock", false),
        new(12939, 33147, "Humic Soil", false),
        new(12938, 5224, "Radiant Lightning Moraine", false),
        new(12939, 33150, "Wild Sage", false),
        new(12938, 5220, "Radiant Fire Moraine", false),
        new(15648, 15948, "Lover's Laurel", false),
        new(15648, 15949, "Radiant Astral Moraine", false),
        new(20015, 33152, "Dacite", false),
        new(20013, 20012, "Doman Yellow", false),
        new(20014, 20009, "Schorl", false),
        new(20014, 33151, "Countess Tea Leaves", false),
        new(20016, 19937, "Torreya Branch", false),
        new(20013, 33153, "Rhodolite", false),
        new(23182, 23221, "Yanxian Verbena", false),
        new(23182, 23220, "Yanxian Soil", false),
        new(27811, 27542, "Voeburt Bichir", true),
        new(27812, 27543, "Poecilia", true),
        new(27811, 27805, "Gale Rock", false),
        new(27811, 27808, "White Clay", false),
        new(27812, 27806, "Solarite", false),
        new(27812, 27809, "Sweet Marjoram", false),
        new(27814, 27810, "Bog Sage", false),
        new(27813, 27807, "Shade Quartz", false),
        new(30590, 30593, "Fuchsia Bloom", true),
        new(30590, 30591, "Thunder Rock", false),
        new(30590, 30592, "Levin Mint", false),
        new(36223, 36285, "Lunar Quartz", false),
        new(36223, 36287, "Ewer Clay", false),
        new(36223, 36525, "Gilled Topknot", true),
        new(38936, 38939, "Verdigris Guppy", true),
        new(36226, 36577, "Othardian Lumpsucker", true),
        new(36224, 36286, "Ghostly Umbral Rock", false),
        new(36225, 36288, "Palm Chippings", false),
        new(39241, 39240, "Phyllinos", true),
        new(37696, 37694, "Prime Siderite", false),
        new(38936, 38937, "Earthen Quartz", false),
        new(37693, 37691, "Prime Crystalbloom", false),
        new(38936, 38938, "Sophora Roots", false),
        new(37698, 37697, "Mayashell", true),
        new(39236, 39234, "Prime Sphongos", false),
        new(39818, 39807, "Connoisseur's Miracle Apple", false),
        new(39239, 39237, "Prime Achondrite", false),
        new(39817, 39805, "Connoisseur's Soiled Femur", false),
        new(39908, 39909, "Prime Chloroschist", false),
        new(39911, 39906, "Prime Haritaki", false),
        new(39913, 39912, "The Fury's Aegis", true),
        new(41415, 41416, "Prime Fossilized Dragon's Scale", false),
        new(41418, 41413, "Prime Kukuru Beans", false),
        new(41420, 41419, "Stargilt Lobster", true),
        new(44035, 43931, "Electrocoal", false),
        new(44035, 43933, "Goldbranch", false),
        new(44038, 43847, "Longnose Gar", true),
        new(44035, 43829, "Sunlit Prism", true),
        new(44036, 43932, "Brightwind Ore", false),
        new(44037, 43934, "Volcanic Grass", false),
        new(46246, 46249, "Purple Palate", true),
        new(46246, 46247, "Levin Quartz", false),
        new(46246, 46248, "Calamus Root", false),
    ];

    private readonly IDataManager dataManager;
    private readonly FileLogService fileLog;
    private readonly Dictionary<uint, IReadOnlyList<EorzeaNodeWindow>> nodeWindowCache = [];
    private IReadOnlyDictionary<uint, IReadOnlyList<AetherialReductionSource>>? sourcesByResult;

    public AetherialReductionService(IDataManager dataManager, FileLogService fileLog)
    {
        this.dataManager = dataManager;
        this.fileLog = fileLog;
    }

    public IReadOnlyList<AetherialReductionSource> GetSources(uint resultItemId)
    {
        this.EnsureSources();
        return this.sourcesByResult!.GetValueOrDefault(resultItemId) ?? [];
    }

    public AetherialReductionSource? GetPreferredSource(
        IReadOnlyList<AetherialReductionSource>? sources)
    {
        if (sources is null || sources.Count == 0)
            return null;

        var currentMinute = GetCurrentEorzeaMinute();
        return sources
            .OrderBy(source => GetWaitMinutes(source, currentMinute))
            .ThenBy(source => source.IsFishing)
            .First();
    }

    public string GetTimerText(AetherialReductionSource source)
    {
        if (source.IsFishing || source.Windows.Count == 0)
            return "Check GatherBuddy";

        var currentMinute = GetCurrentEorzeaMinute();
        var active = source.Windows
            .Select(window => (Window: window, Remaining: GetRemainingMinutes(window, currentMinute)))
            .Where(entry => entry.Remaining > 0)
            .OrderByDescending(entry => entry.Remaining)
            .FirstOrDefault();
        if (active.Remaining > 0)
            return $"Now · {FormatRealDuration(active.Remaining)} left";

        var wait = source.Windows.Min(window => ForwardMinutes(currentMinute, window.StartMinute));
        return $"In {FormatRealDuration(wait)}";
    }

    public string? GetGatheringTimerText(uint itemId)
    {
        if (itemId is >= 2 and <= 19)
            return null;

        var windows = this.GetNodeWindows(itemId);
        return windows.Count == 0
            ? null
            : this.GetTimerText(new AetherialReductionSource(itemId, string.Empty, false, windows));
    }

    public IReadOnlyList<IngredientNeed> OrderByAvailability(
        IEnumerable<IngredientNeed> materials)
    {
        var currentMinute = GetCurrentEorzeaMinute();
        return materials
            .Select(material => (
                Material: material,
                SortKey: this.GetAvailabilitySortKey(
                    material.ItemId,
                    material.ReductionSources,
                    currentMinute)))
            .OrderBy(entry => entry.SortKey)
            .ThenBy(entry => entry.Material.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(entry => entry.Material.ItemId)
            .Select(entry => entry.Material)
            .ToList();
    }

    private double GetAvailabilitySortKey(
        uint itemId,
        IReadOnlyList<AetherialReductionSource>? reductionSources,
        double currentMinute)
    {
        if (reductionSources is { Count: > 0 })
        {
            var reductionWindows = reductionSources
                .Where(source => !source.IsFishing)
                .SelectMany(source => source.Windows)
                .Distinct()
                .ToList();
            return reductionWindows.Count > 0
                ? GetWindowAvailabilitySortKey(reductionWindows, currentMinute)
                : 2001d;
        }

        if (itemId is >= 2 and <= 19)
            return 2000d;

        var windows = this.GetNodeWindows(itemId);
        if (windows.Count == 0)
            return 2000d;

        return GetWindowAvailabilitySortKey(windows, currentMinute);
    }

    public bool IsCurrentlyAvailable(
        uint itemId,
        IReadOnlyList<AetherialReductionSource>? reductionSources)
    {
        var currentMinute = GetCurrentEorzeaMinute();
        return this.GetTimedWindows(itemId, reductionSources).Any(window =>
            GetRemainingMinutes(window, currentMinute) > 0);
    }

    public bool HasTimedWindow(
        uint itemId,
        IReadOnlyList<AetherialReductionSource>? reductionSources) =>
        this.GetTimedWindows(itemId, reductionSources).Count > 0;

    private IReadOnlyList<EorzeaNodeWindow> GetTimedWindows(
        uint itemId,
        IReadOnlyList<AetherialReductionSource>? reductionSources)
    {
        if (reductionSources is { Count: > 0 })
        {
            return reductionSources
                .Where(source => !source.IsFishing)
                .SelectMany(source => source.Windows)
                .Distinct()
                .ToList();
        }

        return itemId is >= 2 and <= 19
            ? []
            : this.GetNodeWindows(itemId);
    }

    private void EnsureSources()
    {
        if (this.sourcesByResult is not null)
            return;

        this.sourcesByResult = Mappings
            .GroupBy(mapping => mapping.ResultItemId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<AetherialReductionSource>)group
                    .Select(mapping => new AetherialReductionSource(
                        mapping.SourceItemId,
                        mapping.SourceName,
                        mapping.IsFishing,
                        mapping.IsFishing ? [] : this.GetNodeWindows(mapping.SourceItemId)))
                    .ToList());

        this.fileLog.Info(
            "AetherialReduction",
            $"Loaded {Mappings.Length} reduction source mapping(s) for {this.sourcesByResult.Count} result item(s).");
    }

    private IReadOnlyList<EorzeaNodeWindow> GetNodeWindows(uint sourceItemId)
    {
        if (this.nodeWindowCache.TryGetValue(sourceItemId, out var cached))
            return cached;

        var windows = this.ReadNodeWindows(sourceItemId);
        this.nodeWindowCache[sourceItemId] = windows;
        return windows;
    }

    private IReadOnlyList<EorzeaNodeWindow> ReadNodeWindows(uint sourceItemId)
    {
        var gatheringItemIds = this.dataManager.GetExcelSheet<GatheringItem>()?
            .Where(item => item.Item.RowId == sourceItemId)
            .Select(item => item.RowId)
            .ToHashSet() ?? [];
        var pointBaseIds = this.dataManager.GetExcelSheet<GatheringPointBase>()?
            .Where(point => point.Item.Any(item => gatheringItemIds.Contains(item.RowId)))
            .Select(point => point.RowId)
            .ToHashSet() ?? [];
        var additionalPointIds = this.dataManager.GetSubrowExcelSheet<GatheringItemPoint>()?
            .SelectMany(rows => rows)
            .Where(row => gatheringItemIds.Contains(row.RowId))
            .Select(row => row.GatheringPoint.RowId)
            .ToHashSet() ?? [];
        var transients = this.dataManager.GetExcelSheet<GatheringPointTransient>();
        var points = this.dataManager.GetExcelSheet<GatheringPoint>();
        if (transients is null || points is null)
            return [];

        var windows = new List<EorzeaNodeWindow>();
        foreach (var point in points.Where(point =>
                     pointBaseIds.Contains(point.GatheringPointBase.RowId) ||
                     additionalPointIds.Contains(point.RowId)))
        {
            var transient = transients.GetRowOrDefault(point.RowId);
            if (transient is not { } row)
                continue;

            if (row.GatheringRarePopTimeTable.RowId == 0)
            {
                AddWindow(windows, row.EphemeralStartTime, row.EphemeralEndTime);
                continue;
            }

            var timeTable = row.GatheringRarePopTimeTable.Value;
            foreach (var (durationValue, startValue) in timeTable.Duration.Zip(timeTable.StartTime))
            {
                if (durationValue == 0)
                    continue;

                var duration = durationValue == 160 ? 200 : durationValue;
                AddWindow(windows, startValue, (ushort)((startValue + duration) % 2400));
            }
        }

        return windows.Distinct().OrderBy(window => window.StartMinute).ToList();
    }

    private static void AddWindow(
        ICollection<EorzeaNodeWindow> windows,
        ushort startValue,
        ushort endValue)
    {
        var start = TimeValueToMinute(startValue);
        var end = TimeValueToMinute(endValue);
        if (start != end)
            windows.Add(new EorzeaNodeWindow(start, end));
    }

    private static int TimeValueToMinute(ushort value) =>
        (((value / 100) % 24) * 60) + Math.Min(value % 100, (ushort)59);

    private static double GetCurrentEorzeaMinute()
    {
        var totalMinutes =
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 12d / 35_000d;
        return (totalMinutes % 1440d + 1440d) % 1440d;
    }

    private static double GetWaitMinutes(
        AetherialReductionSource source,
        double currentMinute)
    {
        if (source.Windows.Count == 0)
            return 1441d;
        if (source.Windows.Any(window => GetRemainingMinutes(window, currentMinute) > 0))
            return 0d;
        return source.Windows.Min(window => ForwardMinutes(currentMinute, window.StartMinute));
    }

    private static double GetWindowAvailabilitySortKey(
        IReadOnlyList<EorzeaNodeWindow> windows,
        double currentMinute)
    {
        var shortestRemaining = windows
            .Select(window => GetRemainingMinutes(window, currentMinute))
            .Where(remaining => remaining > 0)
            .DefaultIfEmpty()
            .Min();
        if (shortestRemaining > 0)
            return -2000d + shortestRemaining;

        return windows.Min(window => ForwardMinutes(currentMinute, window.StartMinute));
    }

    private static double GetRemainingMinutes(
        EorzeaNodeWindow window,
        double currentMinute)
    {
        var duration = ForwardMinutes(window.StartMinute, window.EndMinute);
        var elapsed = ForwardMinutes(window.StartMinute, currentMinute);
        return elapsed < duration ? duration - elapsed : 0d;
    }

    private static double ForwardMinutes(double from, double to) =>
        (to - from + 1440d) % 1440d;

    private static string FormatRealDuration(double eorzeaMinutes)
    {
        var realSeconds = (int)Math.Ceiling(eorzeaMinutes * 35d / 12d);
        var duration = TimeSpan.FromSeconds(realSeconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds:D2}s"
            : duration.TotalMinutes >= 1
                ? $"{duration.Minutes}m {duration.Seconds:D2}s"
                : $"{Math.Max(1, duration.Seconds)}s";
    }

    private sealed record ReductionMapping(
        uint ResultItemId,
        uint SourceItemId,
        string SourceName,
        bool IsFishing);
}
