using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for CurrencyLedger aggregate.
/// </summary>
public class CurrencyLedgerRepository : ICurrencyLedgerRepository
{
    private readonly AppDbContext _context;

    public CurrencyLedgerRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<CurrencyLedger?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyLedgers
            .FirstOrDefaultAsync(cl => cl.Id == id, cancellationToken);
    }

    public async Task<CurrencyLedger?> GetByIdWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyLedgers
            .Include(cl => cl.Transactions.Where(t => !t.IsDeleted).OrderBy(t => t.TransactionDate))
            .FirstOrDefaultAsync(cl => cl.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<CurrencyLedger>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyLedgers
            .Where(cl => cl.UserId == userId)
            .OrderBy(cl => cl.CurrencyCode)
            .ToListAsync(cancellationToken);
    }

    public async Task<CurrencyLedger?> GetByCurrencyCodeAsync(Guid userId, string currencyCode, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyLedgers
            .FirstOrDefaultAsync(cl => cl.UserId == userId && cl.CurrencyCode == currencyCode.ToUpperInvariant(), cancellationToken);
    }

    public async Task<CurrencyLedger> AddAsync(CurrencyLedger ledger, CancellationToken cancellationToken = default)
    {
        await _context.CurrencyLedgers.AddAsync(ledger, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return ledger;
    }

    public async Task UpdateAsync(CurrencyLedger ledger, CancellationToken cancellationToken = default)
    {
        _context.CurrencyLedgers.Update(ledger);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var ledger = await GetByIdAsync(id, cancellationToken);
        if (ledger != null)
        {
            ledger.Deactivate();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyLedgers.AnyAsync(cl => cl.Id == id, cancellationToken);
    }

    public async Task<bool> ExistsByCurrencyCodeAsync(Guid userId, string currencyCode, CancellationToken cancellationToken = default)
    {
        return await _context.CurrencyLedgers
            .AnyAsync(cl => cl.UserId == userId && cl.CurrencyCode == currencyCode.ToUpperInvariant(), cancellationToken);
    }
}
