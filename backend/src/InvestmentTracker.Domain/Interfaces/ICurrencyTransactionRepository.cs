using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Repository interface for CurrencyTransaction entity.
/// </summary>
public interface ICurrencyTransactionRepository
{
    Task<CurrencyTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CurrencyTransaction?> GetByStockTransactionIdAsync(Guid stockTransactionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdAsync(Guid ledgerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdOrderedAsync(Guid ledgerId, CancellationToken cancellationToken = default);
    Task<CurrencyTransaction> AddAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default);
    Task UpdateAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default);
    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
}
