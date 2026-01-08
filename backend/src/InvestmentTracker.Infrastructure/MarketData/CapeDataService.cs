using System.Net.Http.Json;
using System.Text.Json;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching CAPE data from Research Affiliates API
/// Stores data in database - fetches new data only when current month's data becomes available
/// </summary>
public class CapeDataService : ICapeDataService
{
    private readonly HttpClient _httpClient;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<CapeDataService> _logger;

    private const string BaseUrl = "https://interactive.researchaffiliates.com/asset-allocation-data";

    public CapeDataService(
        HttpClient httpClient,
        AppDbContext dbContext,
        ILogger<CapeDataService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<CapeDataResponse?> GetCapeDataAsync(CancellationToken cancellationToken = default)
    {
        // Get the latest snapshot from database
        var snapshot = await _dbContext.CapeDataSnapshots
            .OrderByDescending(s => s.DataDate)
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot != null)
        {
            // Parse the data date to check if we need new data
            var shouldCheckForNew = ShouldCheckForNewData(snapshot.DataDate, snapshot.FetchedAt);

            if (shouldCheckForNew)
            {
                // Try to get newer data, but return existing if fetch fails
                var newData = await TryFetchNewerDataAsync(snapshot.DataDate, cancellationToken);
                if (newData != null)
                {
                    return newData;
                }
            }

            return DeserializeSnapshot(snapshot);
        }

        // No data in database, fetch fresh
        return await FetchAndSaveAsync(cancellationToken);
    }

    /// <summary>
    /// Determine if we should check for new CAPE data
    /// - If current data is from this month: no need to check
    /// - If current data is from previous month: check once per day
    /// </summary>
    private static bool ShouldCheckForNewData(string dataDate, DateTime lastFetchedAt)
    {
        var now = DateTime.UtcNow;
        var currentYearMonth = $"{now.Year}-{now.Month:D2}";

        // Extract year-month from data date (format: "2026-01-02")
        var dataYearMonth = dataDate.Substring(0, 7);

        // If we already have current month's data, no need to check
        if (dataYearMonth == currentYearMonth)
        {
            return false;
        }

        // Data is from previous month - check once per day
        return (now - lastFetchedAt) > TimeSpan.FromDays(1);
    }

    public async Task<CapeDataResponse?> RefreshCapeDataAsync(CancellationToken cancellationToken = default)
    {
        return await FetchAndSaveAsync(cancellationToken);
    }

    private async Task<CapeDataResponse?> TryFetchNewerDataAsync(string currentDataDate, CancellationToken cancellationToken)
    {
        var result = await DiscoverLatestCapeDataAsync(cancellationToken);
        if (result == null) return null;

        // Only save if we found newer data
        if (string.Compare(result.Date, currentDataDate, StringComparison.Ordinal) > 0)
        {
            await SaveSnapshotAsync(result, cancellationToken);
            return result;
        }

        // Update FetchedAt to avoid checking again soon
        var snapshot = await _dbContext.CapeDataSnapshots
            .FirstOrDefaultAsync(s => s.DataDate == currentDataDate, cancellationToken);
        if (snapshot != null)
        {
            snapshot.FetchedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return null;
    }

    private async Task<CapeDataResponse?> FetchAndSaveAsync(CancellationToken cancellationToken)
    {
        var result = await DiscoverLatestCapeDataAsync(cancellationToken);
        if (result == null)
        {
            _logger.LogWarning("Failed to discover CAPE data");
            return null;
        }

        await SaveSnapshotAsync(result, cancellationToken);
        return result;
    }

    private async Task SaveSnapshotAsync(CapeDataResponse data, CancellationToken cancellationToken)
    {
        // Check if we already have this date
        var existing = await _dbContext.CapeDataSnapshots
            .FirstOrDefaultAsync(s => s.DataDate == data.Date, cancellationToken);

        if (existing != null)
        {
            existing.ItemsJson = JsonSerializer.Serialize(data.Items);
            existing.FetchedAt = DateTime.UtcNow;
        }
        else
        {
            var snapshot = new CapeDataSnapshot
            {
                DataDate = data.Date,
                ItemsJson = JsonSerializer.Serialize(data.Items),
                FetchedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.CapeDataSnapshots.Add(snapshot);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Saved CAPE data snapshot for date {Date}", data.Date);
    }

    private static CapeDataResponse DeserializeSnapshot(CapeDataSnapshot snapshot)
    {
        var items = JsonSerializer.Deserialize<List<CapeDataItem>>(snapshot.ItemsJson) ?? [];
        return new CapeDataResponse(snapshot.DataDate, items, snapshot.FetchedAt);
    }

    /// <summary>
    /// Discover the latest available CAPE data
    /// Try current month first (days 1-10), then previous month
    /// </summary>
    private async Task<CapeDataResponse?> DiscoverLatestCapeDataAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var year = now.Year;
        var month = now.Month;

        // Try only 2 months: current month and previous month
        for (var monthIndex = 0; monthIndex < 2; monthIndex++)
        {
            // Try days 1-10 in ascending order
            for (var day = 1; day <= 10; day++)
            {
                var items = await TryFetchForDateAsync(year, month, day, cancellationToken);
                if (items != null)
                {
                    var date = $"{year}-{month:D2}-{day:D2}";
                    _logger.LogInformation("Found CAPE data for {Date}", date);
                    return new CapeDataResponse(date, items, DateTime.UtcNow);
                }
            }

            // Move to previous month
            month--;
            if (month == 0)
            {
                month = 12;
                year--;
            }
        }

        return null;
    }

    private async Task<List<CapeDataItem>?> TryFetchForDateAsync(
        int year, int month, int day, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}/{year}{month:D2}/{day:D2}/boxplot/boxplot_shillerpe.json";

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var rawItems = await response.Content.ReadFromJsonAsync<List<RawCapeItem>>(jsonOptions, cancellationToken);
            if (rawItems == null || rawItems.Count == 0)
            {
                return null;
            }

            return rawItems.Select(item => new CapeDataItem(
                item.BoxName ?? "",
                item.CurrentValue,
                item.CurrentValuePercentile,
                item.Range25th,
                item.Range50th,
                item.Range75th
            )).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to fetch CAPE data for {Year}-{Month:D2}-{Day:D2}", year, month, day);
            return null;
        }
    }

    private record RawCapeItem(
        string? BoxName,
        decimal CurrentValue,
        decimal CurrentValuePercentile,
        decimal Range25th,
        decimal Range50th,
        decimal Range75th
    );
}
