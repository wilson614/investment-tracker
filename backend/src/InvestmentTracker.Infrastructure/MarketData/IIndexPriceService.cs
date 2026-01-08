namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching market index prices for CAPE adjustment
/// </summary>
public interface IIndexPriceService
{
    /// <summary>
    /// Get current and historical index prices for CAPE adjustment
    /// </summary>
    Task<IndexPriceData?> GetIndexPricesAsync(string marketKey, DateTime referenceDate, CancellationToken cancellationToken = default);
}

/// <summary>
/// Index price data for CAPE adjustment calculation
/// </summary>
public record IndexPriceData(
    string MarketKey,
    string IndexSymbol,
    decimal CurrentPrice,
    decimal ReferencePrice,
    DateTime ReferenceDate,
    DateTime FetchedAt
);
