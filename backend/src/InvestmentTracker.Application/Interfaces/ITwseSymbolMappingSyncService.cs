namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// TWSE symbol mapping synchronization abstraction used by application use cases.
/// </summary>
public interface ITwseSymbolMappingSyncService
{
    Task<TwseSymbolMappingSyncResult> SyncOnDemandAsync(
        IEnumerable<string> securityNames,
        CancellationToken cancellationToken = default);
}

public sealed record TwseSymbolMappingSyncResult(
    int Requested,
    int Resolved,
    int Unresolved,
    IReadOnlyList<TwseSymbolMappingSyncMapping> Mappings,
    IReadOnlyList<TwseSymbolMappingSyncError> Errors);

public sealed record TwseSymbolMappingSyncMapping(
    string SecurityName,
    string Ticker,
    string? Isin,
    string? Market);

public sealed record TwseSymbolMappingSyncError(
    string SecurityName,
    string ErrorCode,
    string Message);
