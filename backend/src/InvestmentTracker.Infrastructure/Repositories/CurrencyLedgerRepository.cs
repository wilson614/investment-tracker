using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// CurrencyLedger aggregate 的 Repository 實作。
/// </summary>
public class CurrencyLedgerRepository(AppDbContext context) : ICurrencyLedgerRepository
{
    public async Task<CurrencyLedger?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyLedgers
            .FirstOrDefaultAsync(cl => cl.Id == id, cancellationToken);
    }

    public async Task<CurrencyLedger?> GetByIdWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyLedgers
            .Include(cl => cl.Transactions.Where(t => !t.IsDeleted).OrderBy(t => t.TransactionDate))
            .FirstOrDefaultAsync(cl => cl.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyLedger>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyLedgers
            .Where(cl => cl.UserId == userId)
            .OrderBy(cl => cl.CurrencyCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<CurrencyLedger?> GetByCurrencyCodeAsync(Guid userId, string currencyCode, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyLedgers
            .FirstOrDefaultAsync(cl => cl.UserId == userId && cl.CurrencyCode == currencyCode.ToUpperInvariant(), cancellationToken);
    }

    public async Task<CurrencyLedger> AddAsync(CurrencyLedger ledger, CancellationToken cancellationToken = default)
    {
        await context.CurrencyLedgers.AddAsync(ledger, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return ledger;
    }

    public async Task UpdateAsync(CurrencyLedger ledger, CancellationToken cancellationToken = default)
    {
        context.CurrencyLedgers.Update(ledger);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ledger = await GetByIdAsync(id, cancellationToken);
        if (ledger != null)
        {
            ledger.Deactivate();
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyLedgers.AnyAsync(cl => cl.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByCurrencyCodeAsync(Guid userId, string currencyCode, CancellationToken cancellationToken = default)
    {
        return await context.CurrencyLedgers
            .AnyAsync(cl => cl.UserId == userId && cl.CurrencyCode == currencyCode.ToUpperInvariant(), cancellationToken);
    }
}
