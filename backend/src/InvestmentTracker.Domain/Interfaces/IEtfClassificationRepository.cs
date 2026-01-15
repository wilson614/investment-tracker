using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Repository interface for ETF classification operations.
/// </summary>
public interface IEtfClassificationRepository
{
    /// <summary>
    /// Gets an ETF classification by symbol and market.
    /// </summary>
    Task<EtfClassification?> GetBySymbolAndMarketAsync(string symbol, string market, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all ETF classifications.
    /// </summary>
    Task<IReadOnlyList<EtfClassification>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds or updates an ETF classification.
    /// </summary>
    Task UpsertAsync(EtfClassification classification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets ETF classifications for multiple symbols.
    /// </summary>
    Task<IReadOnlyList<EtfClassification>> GetBySymbolsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);
}
