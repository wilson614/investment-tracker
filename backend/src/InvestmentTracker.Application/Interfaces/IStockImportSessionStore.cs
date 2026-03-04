using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Durable stock-import session store.
/// Session snapshot includes baseline scaffold fields for import foundation.
/// </summary>
public interface IStockImportSessionStore
{
    Task SaveAsync(
        StockImportSessionSnapshotDto session,
        CancellationToken cancellationToken = default);

    Task<StockImportSessionSnapshotDto?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the preview session snapshot when executable.
    /// Returns null when session does not exist or is no longer executable.
    /// </summary>
    Task<StockImportSessionSnapshotDto?> TryConsumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the preview session snapshot when executable and ownership matches.
    /// Returns null when session does not exist, is no longer executable, or does not belong to the specified owner/portfolio.
    /// </summary>
    Task<StockImportSessionSnapshotDto?> TryConsumeForOwnerAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to mark execute flow as processing for a given session.
    /// Returns false when already processing/completed or owner/portfolio mismatches existing execution state.
    /// </summary>
    Task<bool> TryStartExecutionAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    Task SaveExecutionResultAsync(
        StockImportExecuteSessionStateDto executionState,
        CancellationToken cancellationToken = default);

    Task<StockImportExecuteSessionStateDto?> GetExecutionStateAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
