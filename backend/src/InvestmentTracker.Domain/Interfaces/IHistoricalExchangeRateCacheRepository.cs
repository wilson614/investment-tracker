using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Repository interface for HistoricalExchangeRateCache operations.
/// This is a global cache (not per-user) for transaction-date exchange rates.
/// </summary>
public interface IHistoricalExchangeRateCacheRepository
{
    /// <summary>
    /// Gets a cached exchange rate for a specific currency pair and date.
    /// </summary>
    Task<HistoricalExchangeRateCache?> GetAsync(
        string currencyPair,
        DateTime requestedDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a cached exchange rate for a specific from/to currency and date.
    /// </summary>
    Task<HistoricalExchangeRateCache?> GetAsync(
        string fromCurrency,
        string toCurrency,
        DateTime requestedDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all cached entries for a specific currency pair.
    /// </summary>
    Task<IReadOnlyList<HistoricalExchangeRateCache>> GetByCurrencyPairAsync(
        string currencyPair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new cache entry. Throws if entry already exists (cache is immutable).
    /// </summary>
    Task<HistoricalExchangeRateCache> AddAsync(
        HistoricalExchangeRateCache data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a cache entry exists for the given parameters.
    /// </summary>
    Task<bool> ExistsAsync(
        string currencyPair,
        DateTime requestedDate,
        CancellationToken cancellationToken = default);
}
