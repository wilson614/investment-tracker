using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// In-memory implementation for stock import preview/execute session binding.
/// Snapshot is persisted as immutable DTO including baseline scaffold fields.
/// </summary>
public sealed class StockImportSessionStore(IMemoryCache memoryCache) : IStockImportSessionStore
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SessionSlidingExpiration = TimeSpan.FromMinutes(10);
    private static readonly object SessionConsumeLock = new();

    public Task SaveAsync(
        StockImportSessionSnapshotDto session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        memoryCache.Set(
            GetCacheKey(session.SessionId),
            session,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = SessionTtl,
                SlidingExpiration = SessionSlidingExpiration
            });

        return Task.CompletedTask;
    }

    public Task<StockImportSessionSnapshotDto?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        memoryCache.TryGetValue(GetCacheKey(sessionId), out StockImportSessionSnapshotDto? session);
        return Task.FromResult(session);
    }

    public Task<StockImportSessionSnapshotDto?> TryConsumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TryConsumeInternal(sessionId, static _ => true));
    }

    public Task<StockImportSessionSnapshotDto?> TryConsumeForOwnerAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(TryConsumeInternal(
            sessionId,
            session => session.UserId == userId && session.PortfolioId == portfolioId));
    }

    public Task RemoveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        memoryCache.Remove(GetCacheKey(sessionId));
        return Task.CompletedTask;
    }

    private StockImportSessionSnapshotDto? TryConsumeInternal(
        Guid sessionId,
        Func<StockImportSessionSnapshotDto, bool> canConsume)
    {
        var cacheKey = GetCacheKey(sessionId);

        lock (SessionConsumeLock)
        {
            memoryCache.TryGetValue(cacheKey, out StockImportSessionSnapshotDto? session);
            if (session is null)
            {
                return null;
            }

            if (!canConsume(session))
            {
                return null;
            }

            memoryCache.Remove(cacheKey);
            return session;
        }
    }

    private static string GetCacheKey(Guid sessionId)
        => $"stock-import:preview-session:{sessionId:N}";
}
