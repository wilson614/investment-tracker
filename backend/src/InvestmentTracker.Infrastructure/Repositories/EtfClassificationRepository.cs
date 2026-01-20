using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// ETF 分類（EtfClassification）的 Repository 實作。
/// </summary>
public class EtfClassificationRepository(AppDbContext context) : IEtfClassificationRepository
{
    public async Task<EtfClassification?> GetBySymbolAndMarketAsync(string symbol, string market, CancellationToken cancellationToken = default)
    {
        return await context.EtfClassifications
            .FirstOrDefaultAsync(c => c.Symbol == symbol && c.Market == market, cancellationToken);
    }

    public async Task<IReadOnlyList<EtfClassification>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await context.EtfClassifications.ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(EtfClassification classification, CancellationToken cancellationToken = default)
    {
        var existing = await GetBySymbolAndMarketAsync(classification.Symbol, classification.Market, cancellationToken);

        if (existing == null)
        {
            await context.EtfClassifications.AddAsync(classification, cancellationToken);
        }
        else
        {
            existing.SetType(classification.Type, classification.UpdatedByUserId);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EtfClassification>> GetBySymbolsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        var symbolList = symbols.ToList();
        return await context.EtfClassifications
            .Where(c => symbolList.Contains(c.Symbol))
            .ToListAsync(cancellationToken);
    }
}
