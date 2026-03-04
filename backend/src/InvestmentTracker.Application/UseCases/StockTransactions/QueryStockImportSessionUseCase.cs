using System.Globalization;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

public interface IQueryStockImportSessionUseCase
{
    Task<StockImportExecuteStatusResponseDto> ExecuteAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default);
}

public sealed class QueryStockImportSessionUseCase(
    IStockImportSessionStore stockImportSessionStore,
    ICurrentUserService currentUserService) : IQueryStockImportSessionUseCase
{
    public const string PendingStatus = "pending";
    public const string ProcessingStatus = "processing";
    public const string CompletedStatus = "completed";
    public const string FailedStatus = "failed";
    public const string NotFoundStatus = "not_found";

    private const string RowStatusCompleted = "completed";
    private const string RowStatusFailed = "failed";

    public async Task<StockImportExecuteStatusResponseDto> ExecuteAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var executionState = await stockImportSessionStore.GetExecutionStateAsync(sessionId, cancellationToken);
        if (executionState is null)
        {
            var previewSession = await stockImportSessionStore.GetAsync(sessionId, cancellationToken);
            if (previewSession is null)
            {
                return new StockImportExecuteStatusResponseDto
                {
                    SessionId = sessionId,
                    ExecutionStatus = NotFoundStatus,
                    Message = null,
                    StartedAtUtc = null,
                    CompletedAtUtc = null,
                    CheckpointCursor = null,
                    Rows = [],
                    Result = null
                };
            }

            if (previewSession.UserId != userId)
            {
                throw new AccessDeniedException();
            }

            return new StockImportExecuteStatusResponseDto
            {
                SessionId = previewSession.SessionId,
                PortfolioId = previewSession.PortfolioId,
                ExecutionStatus = PendingStatus,
                Message = null,
                StartedAtUtc = null,
                CompletedAtUtc = null,
                CheckpointCursor = null,
                Rows = [],
                Result = null
            };
        }

        if (executionState.UserId != userId)
        {
            throw new AccessDeniedException();
        }

        if (string.Equals(executionState.ExecutionStatus, CompletedStatus, StringComparison.OrdinalIgnoreCase))
        {
            var completedAtUtc = executionState.CompletedAtUtc ?? executionState.StartedAtUtc;

            return new StockImportExecuteStatusResponseDto
            {
                SessionId = executionState.SessionId,
                PortfolioId = executionState.PortfolioId,
                ExecutionStatus = CompletedStatus,
                Message = null,
                StartedAtUtc = executionState.StartedAtUtc,
                CompletedAtUtc = executionState.CompletedAtUtc,
                CheckpointCursor = BuildCompletedCheckpointCursor(executionState.SessionId, completedAtUtc),
                Rows = BuildCompletedRows(executionState.Result),
                Result = executionState.Result
            };
        }

        if (string.Equals(executionState.ExecutionStatus, FailedStatus, StringComparison.OrdinalIgnoreCase))
        {
            return new StockImportExecuteStatusResponseDto
            {
                SessionId = executionState.SessionId,
                PortfolioId = executionState.PortfolioId,
                ExecutionStatus = FailedStatus,
                Message = executionState.Message,
                StartedAtUtc = executionState.StartedAtUtc,
                CompletedAtUtc = executionState.CompletedAtUtc,
                CheckpointCursor = null,
                Rows = [],
                Result = null
            };
        }

        if (string.Equals(executionState.ExecutionStatus, ProcessingStatus, StringComparison.OrdinalIgnoreCase))
        {
            return new StockImportExecuteStatusResponseDto
            {
                SessionId = executionState.SessionId,
                PortfolioId = executionState.PortfolioId,
                ExecutionStatus = ProcessingStatus,
                Message = null,
                StartedAtUtc = executionState.StartedAtUtc,
                CompletedAtUtc = executionState.CompletedAtUtc,
                CheckpointCursor = null,
                Rows = [],
                Result = null
            };
        }

        return new StockImportExecuteStatusResponseDto
        {
            SessionId = executionState.SessionId,
            PortfolioId = executionState.PortfolioId,
            ExecutionStatus = PendingStatus,
            Message = null,
            StartedAtUtc = null,
            CompletedAtUtc = null,
            CheckpointCursor = null,
            Rows = executionState.Result is null ? [] : BuildCompletedRows(executionState.Result),
            Result = null
        };
    }

    private static string BuildCompletedCheckpointCursor(Guid sessionId, DateTime completedAtUtc)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"completed:{sessionId:N}:{completedAtUtc:O}");

    private static IReadOnlyList<StockImportStatusRowDto> BuildCompletedRows(StockImportExecuteResponseDto? result)
    {
        if (result is null || result.Results.Count == 0)
        {
            return [];
        }

        return result.Results
            .OrderBy(row => row.RowNumber)
            .Select(row => new StockImportStatusRowDto
            {
                RowNumber = row.RowNumber,
                Status = row.Success ? RowStatusCompleted : RowStatusFailed,
                TransactionId = row.TransactionId,
                ErrorCode = row.ErrorCode,
                Message = row.Message
            })
            .ToArray();
    }
}
