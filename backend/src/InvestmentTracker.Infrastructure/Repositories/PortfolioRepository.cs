using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Portfolio aggregate.
/// </summary>
public class PortfolioRepository : IPortfolioRepository
{
    private readonly Persistence.AppDbContext _context;

    public PortfolioRepository(Persistence.AppDbContext context)
    {
        _context = context;
    }

    public async Task<Portfolio?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Portfolios
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<Portfolio?> GetByIdWithTransactionsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Portfolios
            .Include(p => p.Transactions)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Portfolio>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _context.Portfolios
            .Where(p => p.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Portfolio> AddAsync(Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        await _context.Portfolios.AddAsync(portfolio, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return portfolio;
    }

    public async Task UpdateAsync(Portfolio portfolio, CancellationToken cancellationToken = default)
    {
        _context.Portfolios.Update(portfolio);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var portfolio = await GetByIdAsync(id, cancellationToken);
        if (portfolio != null)
        {
            portfolio.Deactivate();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Portfolios.AnyAsync(p => p.Id == id, cancellationToken);
    }
}
