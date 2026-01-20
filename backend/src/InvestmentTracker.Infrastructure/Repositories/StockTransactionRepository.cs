using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// StockTransaction entity 的 Repository 實作。
/// </summary>
public class StockTransactionRepository(Persistence.AppDbContext context) : IStockTransactionRepository
{
    public async Task<StockTransaction?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.StockTransactions
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StockTransaction>> GetByPortfolioIdAsync(
        Guid portfolioId, CancellationToken cancellationToken = default)
    {
        return await context.StockTransactions
            .Where(t => t.PortfolioId == portfolioId)
            .OrderByDescending(t => t.TransactionDate)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockTransaction>> GetByTickerAsync(
        Guid portfolioId, string ticker, CancellationToken cancellationToken = default)
    {
        return await context.StockTransactions
            .Where(t => t.PortfolioId == portfolioId && t.Ticker == ticker)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<StockTransaction> AddAsync(StockTransaction transaction, CancellationToken cancellationToken = default)
    {
        await context.StockTransactions.AddAsync(transaction, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return transaction;
    }

    public async Task UpdateAsync(StockTransaction transaction, CancellationToken cancellationToken = default)
    {
        context.StockTransactions.Update(transaction);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var transaction = await context.StockTransactions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

        if (transaction != null)
        {
            transaction.MarkAsDeleted();
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.StockTransactions.AnyAsync(t => t.Id == id, cancellationToken);
    }
}
