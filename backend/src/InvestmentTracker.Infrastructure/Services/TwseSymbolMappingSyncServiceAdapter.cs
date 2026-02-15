using InvestmentTracker.Application.Interfaces;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// Infrastructure adapter that exposes TWSE symbol synchronization to application use cases.
/// </summary>
public sealed class TwseSymbolMappingSyncServiceAdapter(
    TwseSymbolMappingService twseSymbolMappingService) : ITwseSymbolMappingSyncService
{
    public async Task<TwseSymbolMappingSyncResult> SyncOnDemandAsync(
        IEnumerable<string> securityNames,
        CancellationToken cancellationToken = default)
    {
        var result = await twseSymbolMappingService.SyncOnDemandAsync(securityNames, cancellationToken);

        return new TwseSymbolMappingSyncResult(
            result.Requested,
            result.Resolved,
            result.Unresolved,
            result.Mappings
                .Select(mapping => new TwseSymbolMappingSyncMapping(
                    mapping.SecurityName,
                    mapping.Ticker,
                    mapping.Isin,
                    mapping.Market))
                .ToList(),
            result.Errors
                .Select(error => new TwseSymbolMappingSyncError(
                    error.SecurityName,
                    error.ErrorCode,
                    error.Message))
                .ToList());
    }
}
