using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Service for calculating Market YTD (Year-to-Date) benchmark returns
/// </summary>
public interface IMarketYtdService
{
    /// <summary>
    /// Get YTD comparison for all benchmark ETFs
    /// </summary>
    Task<MarketYtdComparisonDto> GetYtdComparisonAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh current prices for all benchmarks
    /// </summary>
    Task<MarketYtdComparisonDto> RefreshYtdComparisonAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Store Jan 1 reference price for a benchmark
    /// </summary>
    Task StoreJan1PriceAsync(string marketKey, decimal price, CancellationToken cancellationToken = default);
}
