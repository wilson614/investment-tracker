using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for CurrencyTransaction entity.
/// </summary>
public class CurrencyTransactionRepository : ICurrencyTransactionRepository
{
    private readonly AppDbContext _context;

    public CurrencyTransactionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CurrencyTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyTransactions
            .FirstOrDefaultAsync(ct => ct.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdAsync(Guid ledgerId, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyTransactions
            .Where(ct => ct.CurrencyLedgerId == ledgerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyTransaction>> GetByLedgerIdOrderedAsync(Guid ledgerId, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyTransactions
            .Where(ct => ct.CurrencyLedgerId == ledgerId)
            .OrderBy(ct => ct.TransactionDate)
            .ThenBy(ct => ct.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<CurrencyTransaction> AddAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default)
    {
        await _context.CurrencyTransactions.AddAsync(transaction, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task UpdateAsync(CurrencyTransaction transaction, CancellationToken cancellationToken = default)
    {
        _context.CurrencyTransactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.CurrencyTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(ct => ct.Id == id, cancellationToken);

        if (transaction != null)
        {
            transaction.MarkAsDeleted();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyTransactions.AnyAsync(ct => ct.Id == id, cancellationToken);
    }
}
