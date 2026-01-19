using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// HistoricalExchangeRateCache 的 Repository 實作。
/// 這是一個跨使用者共用的全域快取。
/// </summary>
public class HistoricalExchangeRateCacheRepository(AppDbContext context) : IHistoricalExchangeRateCacheRepository
{
    private readonly AppDbContext _context = context;

    public async Task<HistoricalExchangeRateCache?> GetAsync(
        string currencyPair,
        DateTime requestedDate,
        CancellationToken cancellationToken = default)
    {
        var normalizedPair = currencyPair.Trim().ToUpperInvariant();
        var dateOnly = requestedDate.Date;

        return await _context.HistoricalExchangeRateCaches
            .FirstOrDefaultAsync(
                d => d.CurrencyPair == normalizedPair && d.RequestedDate == dateOnly,
                cancellationToken);
    }

    public async Task<HistoricalExchangeRateCache?> GetAsync(
        string fromCurrency,
        string toCurrency,
        DateTime requestedDate,
        CancellationToken cancellationToken = default)
    {
        var currencyPair = $"{fromCurrency.Trim().ToUpperInvariant()}{toCurrency.Trim().ToUpperInvariant()}";
        return await GetAsync(currencyPair, requestedDate, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoricalExchangeRateCache>> GetByCurrencyPairAsync(
        string currencyPair,
        CancellationToken cancellationToken = default)
    {
        var normalizedPair = currencyPair.Trim().ToUpperInvariant();

        return await _context.HistoricalExchangeRateCaches
            .Where(d => d.CurrencyPair == normalizedPair)
            .OrderByDescending(d => d.RequestedDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<HistoricalExchangeRateCache> AddAsync(
        HistoricalExchangeRateCache data,
        CancellationToken cancellationToken = default)
    {
        // 確認是否已存在（快取為 immutable，不允許覆寫）
        var exists = await ExistsAsync(data.CurrencyPair, data.RequestedDate, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException(
                $"Cache entry already exists for {data.CurrencyPair}/{data.RequestedDate:yyyy-MM-dd}. " +
                "Historical cache is immutable - entries cannot be overwritten.");
        }

        await _context.HistoricalExchangeRateCaches.AddAsync(data, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return data;
    }

    public async Task<bool> ExistsAsync(
        string currencyPair,
        DateTime requestedDate,
        CancellationToken cancellationToken = default)
    {
        var normalizedPair = currencyPair.Trim().ToUpperInvariant();
        var dateOnly = requestedDate.Date;

        return await _context.HistoricalExchangeRateCaches
            .AnyAsync(
                d => d.CurrencyPair == normalizedPair && d.RequestedDate == dateOnly,
                cancellationToken);
    }
}
