using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

/// <summary>
/// Repository implementation for ETF classifications.
/// </summary>
public class EtfClassificationRepository : IEtfClassificationRepository
{
    private readonly AppDbContext _context;

    public EtfClassificationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<EtfClassification?> GetBySymbolAndMarketAsync(string symbol, string market, CancellationToken cancellationToken = default)
    {
        return await _context.EtfClassifications
            .FirstOrDefaultAsync(c => c.Symbol == symbol && c.Market == market, cancellationToken);
    }

    public async Task<IReadOnlyList<EtfClassification>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.EtfClassifications.ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(EtfClassification classification, CancellationToken cancellationToken = default)
    {
        var existing = await GetBySymbolAndMarketAsync(classification.Symbol, classification.Market, cancellationToken);

        if (existing == null)
        {
            await _context.EtfClassifications.AddAsync(classification, cancellationToken);
        }
        else
        {
            existing.SetType(classification.Type, classification.UpdatedByUserId);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EtfClassification>> GetBySymbolsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        var symbolList = symbols.ToList();
        return await _context.EtfClassifications
            .Where(c => symbolList.Contains(c.Symbol))
            .ToListAsync(cancellationToken);
    }
}
