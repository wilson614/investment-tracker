using InvestmentTracker.Application.DTOs;

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
    Task<YearEndPriceResult?> GetOrFetchYearEndPriceAsync(
        string ticker,
        int year,
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
