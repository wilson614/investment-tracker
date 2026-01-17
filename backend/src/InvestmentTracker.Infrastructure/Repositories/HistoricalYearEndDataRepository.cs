using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for HistoricalYearEndData cache.
/// This is a global cache shared across all users.
/// </summary>
public class HistoricalYearEndDataRepository : IHistoricalYearEndDataRepository
{
    private readonly AppDbContext _context;

    public HistoricalYearEndDataRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<HistoricalYearEndData?> GetAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        return await _context.HistoricalYearEndData
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
        return await _context.HistoricalYearEndData
            .Where(d => d.Year == year)
            .OrderBy(d => d.DataType)
            .ThenBy(d => d.Ticker)
            .ToListAsync(cancellationToken);
    }

    public async Task<HistoricalYearEndData> AddAsync(
        HistoricalYearEndData data,
        CancellationToken cancellationToken = default)
    {
        // Check if entry already exists (cache is immutable)
        var exists = await ExistsAsync(data.DataType, data.Ticker, data.Year, cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException(
                $"Cache entry already exists for {data.DataType}/{data.Ticker}/{data.Year}. " +
                "Historical cache is immutable - entries cannot be overwritten.");
        }

        await _context.HistoricalYearEndData.AddAsync(data, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return data;
    }

    public async Task<bool> ExistsAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default)
    {
        var normalizedTicker = ticker.Trim().ToUpperInvariant();
        return await _context.HistoricalYearEndData
            .AnyAsync(
                d => d.DataType == dataType && d.Ticker == normalizedTicker && d.Year == year,
                cancellationToken);
    }
}
