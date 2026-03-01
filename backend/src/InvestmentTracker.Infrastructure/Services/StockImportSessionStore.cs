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
    private static readonly TimeSpan ExecuteResultTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan ExecuteResultSlidingExpiration = TimeSpan.FromHours(2);
    private static readonly object SessionConsumeLock = new();
    private static readonly object ExecutionStateLock = new();

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

    public Task<bool> TryStartExecutionAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        var stateKey = GetExecutionStateCacheKey(sessionId);

        lock (ExecutionStateLock)
        {
            memoryCache.TryGetValue(stateKey, out StockImportExecuteSessionStateDto? existingState);
            if (existingState is not null)
            {
                if (existingState.UserId != userId || existingState.PortfolioId != portfolioId)
                {
                    return Task.FromResult(false);
                }

                if (string.Equals(existingState.ExecutionStatus, ExecutionStatusProcessing, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(existingState.ExecutionStatus, ExecutionStatusCompleted, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(false);
                }
            }

            var processingState = new StockImportExecuteSessionStateDto
            {
                SessionId = sessionId,
                UserId = userId,
                PortfolioId = portfolioId,
                ExecutionStatus = ExecutionStatusProcessing,
                Message = null,
                StartedAtUtc = DateTime.UtcNow,
                CompletedAtUtc = null,
                Result = null
            };

            SaveExecutionStateInternal(processingState);
            return Task.FromResult(true);
        }
    }

    public Task SaveExecutionResultAsync(
        StockImportExecuteSessionStateDto executionState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionState);

        lock (ExecutionStateLock)
        {
            SaveExecutionStateInternal(executionState);
        }

        return Task.CompletedTask;
    }

    public Task<StockImportExecuteSessionStateDto?> GetExecutionStateAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        memoryCache.TryGetValue(GetExecutionStateCacheKey(sessionId), out StockImportExecuteSessionStateDto? state);
        return Task.FromResult(state);
    }

    public Task RemoveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        memoryCache.Remove(GetCacheKey(sessionId));
        memoryCache.Remove(GetExecutionStateCacheKey(sessionId));
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

    private void SaveExecutionStateInternal(StockImportExecuteSessionStateDto executionState)
    {
        memoryCache.Set(
            GetExecutionStateCacheKey(executionState.SessionId),
            executionState,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ExecuteResultTtl,
                SlidingExpiration = ExecuteResultSlidingExpiration
            });
    }

    private static string GetCacheKey(Guid sessionId)
        => $"stock-import:preview-session:{sessionId:N}";

    private static string GetExecutionStateCacheKey(Guid sessionId)
        => $"stock-import:execute-state:{sessionId:N}";

    private const string ExecutionStatusProcessing = "processing";
    private const string ExecutionStatusCompleted = "completed";
}
