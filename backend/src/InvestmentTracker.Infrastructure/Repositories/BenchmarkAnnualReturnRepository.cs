using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// BenchmarkAnnualReturn Repository 實作。
/// 這是一個跨使用者共用的全域快取。
/// </summary>
public class BenchmarkAnnualReturnRepository(AppDbContext context) : IBenchmarkAnnualReturnRepository
{
    public async Task<BenchmarkAnnualReturn?> GetAsync(
        string symbol,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalized = symbol.Trim().ToUpperInvariant();

        return await context.BenchmarkAnnualReturns
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Symbol == normalized && b.Year == year, cancellationToken);
    }

    public async Task<bool> ExistsAsync(
        string symbol,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalized = symbol.Trim().ToUpperInvariant();

        return await context.BenchmarkAnnualReturns
            .AsNoTracking()
            .AnyAsync(b => b.Symbol == normalized && b.Year == year, cancellationToken);
    }

    public async Task<BenchmarkAnnualReturn> AddAsync(
        BenchmarkAnnualReturn data,
        CancellationToken cancellationToken = default)
    {
        // 避免並發寫入造成 unique index 例外：若已存在就直接回傳。
        var existing = await GetAsync(data.Symbol, data.Year, cancellationToken);
        if (existing != null)
            return existing;

        context.BenchmarkAnnualReturns.Add(data);

        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Likely unique constraint conflict (race). Return the existing value.
            var raced = await GetAsync(data.Symbol, data.Year, cancellationToken);
            if (raced != null)
                return raced;

            throw;
        }

        return data;
    }
}
