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
    private readonly IIndexPriceService _indexPriceService;
    private readonly ILogger<CapeDataService> _logger;

    private const string BaseUrl = "https://interactive.researchaffiliates.com/asset-allocation-data";

    public CapeDataService(
        HttpClient httpClient,
        AppDbContext dbContext,
        IIndexPriceService indexPriceService,
        ILogger<CapeDataService> logger)
    {
        _httpClient = httpClient;
        _dbContext = dbContext;
        _indexPriceService = indexPriceService;
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
                    return await ApplyRealTimeAdjustmentsAsync(newData, cancellationToken);
                }
            }

            var data = DeserializeSnapshot(snapshot);
            return await ApplyRealTimeAdjustmentsAsync(data, cancellationToken);
        }

        // No data in database, fetch fresh
        var freshData = await FetchAndSaveAsync(cancellationToken);
        if (freshData != null)
        {
            return await ApplyRealTimeAdjustmentsAsync(freshData, cancellationToken);
        }
        return null;
    }

    /// <summary>
    /// Apply real-time adjustments to CAPE values based on current index prices
    /// </summary>
    private async Task<CapeDataResponse> ApplyRealTimeAdjustmentsAsync(
        CapeDataResponse data,
        CancellationToken cancellationToken)
    {
        // Parse the data date to get the reference date for historical prices
        // API date format: "2026-01-02" means data is as of end of previous month (2025-12-31)
        if (!DateTime.TryParse(data.Date, out var apiDate))
        {
            return data;
        }

        // The CAPE data is for the previous month's end
        var referenceDate = new DateTime(apiDate.Year, apiDate.Month, 1).AddDays(-1);

        var adjustedItems = new List<CapeDataItem>();

        foreach (var item in data.Items)
        {
            var adjustedValue = await CalculateAdjustedValueAsync(
                item.BoxName,
                item.CurrentValue,
                referenceDate,
                cancellationToken);

            adjustedItems.Add(item with { AdjustedValue = adjustedValue });
        }

        return data with { Items = adjustedItems };
    }

    /// <summary>
    /// Calculate adjusted CAPE value based on current vs reference index price
    /// </summary>
    private async Task<decimal?> CalculateAdjustedValueAsync(
        string marketKey,
        decimal originalValue,
        DateTime referenceDate,
        CancellationToken cancellationToken)
    {
        // Check if this market supports adjustment
        if (!IndexPriceService.SupportedMarkets.Contains(marketKey))
        {
            return null;
        }

        try
        {
            var indexPrices = await _indexPriceService.GetIndexPricesAsync(
                marketKey,
                referenceDate,
                cancellationToken);

            if (indexPrices == null || indexPrices.CurrentPrice == 0 || indexPrices.ReferencePrice == 0)
            {
                _logger.LogDebug("Could not get index prices for {Market}", marketKey);
                return null;
            }

            // Adjusted CAPE = Original CAPE × (Current Index / Reference Index)
            var adjustmentRatio = indexPrices.CurrentPrice / indexPrices.ReferencePrice;
            var adjustedValue = originalValue * adjustmentRatio;

            _logger.LogDebug(
                "Adjusted {Market} CAPE: {Original} × ({Current}/{Reference}) = {Adjusted}",
                marketKey, originalValue, indexPrices.CurrentPrice, indexPrices.ReferencePrice, adjustedValue);

            return Math.Round(adjustedValue, 2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate adjusted CAPE for {Market}", marketKey);
            return null;
        }
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
        var data = await FetchAndSaveAsync(cancellationToken);
        if (data != null)
        {
            return await ApplyRealTimeAdjustmentsAsync(data, cancellationToken);
        }
        return null;
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
        try
        {
            // Check if we already have this date
            var existing = await _dbContext.CapeDataSnapshots
                .FirstOrDefaultAsync(s => s.DataDate == data.Date, cancellationToken);

            // Store items without AdjustedValue (it's calculated on-the-fly)
            var itemsToStore = data.Items.Select(i => new StoredCapeItem(
                i.BoxName, i.CurrentValue, i.CurrentValuePercentile,
                i.Range25th, i.Range50th, i.Range75th
            )).ToList();

            if (existing != null)
            {
                existing.ItemsJson = JsonSerializer.Serialize(itemsToStore);
                existing.FetchedAt = DateTime.UtcNow;
            }
            else
            {
                var snapshot = new CapeDataSnapshot
                {
                    DataDate = data.Date,
                    ItemsJson = JsonSerializer.Serialize(itemsToStore),
                    FetchedAt = DateTime.UtcNow,
                    CreatedAt = DateTime.UtcNow
                };
                _dbContext.CapeDataSnapshots.Add(snapshot);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Saved CAPE data snapshot for date {Date}", data.Date);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Another concurrent request already inserted this record - that's fine
            _logger.LogDebug("Duplicate CAPE snapshot ignored for {Date} - already exists", data.Date);
        }
    }

    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        // PostgreSQL unique constraint violation: 23505
        // SQLite constraint violation: 19 (SQLITE_CONSTRAINT)
        return ex.InnerException?.Message?.Contains("23505") == true ||
               ex.InnerException?.Message?.Contains("UNIQUE constraint failed") == true;
    }

    private static CapeDataResponse DeserializeSnapshot(CapeDataSnapshot snapshot)
    {
        var storedItems = JsonSerializer.Deserialize<List<StoredCapeItem>>(snapshot.ItemsJson) ?? [];
        var items = storedItems.Select(i => new CapeDataItem(
            i.BoxName, i.CurrentValue, null, i.CurrentValuePercentile,
            i.Range25th, i.Range50th, i.Range75th
        )).ToList();
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
                null, // AdjustedValue will be calculated later
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

    // For storing in database without AdjustedValue
    private record StoredCapeItem(
        string BoxName,
        decimal CurrentValue,
        decimal CurrentValuePercentile,
        decimal Range25th,
        decimal Range50th,
        decimal Range75th
    );
}
