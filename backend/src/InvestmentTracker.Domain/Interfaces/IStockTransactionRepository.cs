using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// StockTransaction Entity 的 Repository 介面。
/// </summary>
public interface IStockTransactionRepository
{
    Task<StockTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockTransaction>> GetByPortfolioIdAsync(Guid portfolioId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StockTransaction>> GetByTickerAsync(Guid portfolioId, string ticker, CancellationToken cancellationToken = default);
    Task<StockTransaction> AddAsync(StockTransaction transaction, CancellationToken cancellationToken = default);
    Task UpdateAsync(StockTransaction transaction, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
