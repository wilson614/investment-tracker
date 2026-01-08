namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching real-time index prices from Google Finance
/// </summary>
public interface IGoogleFinanceService
{
    /// <summary>
    /// Get current price for a market index
    /// </summary>
    Task<decimal?> GetCurrentPriceAsync(string marketKey, CancellationToken cancellationToken = default);
}
