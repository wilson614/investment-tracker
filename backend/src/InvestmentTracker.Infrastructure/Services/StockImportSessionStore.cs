using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using Microsoft.Extensions.Caching.Memory;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// In-memory implementation for stock import preview/execute session binding.
/// </summary>
public sealed class StockImportSessionStore(IMemoryCache memoryCache) : IStockImportSessionStore
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan SessionSlidingExpiration = TimeSpan.FromMinutes(10);

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

    public Task RemoveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        memoryCache.Remove(GetCacheKey(sessionId));
        return Task.CompletedTask;
    }

    private static string GetCacheKey(Guid sessionId)
        => $"stock-import:preview-session:{sessionId:N}";
}
