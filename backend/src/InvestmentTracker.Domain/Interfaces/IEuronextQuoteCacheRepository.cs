using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Repository interface for Euronext quote cache operations.
/// </summary>
public interface IEuronextQuoteCacheRepository
{
    /// <summary>
    /// Gets a cached quote by ISIN and MIC.
    /// </summary>
    Task<EuronextQuoteCache?> GetByIsinAndMicAsync(string isin, string mic, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates a quote cache entry.
    /// </summary>
    Task UpsertAsync(EuronextQuoteCache quoteCache, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a quote as stale.
    /// </summary>
    Task MarkAsStaleAsync(string isin, string mic, CancellationToken cancellationToken = default);
}
