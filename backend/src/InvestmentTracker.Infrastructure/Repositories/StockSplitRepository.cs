using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// StockSplit entity 的 Repository 實作。
/// </summary>
public class StockSplitRepository(AppDbContext context) : IStockSplitRepository
{
    private readonly AppDbContext _context = context;

    public async Task<StockSplit?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.StockSplits
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<StockSplit>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.StockSplits
            .OrderBy(s => s.Symbol)
            .ThenByDescending(s => s.SplitDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockSplit>> GetBySymbolAsync(
        string symbol,
        StockMarket market,
        CancellationToken cancellationToken = default)
    {
        return await _context.StockSplits
            .Where(s => s.Symbol == symbol.ToUpperInvariant() && s.Market == market)
            .OrderByDescending(s => s.SplitDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockSplit>> GetSplitsAfterDateAsync(
        string symbol,
        StockMarket market,
        DateTime date,
        CancellationToken cancellationToken = default)
    {
        return await _context.StockSplits
            .Where(s => s.Symbol == symbol.ToUpperInvariant()
                        && s.Market == market
                        && s.SplitDate > date.Date)
            .OrderBy(s => s.SplitDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<StockSplit> AddAsync(StockSplit split, CancellationToken cancellationToken = default)
    {
        await _context.StockSplits.AddAsync(split, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return split;
    }

    public async Task UpdateAsync(StockSplit split, CancellationToken cancellationToken = default)
    {
        _context.StockSplits.Update(split);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var split = await GetByIdAsync(id, cancellationToken);
        if (split != null)
        {
            _context.StockSplits.Remove(split);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> ExistsAsync(
        string symbol,
        StockMarket market,
        DateTime splitDate,
        CancellationToken cancellationToken = default)
    {
        return await _context.StockSplits
            .AnyAsync(s => s.Symbol == symbol.ToUpperInvariant()
                           && s.Market == market
                           && s.SplitDate == splitDate.Date, cancellationToken);
    }
}
