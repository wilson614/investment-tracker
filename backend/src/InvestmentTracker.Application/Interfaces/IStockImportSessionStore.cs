using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Stores preview stock-import sessions for later execute validation.
/// </summary>
public interface IStockImportSessionStore
{
    Task SaveAsync(
        StockImportSessionSnapshotDto session,
        CancellationToken cancellationToken = default);

    Task<StockImportSessionSnapshotDto?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
