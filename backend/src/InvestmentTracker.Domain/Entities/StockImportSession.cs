using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Durable stock import session state for preview/execute lifecycle and replay-safe execution result.
/// </summary>
public class StockImportSession : BaseEntity
{
    private const string StatusPending = "pending";
    private const string StatusProcessing = "processing";
    private const string StatusCompleted = "completed";
    private const string StatusFailed = "failed";

    private StockImportSession()
    {
    }

    public Guid SessionId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid PortfolioId { get; private set; }

    /// <summary>
    /// pending | processing | completed | failed
    /// </summary>
    public string ExecutionStatus { get; private set; } = StatusPending;

    /// <summary>
    /// Stores preview snapshot JSON for execute validation.
    /// </summary>
    public string SessionSnapshotJson { get; private set; } = string.Empty;

    /// <summary>
    /// Stores execute result JSON for replay/status query (only when completed).
    /// </summary>
    public string? ExecutionResultJson { get; private set; }

    public string? Message { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    public static StockImportSession CreatePending(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        string sessionSnapshotJson)
    {
        if (sessionId == Guid.Empty)
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        if (portfolioId == Guid.Empty)
            throw new ArgumentException("PortfolioId is required.", nameof(portfolioId));

        if (string.IsNullOrWhiteSpace(sessionSnapshotJson))
            throw new ArgumentException("Session snapshot JSON is required.", nameof(sessionSnapshotJson));

        return new StockImportSession
        {
            SessionId = sessionId,
            UserId = userId,
            PortfolioId = portfolioId,
            ExecutionStatus = StatusPending,
            SessionSnapshotJson = sessionSnapshotJson,
            ExecutionResultJson = null,
            Message = null,
            StartedAtUtc = null,
            CompletedAtUtc = null
        };
    }

    public void ReplacePending(
        Guid userId,
        Guid portfolioId,
        string sessionSnapshotJson)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));

        if (portfolioId == Guid.Empty)
            throw new ArgumentException("PortfolioId is required.", nameof(portfolioId));

        if (string.IsNullOrWhiteSpace(sessionSnapshotJson))
            throw new ArgumentException("Session snapshot JSON is required.", nameof(sessionSnapshotJson));

        UserId = userId;
        PortfolioId = portfolioId;
        ExecutionStatus = StatusPending;
        SessionSnapshotJson = sessionSnapshotJson;
        ExecutionResultJson = null;
        Message = null;
        StartedAtUtc = null;
        CompletedAtUtc = null;
    }

    public bool TryStartProcessing(DateTime startedAtUtc)
    {
        if (!string.Equals(ExecutionStatus, StatusPending, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(ExecutionStatus, StatusFailed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ExecutionStatus = StatusProcessing;
        StartedAtUtc = EnsureUtc(startedAtUtc);
        CompletedAtUtc = null;
        Message = null;
        ExecutionResultJson = null;
        return true;
    }

    public void MarkCompleted(DateTime completedAtUtc, string executionResultJson)
    {
        if (string.IsNullOrWhiteSpace(executionResultJson))
            throw new ArgumentException("Execution result JSON is required.", nameof(executionResultJson));

        ExecutionStatus = StatusCompleted;
        StartedAtUtc ??= EnsureUtc(completedAtUtc);
        CompletedAtUtc = EnsureUtc(completedAtUtc);
        Message = null;
        SessionSnapshotJson = string.Empty;
        ExecutionResultJson = executionResultJson;
    }

    public void MarkFailed(DateTime completedAtUtc, string? message)
    {
        ExecutionStatus = StatusFailed;
        StartedAtUtc ??= EnsureUtc(completedAtUtc);
        CompletedAtUtc = EnsureUtc(completedAtUtc);
        Message = string.IsNullOrWhiteSpace(message) ? null : message;
        ExecutionResultJson = null;
    }

    private static DateTime EnsureUtc(DateTime value)
        => value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
