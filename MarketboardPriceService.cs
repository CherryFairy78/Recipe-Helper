using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace DalamudRecipeHelper;

public sealed class MarketboardPriceService
{
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(8),
    };

    private readonly FileLogService fileLog;
    private readonly ConcurrentDictionary<uint, CacheEntry> cache = [];
    private const string Scope = "Oceania";
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    public MarketboardPriceService(FileLogService fileLog)
    {
        this.fileLog = fileLog;
    }

    public MarketboardPriceState GetState(uint itemId)
    {
        var entry = this.cache.GetOrAdd(itemId, static _ => new CacheEntry());
        lock (entry.SyncRoot)
        {
            if (entry.IsMarketboardAvailable is false &&
                DateTime.UtcNow - entry.FetchedAtUtc < CacheLifetime)
            {
                return new MarketboardPriceState(null, false, true, false);
            }

            if (entry.Snapshot is { } snapshot &&
                DateTime.UtcNow - entry.FetchedAtUtc < CacheLifetime)
            {
                return new MarketboardPriceState(snapshot, false, false, true);
            }

            if (entry.LoadingTask is null || entry.LoadingTask.IsCompleted)
                entry.LoadingTask = this.LoadSnapshotAsync(itemId, entry);

            return new MarketboardPriceState(
                entry.Snapshot,
                true,
                entry.Snapshot is null,
                entry.IsMarketboardAvailable);
        }
    }

    private async Task LoadSnapshotAsync(uint itemId, CacheEntry entry)
    {
        try
        {
            var uri = $"https://universalis.app/api/v2/{Scope}/{itemId}?listings=80&entries=0";
            var json = await HttpClient.GetStringAsync(uri);
            var response = JsonSerializer.Deserialize<UniversalisResponse>(json);
            if (response is null)
                return;

            if (!response.HasData)
            {
                lock (entry.SyncRoot)
                {
                    entry.IsMarketboardAvailable = false;
                    entry.FetchedAtUtc = DateTime.UtcNow;
                }

                return;
            }

            var nqWorldPrices = (response.Listings ?? [])
                .Where(listing => !listing.Hq)
                .GroupBy(listing => listing.WorldName ?? "Unknown")
                .Select(group =>
                {
                    var cheapest = group
                        .OrderBy(listing => listing.PricePerUnit)
                        .First();
                    return new WorldPriceSnapshot(group.Key, cheapest.PricePerUnit);
                })
                .OrderBy(world => world.PricePerUnit)
                .ThenBy(world => world.WorldName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hqWorldPrices = (response.Listings ?? [])
                .Where(listing => listing.Hq)
                .GroupBy(listing => listing.WorldName ?? "Unknown")
                .Select(group =>
                {
                    var cheapest = group
                        .OrderBy(listing => listing.PricePerUnit)
                        .First();
                    return new WorldPriceSnapshot(group.Key, cheapest.PricePerUnit);
                })
                .OrderBy(world => world.PricePerUnit)
                .ThenBy(world => world.WorldName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (entry.SyncRoot)
            {
                entry.Snapshot = new MarketboardPriceSnapshot(
                    response.RegionName ?? Scope,
                    response.MinPrice,
                    response.MinPriceHq > 0 ? response.MinPriceHq : null,
                    response.CurrentAveragePrice,
                    response.CurrentAveragePriceHq > 0 ? response.CurrentAveragePriceHq : null,
                    nqWorldPrices,
                    hqWorldPrices,
                    response.LastUploadTime > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(response.LastUploadTime).ToLocalTime()
                        : null);
                entry.IsMarketboardAvailable = true;
                entry.FetchedAtUtc = DateTime.UtcNow;
            }
        }
        catch (Exception ex)
        {
            this.fileLog.Warning("Marketboard", $"Universalis lookup failed for item {itemId}: {ex.Message}");
        }
    }

    public readonly record struct MarketboardPriceState(
        MarketboardPriceSnapshot? Snapshot,
        bool IsLoading,
        bool HasNoCachedData,
        bool? IsMarketboardAvailable);

    public sealed record MarketboardPriceSnapshot(
        string Scope,
        int MinPrice,
        int? MinPriceHq,
        double CurrentAveragePrice,
        double? CurrentAveragePriceHq,
        IReadOnlyList<WorldPriceSnapshot> NqWorldPrices,
        IReadOnlyList<WorldPriceSnapshot> HqWorldPrices,
        DateTimeOffset? LastUploadTime);

    public sealed record WorldPriceSnapshot(
        string WorldName,
        int PricePerUnit);

    private sealed class CacheEntry
    {
        public object SyncRoot { get; } = new();

        public Task? LoadingTask { get; set; }

        public MarketboardPriceSnapshot? Snapshot { get; set; }

        public bool? IsMarketboardAvailable { get; set; }

        public DateTime FetchedAtUtc { get; set; }
    }

    private sealed class UniversalisResponse
    {
        [JsonPropertyName("regionName")]
        public string? RegionName { get; set; }

        [JsonPropertyName("minPrice")]
        public int MinPrice { get; set; }

        [JsonPropertyName("minPriceHQ")]
        public int MinPriceHq { get; set; }

        [JsonPropertyName("currentAveragePrice")]
        public double CurrentAveragePrice { get; set; }

        [JsonPropertyName("currentAveragePriceHQ")]
        public double CurrentAveragePriceHq { get; set; }

        [JsonPropertyName("lastUploadTime")]
        public long LastUploadTime { get; set; }

        [JsonPropertyName("hasData")]
        public bool HasData { get; set; }

        [JsonPropertyName("listings")]
        public ListingResponse[]? Listings { get; set; }
    }

    private sealed class ListingResponse
    {
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("pricePerUnit")]
        public int PricePerUnit { get; set; }

        [JsonPropertyName("hq")]
        public bool Hq { get; set; }
    }
}
