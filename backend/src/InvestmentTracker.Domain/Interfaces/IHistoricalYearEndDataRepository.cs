using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Repository interface for HistoricalYearEndData cache operations.
/// This is a global cache (not per-user) for year-end stock prices and exchange rates.
/// </summary>
public interface IHistoricalYearEndDataRepository
{
    /// <summary>
    /// Gets a cached year-end data entry by type, ticker, and year.
    /// </summary>
    Task<HistoricalYearEndData?> GetAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a cached stock price for a specific ticker and year.
    /// </summary>
    Task<HistoricalYearEndData?> GetStockPriceAsync(
        string ticker,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a cached exchange rate for a specific currency pair and year.
    /// </summary>
    Task<HistoricalYearEndData?> GetExchangeRateAsync(
        string currencyPair,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all cached entries for a specific year.
    /// </summary>
    Task<IReadOnlyList<HistoricalYearEndData>> GetByYearAsync(
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new cache entry. Throws if entry already exists (cache is immutable).
    /// </summary>
    Task<HistoricalYearEndData> AddAsync(
        HistoricalYearEndData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a cache entry exists for the given parameters.
    /// </summary>
    Task<bool> ExistsAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default);
}
