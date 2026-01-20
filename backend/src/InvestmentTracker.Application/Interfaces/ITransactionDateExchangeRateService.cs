namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Result of fetching a transaction-date exchange rate.
/// </summary>
public record TransactionDateExchangeRateResult
{
    public decimal Rate { get; init; }
    public string CurrencyPair { get; init; } = string.Empty;
    public DateTime RequestedDate { get; init; }
    public DateTime ActualDate { get; init; }
    public string Source { get; init; } = string.Empty;
    public bool FromCache { get; set; }
}

/// <summary>
/// Service interface for managing historical exchange rate cache by transaction date.
/// Provides cache → Stooq → persist lazy loading pattern.
/// </summary>
public interface ITransactionDateExchangeRateService
{
    /// <summary>
    /// Gets exchange rate for a specific transaction date from cache, or fetches from Stooq and caches it.
    /// Returns null if rate cannot be obtained (requires manual entry).
    /// </summary>
    Task<TransactionDateExchangeRateResult?> GetOrFetchAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Manually saves an exchange rate for a transaction date.
    /// Only allowed when no cache entry exists.
    /// </summary>
    Task<TransactionDateExchangeRateResult> SaveManualAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        decimal rate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a cache entry exists for the given parameters.
    /// </summary>
    Task<bool> ExistsAsync(
        string fromCurrency,
        string toCurrency,
        DateTime transactionDate,
        CancellationToken cancellationToken = default);
}
