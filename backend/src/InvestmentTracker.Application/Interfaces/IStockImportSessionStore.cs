using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Stores preview stock-import sessions for later execute validation.
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
    /// Atomically retrieves and removes the session snapshot to prevent replay/double execute.
    /// Returns null when session does not exist or already consumed.
    /// </summary>
    Task<StockImportSessionSnapshotDto?> TryConsumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically validates session owner/portfolio and consumes the session in the same critical section.
    /// Returns null when session does not exist, already consumed, or does not belong to the specified owner/portfolio.
    /// </summary>
    Task<StockImportSessionSnapshotDto?> TryConsumeForOwnerAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    Task RemoveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}
