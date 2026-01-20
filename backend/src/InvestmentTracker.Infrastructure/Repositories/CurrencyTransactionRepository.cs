using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// CurrencyTransaction entity 的 Repository 實作。
/// </summary>
public class CurrencyTransactionRepository(AppDbContext context) : ICurrencyTransactionRepository
{
    public async Task<CurrencyTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyTransactions
            .FirstOrDefaultAsync(ct => ct.Id == id, cancellationToken);
    }

    public async Task<CurrencyTransaction?> GetByStockTransactionIdAsync(Guid stockTransactionId, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyTransactions
            .FirstOrDefaultAsync(ct => ct.RelatedStockTransactionId == stockTransactionId, cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdAsync(Guid ledgerId, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyTransactions
            .Where(ct => ct.CurrencyLedgerId == ledgerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdOrderedAsync(Guid ledgerId, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyTransactions
            .Where(ct => ct.CurrencyLedgerId == ledgerId)
            .OrderBy(ct => ct.TransactionDate)
            .ThenBy(ct => ct.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CurrencyTransaction> AddAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default)
    {
        await context.CurrencyTransactions.AddAsync(transaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task UpdateAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default)
    {
        context.CurrencyTransactions.Update(transaction);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await context.CurrencyTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id, cancellationToken);

        if (transaction != null)
        {
            transaction.MarkAsDeleted();
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyTransactions.AnyAsync(ct => ct.Id == id, cancellationToken);
    }
}
