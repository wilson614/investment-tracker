using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// CurrencyLedger 彙總根（aggregate）的 Repository 介面。
/// </summary>
public interface ICurrencyLedgerRepository
{
    Task<CurrencyLedger?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CurrencyLedger?> GetByIdWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CurrencyLedger>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<CurrencyLedger?> GetByCurrencyCodeAsync(Guid userId, string currencyCode, CancellationToken cancellationToken = default);
    Task<CurrencyLedger> AddAsync(CurrencyLedger ledger, CancellationToken cancellationToken = default);
    Task UpdateAsync(CurrencyLedger ledger, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByCurrencyCodeAsync(Guid userId, string currencyCode, CancellationToken cancellationToken = default);
}
