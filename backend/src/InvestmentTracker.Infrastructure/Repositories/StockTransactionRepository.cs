using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// StockTransaction entity 的 Repository 實作。
/// </summary>
public class StockTransactionRepository(Persistence.AppDbContext context) : IStockTransactionRepository
{
    private readonly Persistence.AppDbContext _context = context;

    public async Task<StockTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.StockTransactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StockTransaction>> GetByPortfolioIdAsync(
        Guid portfolioId, CancellationToken cancellationToken = default)
    {
        return await _context.StockTransactions
            .Where(t => t.PortfolioId == portfolioId)
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockTransaction>> GetByTickerAsync(
        Guid portfolioId, string ticker, CancellationToken cancellationToken = default)
    {
        return await _context.StockTransactions
            .Where(t => t.PortfolioId == portfolioId && t.Ticker == ticker)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<StockTransaction> AddAsync(StockTransaction transaction, CancellationToken cancellationToken = default)
    {
        await _context.StockTransactions.AddAsync(transaction, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task UpdateAsync(StockTransaction transaction, CancellationToken cancellationToken = default)
    {
        _context.StockTransactions.Update(transaction);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await _context.StockTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transaction != null)
        {
            transaction.MarkAsDeleted();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.StockTransactions.AnyAsync(t => t.Id == id, cancellationToken);
    }
}
