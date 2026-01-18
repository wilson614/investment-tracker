namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Service for fetching historical prices for individual Taiwan stocks from TWSE.
/// Used for year-end price lookups when Stooq doesn't cover Taiwan stocks.
/// </summary>
public interface ITwseStockHistoricalPriceService
{
    /// <summary>
    /// Get the closing price for a Taiwan stock on a specific date.
    /// Returns the closing price for the nearest trading day on or before the specified date.
    /// </summary>
    /// <param name="stockNo">Taiwan stock number (e.g., "2330", "0050")</param>
    /// <param name="date">Target date</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Price result with actual trading date, or null if not found</returns>
    Task<TwseStockPriceResult?> GetStockPriceAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the year-end closing price for a Taiwan stock.
    /// Fetches the last trading day price of December for the given year.
    /// </summary>
    Task<TwseStockPriceResult?> GetYearEndPriceAsync(
        string stockNo,
        int year,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from TWSE stock price lookup
/// </summary>
public record TwseStockPriceResult(
    decimal Price,
    DateOnly ActualDate,
    string StockNo
);
