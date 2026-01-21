using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// HistoricalYearEndData 快取的 Repository 實作。
/// 這是一個跨使用者共用的全域快取。
/// </summary>
public class HistoricalYearEndDataRepository(AppDbContext context) : IHistoricalYearEndDataRepository
{
    public async Task<HistoricalYearEndData?> GetAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        return await context.HistoricalYearEndData
            .FirstOrDefaultAsync(
                d => d.DataType == dataType && d.Ticker == normalizedTicker && d.Year == year,
                cancellationToken);
    }

    public async Task<HistoricalYearEndData?> GetStockPriceAsync(
        string ticker,
        int year,
        CancellationToken cancellationToken = default)
    {
        return await GetAsync(HistoricalDataType.StockPrice, ticker, year, cancellationToken);
    }

    public async Task<HistoricalYearEndData?> GetExchangeRateAsync(
        string currencyPair,
        int year,
        CancellationToken cancellationToken = default)
    {
        return await GetAsync(HistoricalDataType.ExchangeRate, currencyPair, year, cancellationToken);
    }

    public async Task<IReadOnlyList<HistoricalYearEndData>> GetByYearAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        return await context.HistoricalYearEndData
            .Where(d => d.Year == year)
            .OrderBy(d => d.DataType)
            .ThenBy(d => d.Ticker)
            .ToListAsync(cancellationToken);
    }

    public async Task<HistoricalYearEndData> AddAsync(
        HistoricalYearEndData data,
        CancellationToken cancellationToken = default)
    {
        // 確認是否已存在（快取為 immutable，不允許覆寫）
        var exists = await ExistsAsync(data.DataType, data.Ticker, data.Year, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException(
                $"Cache entry already exists for {data.DataType}/{data.Ticker}/{data.Year}. " +
                "Historical cache is immutable - entries cannot be overwritten.");
        }

        await context.HistoricalYearEndData.AddAsync(data, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        return data;
    }

    public async Task<bool> ExistsAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        return await context.HistoricalYearEndData
            .AnyAsync(
                d => d.DataType == dataType && d.Ticker == normalizedTicker && d.Year == year,
                cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var entity = await context.HistoricalYearEndData
            .FirstOrDefaultAsync(
                d => d.DataType == dataType && d.Ticker == normalizedTicker && d.Year == year,
                cancellationToken);

        if (entity == null)
            return false;

        context.HistoricalYearEndData.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> DeleteByTickerAsync(
        string ticker,
        CancellationToken cancellationToken = default)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        var entities = await context.HistoricalYearEndData
            .Where(d => d.Ticker == normalizedTicker)
            .ToListAsync(cancellationToken);

        if (entities.Count == 0)
            return 0;

        context.HistoricalYearEndData.RemoveRange(entities);
        await context.SaveChangesAsync(cancellationToken);
        return entities.Count;
    }
}
