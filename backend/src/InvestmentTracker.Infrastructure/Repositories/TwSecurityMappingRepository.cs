using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Repositories;

public class TwSecurityMappingRepository(AppDbContext context) : ITwSecurityMappingRepository
{
    public async Task<TwSecurityMapping?> GetByTickerAsync(string ticker, CancellationToken cancellationToken = default)
    {
        var normalizedTicker = NormalizeTicker(ticker);
        if (normalizedTicker is null)
        {
            return null;
        }

        return await context.TwSecurityMappings
            .FirstOrDefaultAsync(m => m.Ticker == normalizedTicker, cancellationToken);
    }

    public async Task<IReadOnlyList<TwSecurityMapping>> GetBySecurityNameAsync(string securityName, CancellationToken cancellationToken = default)
    {
        var normalizedName = NormalizeSecurityNameForMatch(securityName);
        if (normalizedName is null)
        {
            return [];
        }

        return await context.TwSecurityMappings
            .Where(m => m.SecurityName == normalizedName)
            .OrderBy(m => m.Ticker)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TwSecurityMapping>> GetBySecurityNamesAsync(IEnumerable<string> securityNames, CancellationToken cancellationToken = default)
    {
        var normalizedNames = securityNames
            .Select(NormalizeSecurityNameForMatch)
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedNames.Count == 0)
        {
            return [];
        }

        return await context.TwSecurityMappings
            .Where(m => normalizedNames.Contains(m.SecurityName))
            .OrderBy(m => m.SecurityName)
            .ThenBy(m => m.Ticker)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(TwSecurityMapping mapping, CancellationToken cancellationToken = default)
    {
        var existing = await context.TwSecurityMappings
            .FirstOrDefaultAsync(m => m.Ticker == mapping.Ticker, cancellationToken);

        if (existing is not null)
        {
            existing.Update(
                mapping.SecurityName,
                mapping.LastSyncedAt,
                mapping.Source,
                mapping.Isin,
                mapping.Market,
                mapping.Currency);
        }
        else
        {
            context.TwSecurityMappings.Add(mapping);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static string? NormalizeTicker(string? ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return null;
        }

        return ticker.Trim().ToUpperInvariant();
    }

    private static string? NormalizeSecurityNameForMatch(string? securityName)
    {
        if (string.IsNullOrWhiteSpace(securityName))
        {
            return null;
        }

        var replaced = securityName.Trim().Replace('\u3000', ' ');
        return string.Join(' ', replaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
