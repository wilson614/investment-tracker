namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching CAPE (Cyclically Adjusted P/E) data from Research Affiliates
/// </summary>
public interface ICapeDataService
{
    /// <summary>
    /// Get the latest CAPE data, using cache if available
    /// </summary>
    Task<CapeDataResponse?> GetCapeDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear the cache and fetch fresh data
    /// </summary>
    Task<CapeDataResponse?> RefreshCapeDataAsync(CancellationToken cancellationToken = default);
}

public record CapeDataResponse(
    string Date,
    List<CapeDataItem> Items,
    DateTime FetchedAt
);

public record CapeDataItem(
    string BoxName,
    decimal CurrentValue,
    decimal CurrentValuePercentile,
    decimal Range25th,
    decimal Range50th,
    decimal Range75th
);
