using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Repository interface for Portfolio aggregate.
/// </summary>
public interface IPortfolioRepository
{
    Task<Portfolio?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Portfolio?> GetByIdWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Portfolio>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Portfolio> AddAsync(Portfolio portfolio, CancellationToken cancellationToken = default);
    Task UpdateAsync(Portfolio portfolio, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
