using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Interface for managing historical year-end data cache.
/// Provides lazy loading: check cache first, fetch from API if missing, save to cache.
/// </summary>
public interface IHistoricalYearEndDataService
{
    /// <summary>
    /// Gets year-end stock price from cache, or fetches from API and caches it.
    /// Returns null if price cannot be obtained (requires manual entry).
    /// </summary>
    /// <param name="ticker">Stock ticker symbol</param>
    /// <param name="year">Year for the year-end price</param>
    /// <param name="market">Stock market (used to determine data source; EU market requires manual entry)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<YearEndPriceResult?> GetOrFetchYearEndPriceAsync(
        string ticker,
        int year,
        StockMarket? market = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets year-end exchange rate from cache, or fetches from API and caches it.
    /// Returns null if rate cannot be obtained (requires manual entry).
    /// </summary>
    Task<YearEndExchangeRateResult?> GetOrFetchYearEndExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually saves a year-end price. Only allowed when no cache entry exists.
    /// </summary>
    Task<YearEndPriceResult> SaveManualPriceAsync(
        string ticker,
        int year,
        decimal price,
        string currency,
        DateTime actualDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually saves a year-end exchange rate. Only allowed when no cache entry exists.
    /// </summary>
    Task<YearEndExchangeRateResult> SaveManualExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        int year,
        decimal rate,
        DateTime actualDate,
        CancellationToken cancellationToken = default);
}
