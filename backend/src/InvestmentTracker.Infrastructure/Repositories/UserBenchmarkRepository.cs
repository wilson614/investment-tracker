using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// UserBenchmark Repository 實作
/// </summary>
public class UserBenchmarkRepository(AppDbContext context) : IUserBenchmarkRepository
{
    public async Task<IEnumerable<UserBenchmark>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await context.UserBenchmarks
            .Where(b => b.UserId == userId)
            .OrderBy(b => b.Ticker)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserBenchmark?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await context.UserBenchmarks.FindAsync([id], cancellationToken);
    }

    public async Task<UserBenchmark?> FindByUserTickerMarketAsync(Guid userId, string ticker, int market, CancellationToken cancellationToken = default)
    {
        return await context.UserBenchmarks
            .FirstOrDefaultAsync(b => b.UserId == userId && b.Ticker == ticker && (int)b.Market == market, cancellationToken);
    }

    public async Task AddAsync(UserBenchmark benchmark, CancellationToken cancellationToken = default)
    {
        context.UserBenchmarks.Add(benchmark);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(UserBenchmark benchmark, CancellationToken cancellationToken = default)
    {
        context.UserBenchmarks.Remove(benchmark);
        await context.SaveChangesAsync(cancellationToken);
    }
}
