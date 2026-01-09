using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Repository interface for StockSplit entity.
/// </summary>
public interface IStockSplitRepository
{
    Task<StockSplit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockSplit>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockSplit>> GetBySymbolAsync(string symbol, StockMarket market, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockSplit>> GetSplitsAfterDateAsync(string symbol, StockMarket market, DateTime date, CancellationToken cancellationToken = default);
    Task<StockSplit> AddAsync(StockSplit split, CancellationToken cancellationToken = default);
    Task UpdateAsync(StockSplit split, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(string symbol, StockMarket market, DateTime splitDate, CancellationToken cancellationToken = default);
}
