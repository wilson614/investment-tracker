using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Euronext 報價快取（EuronextQuoteCache）的 Repository 實作。
/// </summary>
public class EuronextQuoteCacheRepository(AppDbContext context) : IEuronextQuoteCacheRepository
{
    public async Task<EuronextQuoteCache?> GetByIsinAndMicAsync(string isin, string mic, CancellationToken cancellationToken = default)
    {
        return await context.EuronextQuoteCaches
            .FirstOrDefaultAsync(q => q.Isin == isin && q.Mic == mic, cancellationToken);
    }

    public async Task UpsertAsync(EuronextQuoteCache quoteCache, CancellationToken cancellationToken = default)
    {
        var existing = await GetByIsinAndMicAsync(quoteCache.Isin, quoteCache.Mic, cancellationToken);

        if (existing == null)
        {
            await context.EuronextQuoteCaches.AddAsync(quoteCache, cancellationToken);
        }
        else
        {
            existing.UpdateQuote(quoteCache.Price, quoteCache.Currency, quoteCache.MarketTime, quoteCache.ChangePercent, quoteCache.Change);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAsStaleAsync(string isin, string mic, CancellationToken cancellationToken = default)
    {
        var existing = await GetByIsinAndMicAsync(isin, mic, cancellationToken);

        if (existing != null)
        {
            existing.MarkAsStale();
            await context.SaveChangesAsync(cancellationToken);
        }
    }
}
