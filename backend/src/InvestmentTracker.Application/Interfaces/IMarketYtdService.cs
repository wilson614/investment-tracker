using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Service for calculating Market YTD (Year-to-Date) benchmark returns
/// </summary>
public interface IMarketYtdService
{
    /// <summary>
    /// Get YTD comparison for all benchmark ETFs
    /// Auto-fetches missing year-end prices from Stooq/TWSE if needed
    /// </summary>
    Task<MarketYtdComparisonDto> GetYtdComparisonAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Refresh current prices for all benchmarks
    /// </summary>
    Task<MarketYtdComparisonDto> RefreshYtdComparisonAsync(CancellationToken cancellationToken = default);
}
