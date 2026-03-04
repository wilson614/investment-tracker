using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// DB-backed implementation for stock import preview/execute session binding.
/// </summary>
public sealed class StockImportSessionStore(AppDbContext context) : IStockImportSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SessionLocks = new();

    private const string InMemoryProviderName = "Microsoft.EntityFrameworkCore.InMemory";
    private const string ExecutionStatusPending = "pending";
    private const string ExecutionStatusProcessing = "processing";
    private const string ExecutionStatusCompleted = "completed";
    private const string ExecutionStatusFailed = "failed";

    public async Task SaveAsync(
        StockImportSessionSnapshotDto session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        var snapshotJson = Serialize(session);

        var existing = await context.StockImportSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.SessionId == session.SessionId, cancellationToken);

        if (existing is null)
        {
            var entity = StockImportSession.CreatePending(
                session.SessionId,
                session.UserId,
                session.PortfolioId,
                snapshotJson);

            await context.StockImportSessions.AddAsync(entity, cancellationToken);
        }
        else
        {
            existing.ReplacePending(
                session.UserId,
                session.PortfolioId,
                snapshotJson);

            context.StockImportSessions.Update(existing);
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<StockImportSessionSnapshotDto?> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.StockImportSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (entity is null)
            return null;

        if (string.IsNullOrWhiteSpace(entity.SessionSnapshotJson))
            return null;

        if (!string.Equals(entity.ExecutionStatus, "pending", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entity.ExecutionStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Deserialize<StockImportSessionSnapshotDto>(entity.SessionSnapshotJson);
    }

    public async Task<StockImportSessionSnapshotDto?> TryConsumeAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.StockImportSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (entity is null || string.IsNullOrWhiteSpace(entity.SessionSnapshotJson))
            return null;

        if (string.Equals(entity.ExecutionStatus, ExecutionStatusCompleted, StringComparison.OrdinalIgnoreCase))
            return null;

        return Deserialize<StockImportSessionSnapshotDto>(entity.SessionSnapshotJson);
    }

    public async Task<StockImportSessionSnapshotDto?> TryConsumeForOwnerAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.StockImportSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (entity is null)
            return null;

        if (entity.UserId != userId || entity.PortfolioId != portfolioId)
            return null;

        if (string.IsNullOrWhiteSpace(entity.SessionSnapshotJson))
            return null;

        if (string.Equals(entity.ExecutionStatus, ExecutionStatusCompleted, StringComparison.OrdinalIgnoreCase))
            return null;

        return Deserialize<StockImportSessionSnapshotDto>(entity.SessionSnapshotJson);
    }

    public async Task<bool> TryStartExecutionAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken = default)
    {
        if (IsInMemoryProvider())
        {
            var gate = SessionLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);

            try
            {
                return await TryStartExecutionInternalAsync(sessionId, userId, portfolioId, cancellationToken);
            }
            finally
            {
                gate.Release();
            }
        }

        return await TryStartExecutionAtomicAsync(sessionId, userId, portfolioId, cancellationToken);
    }

    private async Task<bool> TryStartExecutionInternalAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var entity = await context.StockImportSessions
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (entity is null)
            return false;

        if (entity.UserId != userId || entity.PortfolioId != portfolioId)
            return false;

        if (string.IsNullOrWhiteSpace(entity.SessionSnapshotJson))
            return false;

        if (!entity.TryStartProcessing(DateTime.UtcNow))
            return false;

        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task<bool> TryStartExecutionAtomicAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        var affectedRows = await context.StockImportSessions
            .IgnoreQueryFilters()
            .Where(x => x.SessionId == sessionId)
            .Where(x => x.UserId == userId)
            .Where(x => x.PortfolioId == portfolioId)
            .Where(x => x.SessionSnapshotJson != string.Empty)
            .Where(x =>
                x.ExecutionStatus == ExecutionStatusPending
                || x.ExecutionStatus == ExecutionStatusFailed)
            .ExecuteUpdateAsync(
                updater => updater
                    .SetProperty(x => x.ExecutionStatus, ExecutionStatusProcessing)
                    .SetProperty(x => x.Message, (string?)null)
                    .SetProperty(x => x.StartedAtUtc, nowUtc)
                    .SetProperty(x => x.CompletedAtUtc, (DateTime?)null)
                    .SetProperty(x => x.ExecutionResultJson, (string?)null)
                    .SetProperty(x => x.UpdatedAt, nowUtc),
                cancellationToken);

        return affectedRows == 1;
    }

    private bool IsInMemoryProvider()
        => string.Equals(context.Database.ProviderName, InMemoryProviderName, StringComparison.Ordinal);

    public async Task SaveExecutionResultAsync(
        StockImportExecuteSessionStateDto executionState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executionState);

        var entity = await context.StockImportSessions
            .FirstOrDefaultAsync(x => x.SessionId == executionState.SessionId, cancellationToken);

        if (entity is null)
        {
            throw new InvalidOperationException($"Stock import session {executionState.SessionId} was not found when saving execution result.");
        }

        if (string.Equals(executionState.ExecutionStatus, ExecutionStatusCompleted, StringComparison.OrdinalIgnoreCase)
            && executionState.Result is not null)
        {
            entity.MarkCompleted(
                executionState.CompletedAtUtc ?? DateTime.UtcNow,
                Serialize(executionState.Result));
        }
        else if (string.Equals(executionState.ExecutionStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            entity.MarkFailed(
                executionState.CompletedAtUtc ?? DateTime.UtcNow,
                executionState.Message);
        }
        else
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Unsupported stock import execution status '{0}' for session {1}.",
                    executionState.ExecutionStatus,
                    executionState.SessionId));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<StockImportExecuteSessionStateDto?> GetExecutionStateAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.StockImportSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (entity is null)
            return null;

        var hasExecutionState = !string.Equals(entity.ExecutionStatus, "pending", StringComparison.OrdinalIgnoreCase)
            || entity.StartedAtUtc.HasValue
            || entity.CompletedAtUtc.HasValue
            || !string.IsNullOrWhiteSpace(entity.Message)
            || !string.IsNullOrWhiteSpace(entity.ExecutionResultJson);

        if (!hasExecutionState)
            return null;

        StockImportExecuteResponseDto? result = null;
        if (!string.IsNullOrWhiteSpace(entity.ExecutionResultJson))
        {
            result = Deserialize<StockImportExecuteResponseDto>(entity.ExecutionResultJson);
        }

        return new StockImportExecuteSessionStateDto
        {
            SessionId = entity.SessionId,
            UserId = entity.UserId,
            PortfolioId = entity.PortfolioId,
            ExecutionStatus = entity.ExecutionStatus,
            Message = entity.Message,
            StartedAtUtc = entity.StartedAtUtc ?? entity.CreatedAt,
            CompletedAtUtc = entity.CompletedAtUtc,
            Result = result
        };
    }

    public async Task RemoveAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var entity = await context.StockImportSessions
            .FirstOrDefaultAsync(x => x.SessionId == sessionId, cancellationToken);

        if (entity is null)
            return;

        context.StockImportSessions.Remove(entity);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, JsonOptions);

    private static T? Deserialize<T>(string json)
        => JsonSerializer.Deserialize<T>(json, JsonOptions);
}
