using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

public class EuronextSymbolMappingRepository(AppDbContext context) : IEuronextSymbolMappingRepository
{
    public async Task<EuronextSymbolMapping?> GetByTickerAsync(string ticker, CancellationToken cancellationToken = default)
    {
        return await context.EuronextSymbolMappings
            .FirstOrDefaultAsync(m => m.Ticker == ticker.ToUpperInvariant(), cancellationToken);
    }

    public async Task UpsertAsync(EuronextSymbolMapping mapping, CancellationToken cancellationToken = default)
    {
        var existing = await context.EuronextSymbolMappings
            .FirstOrDefaultAsync(m => m.Ticker == mapping.Ticker, cancellationToken);

        if (existing != null)
        {
            existing.Update(mapping.Isin, mapping.Mic, mapping.Currency, mapping.Name);
        }
        else
        {
            context.EuronextSymbolMappings.Add(mapping);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EuronextSymbolMapping>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.EuronextSymbolMappings.ToListAsync(cancellationToken);
    }
}
