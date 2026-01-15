using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for Euronext quote cache.
/// </summary>
public class EuronextQuoteCacheRepository : IEuronextQuoteCacheRepository
{
    private readonly AppDbContext _context;

    public EuronextQuoteCacheRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<EuronextQuoteCache?> GetByIsinAndMicAsync(string isin, string mic, CancellationToken cancellationToken = default)
    {
        return await _context.EuronextQuoteCaches
            .FirstOrDefaultAsync(q => q.Isin == isin && q.Mic == mic, cancellationToken);
    }

    public async Task UpsertAsync(EuronextQuoteCache quoteCache, CancellationToken cancellationToken = default)
    {
        var existing = await GetByIsinAndMicAsync(quoteCache.Isin, quoteCache.Mic, cancellationToken);

        if (existing == null)
        {
            await _context.EuronextQuoteCaches.AddAsync(quoteCache, cancellationToken);
        }
        else
        {
            existing.UpdateQuote(quoteCache.Price, quoteCache.Currency, quoteCache.MarketTime);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAsStaleAsync(string isin, string mic, CancellationToken cancellationToken = default)
    {
        var existing = await GetByIsinAndMicAsync(isin, mic, cancellationToken);

        if (existing != null)
        {
            existing.MarkAsStale();
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
