using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Portfolio aggregate 的 Repository 實作。
/// </summary>
public class PortfolioRepository(Persistence.AppDbContext context) : IPortfolioRepository
{
    public async Task<Portfolio?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Portfolios
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Portfolio?> GetByIdWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Portfolios
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Portfolio>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.Portfolios
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Portfolio> AddAsync(Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        await context.Portfolios.AddAsync(portfolio, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return portfolio;
    }

    public async Task UpdateAsync(Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        context.Portfolios.Update(portfolio);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var portfolio = await GetByIdAsync(id, cancellationToken);
        if (portfolio != null)
        {
            portfolio.Deactivate();
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.Portfolios.AnyAsync(p => p.Id == id, cancellationToken);
    }
}
