using System.Globalization;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.CurrencyTransactions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

internal sealed class StockImportRowFailureException(
    string errorCode,
    string fieldName,
    string message,
    string correctionGuidance,
    string? invalidValue = null,
    StockImportBalanceDecisionContextDto? balanceDecision = null,
    StockImportSellBeforeBuyDecisionContextDto? sellBeforeBuyDecision = null)
    : BusinessRuleException(message)
{
    public string ErrorCode { get; } = errorCode;
    public string FieldName { get; } = fieldName;
    public string CorrectionGuidance { get; } = correctionGuidance;
    public string? InvalidValue { get; } = invalidValue;
    public StockImportBalanceDecisionContextDto? BalanceDecision { get; } = balanceDecision;
    public StockImportSellBeforeBuyDecisionContextDto? SellBeforeBuyDecision { get; } = sellBeforeBuyDecision;
}

public interface IExecuteStockImportUseCase
{
    Task<StockImportExecuteResponseDto> ExecuteAsync(
        ExecuteStockImportRequest request,
        CancellationToken cancellationToken = default);
}

internal readonly record struct ImportedStockTransactionCreationResult(
    StockTransaction Transaction,
    IReadOnlyList<StockTransaction> CreatedTransactions,
    bool UsesPartialHistoryAssumption,
    StockImportSellBeforeBuyDecisionContextDto? SellBeforeBuyDecision);

public sealed class ExecuteStockImportUseCase(
    IPortfolioRepository portfolioRepository,
    ICurrentUserService currentUserService,
    IStockImportSessionStore stockImportSessionStore,
    IStockTransactionRepository stockTransactionRepository,
    ICurrencyLedgerRepository currencyLedgerRepository,
    ICurrencyTransactionRepository currencyTransactionRepository,
    ITransactionDateExchangeRateService txDateFxService,
    IMonthlySnapshotService monthlySnapshotService,
    ITransactionPortfolioSnapshotService txSnapshotService,
    CurrencyLedgerService currencyLedgerService,
    PortfolioCalculator portfolioCalculator,
    IAppDbTransactionManager transactionManager,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    ILogger<ExecuteStockImportUseCase> logger) : IExecuteStockImportUseCase
{
    private const string StatusCommitted = "committed";
    private const string StatusRejected = "rejected";

    private const string ExecutionStatusProcessing = QueryStockImportSessionUseCase.ProcessingStatus;
    private const string ExecutionStatusCompleted = QueryStockImportSessionUseCase.CompletedStatus;
    private const string ExecutionStatusFailed = QueryStockImportSessionUseCase.FailedStatus;

    private const string ErrorCodeSymbolUnresolved = "SYMBOL_UNRESOLVED";
    private const string ErrorCodeSessionRowMismatch = "SESSION_ROW_MISMATCH";
    private const string ErrorCodeTradeSideConfirmationRequired = "TRADE_SIDE_CONFIRMATION_REQUIRED";
    private const string ErrorCodeBalanceActionRequired = "BALANCE_ACTION_REQUIRED";
    private const string ErrorCodeBusinessRuleViolation = "BUSINESS_RULE_VIOLATION";
    private const string ErrorCodeMarketResolutionRequired = "MARKET_RESOLUTION_REQUIRED";
    private const string ErrorCodeSellBeforeBuyActionRequired = "SELL_BEFORE_BUY_ACTION_REQUIRED";

    private const string WarningCodePartialPeriodAssumption = "PARTIAL_PERIOD_ASSUMPTION";

    private const string FieldTicker = "ticker";
    private const string FieldRowNumber = "rowNumber";
    private const string FieldConfirmedTradeSide = "confirmedTradeSide";
    private const string FieldRow = "row";
    private const string FieldPosition = "position";
    private const string FieldMarket = "market";
    private const string FieldSellBeforeBuyAction = "sellBeforeBuyAction";
    private const string FieldBalanceAction = StockBalanceActionRules.FieldBalanceAction;

    private const string MessageExcluded = "此列已由使用者排除。";
    private const string MessageCreated = "Created";
    private const string MessageExecutionInProgress = "Import execution is already in progress for this session.";
    private const string MessageSessionNotFound = "Import session not found, expired, or already consumed.";
    private const string MessageExecutionFailedSafe = "Import execution failed. Please retry later.";
    private const string MessageBrokerStatementAtomicRollbackDueToBalanceAction = "匯入包含需先指定餘額處理方式的列，已整批回滾；請補齊後重試。";
    private const string MessageAtomicRejectedRollback = MessageExecutionFailedSafe;
    private const string WarningMessagePartialPeriodAssumption = "偵測到匯入可能是部分期間；已依使用者指定方式套用 sell-before-buy 處理。";
    private const string WarningCorrectionGuidancePartialPeriodAssumption = "若要提升帳本成本與績效精準度，建議補匯入更早交易紀錄或提供完整期初持倉基準。";

    private const string SellBeforeBuyStrategyNone = "none";
    private const string SellBeforeBuyStrategyUseOpeningPosition = "use_opening_position";
    private const string SellBeforeBuyStrategyCreateAdjustment = "create_adjustment";

    private const string SellBeforeBuyReasonNotApplicable = "not_sell_before_buy_or_has_position";
    private const string SellBeforeBuyReasonAutoDefault = "auto_default_for_sell_before_buy";
    private const string SellBeforeBuyReasonRowOverride = "row_override";
    private const string SellBeforeBuyReasonGlobalDefault = "global_default";

    private const string DecisionScopeAutoDefault = "auto_default";
    private const string DecisionScopeGlobalDefault = "global_default";
    private const string DecisionScopeRowOverride = "row_override";

    private const string TradeSideBuy = "buy";
    private const string TradeSideSell = "sell";
    private const string TwdCurrencyCode = "TWD";
    private const string ImportExecuteOpeningInitialBalanceNote = "import-execute-opening-initial-balance";
    private const string ImportExecuteOpeningInitialBalanceOffsetNote = "import-execute-opening-initial-balance-offset";

    public async Task<StockImportExecuteResponseDto> ExecuteAsync(
        ExecuteStockImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SessionId == Guid.Empty)
            throw new BusinessRuleException("SessionId is required.");

        var duplicatedRowNumbers = request.Rows
            .GroupBy(row => row.RowNumber)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .OrderBy(rowNumber => rowNumber)
            .ToList();

        if (duplicatedRowNumbers.Count > 0)
        {
            throw new BusinessRuleException(
                $"Rows.RowNumber contains duplicates: {string.Join(",", duplicatedRowNumbers)}.");
        }

        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(request.PortfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", request.PortfolioId);

        if (portfolio.UserId != userId)
            throw new AccessDeniedException();

        var replayResponse = await TryResolveReplayResponseAsync(request.SessionId, request.PortfolioId, userId, cancellationToken);
        if (replayResponse is not null)
        {
            return replayResponse;
        }

        var executionStartedAtUtc = DateTime.UtcNow;

        var previewSessionForOwnership = await stockImportSessionStore.GetAsync(request.SessionId, cancellationToken);
        if (previewSessionForOwnership is null)
        {
            throw new BusinessRuleException(MessageSessionNotFound);
        }

        if (previewSessionForOwnership.UserId != userId || previewSessionForOwnership.PortfolioId != request.PortfolioId)
        {
            throw new AccessDeniedException("Import session does not match current user or portfolio.");
        }

        var started = await stockImportSessionStore.TryStartExecutionAsync(
            request.SessionId,
            userId,
            request.PortfolioId,
            cancellationToken);

        if (!started)
        {
            var secondReplayResponse = await TryResolveReplayResponseAsync(request.SessionId, request.PortfolioId, userId, cancellationToken);
            if (secondReplayResponse is not null)
            {
                return secondReplayResponse;
            }

            throw new BusinessRuleException(MessageExecutionInProgress);
        }

        try
        {
            await using var tx = await transactionManager.BeginTransactionAsync(cancellationToken);

            var session = await stockImportSessionStore.TryConsumeForOwnerAsync(
                request.SessionId,
                userId,
                request.PortfolioId,
                cancellationToken);

            if (session is null)
            {
                await tx.RollbackAsync(cancellationToken);
                throw new BusinessRuleException(MessageSessionNotFound);
            }

            return await ExecuteWithConsumedSessionAsync(
                request,
                portfolio,
                userId,
                session,
                executionStartedAtUtc,
                tx,
                cancellationToken);
        }
        catch (Exception ex)
        {
            await PersistFailedExecutionStateIfProcessingAsync(
                request.SessionId,
                userId,
                request.PortfolioId,
                executionStartedAtUtc);

            if (ex is BusinessRuleException or EntityNotFoundException or AccessDeniedException)
            {
                throw;
            }

            if (ex is InvalidOperationException invalidOperationException
                && string.Equals(invalidOperationException.Message, MessageAtomicRejectedRollback, StringComparison.Ordinal))
            {
                throw;
            }

            throw new InvalidOperationException(MessageAtomicRejectedRollback, ex);
        }
    }

    public Task<StockImportExecuteStatusResponseDto> GetStatusAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
        => new QueryStockImportSessionUseCase(stockImportSessionStore, currentUserService)
            .ExecuteAsync(sessionId, cancellationToken);

    private async Task<StockImportExecuteResponseDto?> TryResolveReplayResponseAsync(
        Guid sessionId,
        Guid portfolioId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var executionState = await stockImportSessionStore.GetExecutionStateAsync(sessionId, cancellationToken);
        if (executionState is null)
        {
            return null;
        }

        if (executionState.UserId != userId || executionState.PortfolioId != portfolioId)
        {
            throw new AccessDeniedException("Import session does not match current user or portfolio.");
        }

        if (string.Equals(executionState.ExecutionStatus, ExecutionStatusProcessing, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(MessageExecutionInProgress);
        }

        if (string.Equals(executionState.ExecutionStatus, ExecutionStatusCompleted, StringComparison.OrdinalIgnoreCase)
            && executionState.Result is not null)
        {
            return executionState.Result with
            {
                SessionId = sessionId,
                IsReplay = true
            };
        }

        return null;
    }

    private async Task PersistFailedExecutionStateIfProcessingAsync(
        Guid sessionId,
        Guid userId,
        Guid portfolioId,
        DateTime executionStartedAtUtc)
    {
        try
        {
            var latestExecutionState = await stockImportSessionStore.GetExecutionStateAsync(
                sessionId,
                CancellationToken.None);

            if (latestExecutionState is null)
            {
                return;
            }

            if (latestExecutionState.UserId != userId || latestExecutionState.PortfolioId != portfolioId)
            {
                return;
            }

            if (!string.Equals(latestExecutionState.ExecutionStatus, ExecutionStatusProcessing, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await stockImportSessionStore.SaveExecutionResultAsync(
                new StockImportExecuteSessionStateDto
                {
                    SessionId = sessionId,
                    UserId = userId,
                    PortfolioId = portfolioId,
                    ExecutionStatus = ExecutionStatusFailed,
                    Message = MessageAtomicRejectedRollback,
                    StartedAtUtc = executionStartedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    Result = null
                },
                CancellationToken.None);
        }
        catch (Exception persistEx)
        {
            logger.LogError(
                persistEx,
                "Stock import execute failed-state persistence failed. SessionId={SessionId}",
                sessionId);
        }
    }

    private async Task<StockImportExecuteResponseDto> ExecuteWithConsumedSessionAsync(
        ExecuteStockImportRequest request,
        Domain.Entities.Portfolio portfolio,
        Guid userId,
        StockImportSessionSnapshotDto session,
        DateTime executionStartedAtUtc,
        IAppDbTransaction tx,
        CancellationToken cancellationToken)
    {
        var sessionRowsByNumber = session.Rows.ToDictionary(r => r.RowNumber);

        var results = new List<StockImportExecuteRowResultDto>(request.Rows.Count);
        var diagnostics = new List<StockImportDiagnosticDto>();

        logger.LogInformation(
            "Stock import execute started. PortfolioId={PortfolioId}, SessionId={SessionId}, UserId={UserId}, RequestedRows={RequestedRows}",
            request.PortfolioId,
            request.SessionId,
            userId,
            request.Rows.Count);

        var insertedRows = 0;
        var failedRows = 0;
        var hasBrokerStatementBalanceActionRequiredFailure = false;

        var boundLedger = await currencyLedgerRepository.GetByIdAsync(portfolio.BoundCurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", portfolio.BoundCurrencyLedgerId);

        try
        {
            var knownTransactionsInExecution = (await stockTransactionRepository.GetByPortfolioIdAsync(
                portfolio.Id,
                cancellationToken)).ToList();

            var knownLedgerTransactionsInExecution = (await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                boundLedger.Id,
                cancellationToken)).ToList();

            DateOnly? earliestAffectedMonth = null;

            var sessionBaselineAnchorDate = ResolveSessionBaselineAnchorDate(
                session.SelectedFormat,
                session,
                request.Rows,
                sessionRowsByNumber);

            var isBrokerStatementImport = string.Equals(
                session.SelectedFormat,
                StockImportParser.FormatBrokerStatement,
                StringComparison.Ordinal);

            var openingLedgerTransaction = BuildOpeningLedgerBaselineTransaction(
                session,
                boundLedger,
                request.Rows,
                sessionRowsByNumber,
                sessionBaselineAnchorDate);

            if (openingLedgerTransaction is not null)
            {
                await currencyTransactionRepository.AddAsync(openingLedgerTransaction, cancellationToken);
                knownLedgerTransactionsInExecution.Add(openingLedgerTransaction);

                await txSnapshotService.UpsertSnapshotAsync(
                    portfolio.Id,
                    openingLedgerTransaction.Id,
                    openingLedgerTransaction.TransactionDate,
                    cancellationToken);

                TrackEarliestAffectedMonth(
                    ref earliestAffectedMonth,
                    openingLedgerTransaction.TransactionDate);
            }

            var rowsToExecute = request.Rows
                .OrderBy(row =>
                {
                    if (sessionRowsByNumber.TryGetValue(row.RowNumber, out var sessionRowForOrdering)
                        && sessionRowForOrdering.TradeDate.HasValue)
                    {
                        return sessionRowForOrdering.TradeDate.Value.Date;
                    }

                    return DateTime.MaxValue;
                })
                .ThenBy(row =>
                {
                    if (!sessionRowsByNumber.TryGetValue(row.RowNumber, out var sessionRowForOrdering))
                    {
                        return 2;
                    }

                    var normalizedTradeSideForOrdering = NormalizeConfirmedTradeSide(row.ConfirmedTradeSide)
                        ?? NormalizeConfirmedTradeSide(sessionRowForOrdering.ConfirmedTradeSide)
                        ?? NormalizeConfirmedTradeSide(sessionRowForOrdering.TradeSide);

                    return normalizedTradeSideForOrdering switch
                    {
                        TradeSideSell => 0,
                        TradeSideBuy => 1,
                        _ => 2
                    };
                })
                .ThenBy(row => row.RowNumber);

            foreach (var row in rowsToExecute)
            {
                if (!sessionRowsByNumber.TryGetValue(row.RowNumber, out var sessionRow))
                {
                    AddFailure(
                        row,
                        results,
                        diagnostics,
                        errorCode: ErrorCodeSessionRowMismatch,
                        fieldName: FieldRowNumber,
                        invalidValue: row.RowNumber.ToString(),
                        message: "此列不屬於目前預覽 Session。",
                        correctionGuidance: "請重新預覽後再執行。",
                        confirmedTradeSide: NormalizeConfirmedTradeSide(row.ConfirmedTradeSide),
                        logger: logger,
                        sellBeforeBuyDecision: BuildSellBeforeBuyDecisionContext(
                            SellBeforeBuyAction.None,
                            null,
                            SellBeforeBuyReasonNotApplicable));
                    failedRows++;
                    continue;
                }

                var normalizedTradeSide = NormalizeConfirmedTradeSide(row.ConfirmedTradeSide)
                    ?? NormalizeConfirmedTradeSide(sessionRow.ConfirmedTradeSide)
                    ?? NormalizeConfirmedTradeSide(sessionRow.TradeSide);

                if (row.Exclude)
                {
                    results.Add(new StockImportExecuteRowResultDto
                    {
                        RowNumber = row.RowNumber,
                        Success = true,
                        Message = MessageExcluded,
                        ConfirmedTradeSide = normalizedTradeSide,
                        TransactionId = null,
                        SellBeforeBuyDecision = BuildSellBeforeBuyDecisionContext(
                            SellBeforeBuyAction.None,
                            null,
                            SellBeforeBuyReasonNotApplicable)
                    });
                    continue;
                }

                var normalizedTicker = NormalizeTicker(row.Ticker) ?? NormalizeTicker(sessionRow.Ticker);
                if (string.IsNullOrWhiteSpace(normalizedTicker))
                {
                    AddFailure(
                        row,
                        results,
                        diagnostics,
                        errorCode: ErrorCodeSymbolUnresolved,
                        fieldName: FieldTicker,
                        invalidValue: row.Ticker,
                        message: "此列尚未解析股票代號。",
                        correctionGuidance: "請先填入 ticker，或將此列排除後再執行。",
                        confirmedTradeSide: normalizedTradeSide,
                        logger: logger,
                        sellBeforeBuyDecision: BuildSellBeforeBuyDecisionContext(
                            SellBeforeBuyAction.None,
                            null,
                            SellBeforeBuyReasonNotApplicable));
                    failedRows++;
                    continue;
                }

                if (normalizedTradeSide is null)
                {
                    AddFailure(
                        row,
                        results,
                        diagnostics,
                        errorCode: ErrorCodeTradeSideConfirmationRequired,
                        fieldName: FieldConfirmedTradeSide,
                        invalidValue: row.ConfirmedTradeSide,
                        message: "此列尚未確認買賣方向。",
                        correctionGuidance: "請先確認此列為 buy 或 sell，或將此列排除後再執行。",
                        confirmedTradeSide: null,
                        logger: logger,
                        sellBeforeBuyDecision: BuildSellBeforeBuyDecisionContext(
                            SellBeforeBuyAction.None,
                            null,
                            SellBeforeBuyReasonNotApplicable));
                    failedRows++;
                    continue;
                }

                if (sessionRow.IsInvalid)
                {
                    AddFailure(
                        row,
                        results,
                        diagnostics,
                        errorCode: ErrorCodeSymbolUnresolved,
                        fieldName: FieldTicker,
                        invalidValue: normalizedTicker,
                        message: "此列在預覽結果中為無效列，無法執行。",
                        correctionGuidance: "請修正資料後重新預覽，或將此列排除。",
                        confirmedTradeSide: normalizedTradeSide,
                        logger: logger,
                        sellBeforeBuyDecision: BuildSellBeforeBuyDecisionContext(
                            SellBeforeBuyAction.None,
                            null,
                            SellBeforeBuyReasonNotApplicable));
                    failedRows++;
                    continue;
                }

                if (sessionRow.TradeDate is null || sessionRow.Quantity is null || sessionRow.UnitPrice is null)
                {
                    AddFailure(
                        row,
                        results,
                        diagnostics,
                        errorCode: ErrorCodeSymbolUnresolved,
                        fieldName: FieldTicker,
                        invalidValue: normalizedTicker,
                        message: "此列缺少建立交易所需資料。",
                        correctionGuidance: "請修正資料後重新預覽，或將此列排除。",
                        confirmedTradeSide: normalizedTradeSide,
                        logger: logger,
                        sellBeforeBuyDecision: BuildSellBeforeBuyDecisionContext(
                            SellBeforeBuyAction.None,
                            null,
                            SellBeforeBuyReasonNotApplicable));
                    failedRows++;
                    continue;
                }

                try
                {
                    var creationResult = await CreateImportedTransactionAsync(
                        portfolio,
                        boundLedger,
                        session,
                        sessionBaselineAnchorDate,
                        sessionRow,
                        normalizedTicker,
                        normalizedTradeSide,
                        row,
                        request.BaselineDecision,
                        request.DefaultBalanceAction,
                        knownTransactionsInExecution,
                        knownLedgerTransactionsInExecution,
                        cancellationToken);

                    var createdTransaction = creationResult.Transaction;

                    insertedRows++;
                    var createdTransactions = creationResult.CreatedTransactions;
                    if (createdTransactions.Count > 0)
                    {
                        knownTransactionsInExecution.AddRange(createdTransactions);

                        foreach (var createdTx in createdTransactions)
                        {
                            TrackEarliestAffectedMonth(ref earliestAffectedMonth, createdTx.TransactionDate);
                        }
                    }

                    if (creationResult.UsesPartialHistoryAssumption)
                    {
                        diagnostics.Add(new StockImportDiagnosticDto
                        {
                            RowNumber = row.RowNumber,
                            FieldName = FieldPosition,
                            InvalidValue = normalizedTradeSide,
                            ErrorCode = WarningCodePartialPeriodAssumption,
                            Message = WarningMessagePartialPeriodAssumption,
                            CorrectionGuidance = WarningCorrectionGuidancePartialPeriodAssumption
                        });
                    }

                    results.Add(new StockImportExecuteRowResultDto
                    {
                        RowNumber = row.RowNumber,
                        Success = true,
                        TransactionId = createdTransaction.Id,
                        Message = MessageCreated,
                        ConfirmedTradeSide = normalizedTradeSide,
                        SellBeforeBuyDecision = creationResult.SellBeforeBuyDecision
                    });
                }
                catch (StockImportRowFailureException ex)
                {
                    AddFailure(
                        row,
                        results,
                        diagnostics,
                        errorCode: ex.ErrorCode,
                        fieldName: ex.FieldName,
                        invalidValue: ex.InvalidValue ?? ResolveInvalidValueForField(ex.FieldName, normalizedTradeSide, row),
                        message: ex.Message,
                        correctionGuidance: ex.CorrectionGuidance,
                        confirmedTradeSide: normalizedTradeSide,
                        balanceDecision: ex.BalanceDecision ?? BuildBalanceDecisionContext(request.DefaultBalanceAction, row),
                        sellBeforeBuyDecision: ex.SellBeforeBuyDecision,
                        logger: logger);
                    failedRows++;

                    if (isBrokerStatementImport
                        && string.Equals(ex.ErrorCode, ErrorCodeBalanceActionRequired, StringComparison.Ordinal))
                    {
                        hasBrokerStatementBalanceActionRequiredFailure = true;
                    }
                }
                catch (BusinessRuleException ex)
                {
                    var (fieldName, invalidValue, message, correctionGuidance) =
                        BuildBusinessRuleFailureDetail(ex.Message, row, normalizedTradeSide);

                    AddFailure(
                        row,
                        results,
                        diagnostics,
                        errorCode: ErrorCodeBusinessRuleViolation,
                        fieldName: fieldName,
                        invalidValue: invalidValue,
                        message: message,
                        correctionGuidance: correctionGuidance,
                        confirmedTradeSide: normalizedTradeSide,
                        balanceDecision: BuildBalanceDecisionContext(request.DefaultBalanceAction, row),
                        sellBeforeBuyDecision: BuildSellBeforeBuyDecisionContext(
                            SellBeforeBuyAction.None,
                            null,
                            SellBeforeBuyReasonNotApplicable),
                        logger: logger);
                    failedRows++;
                }
            }

            var shouldRollbackForBrokerStatementBalanceFailure =
                isBrokerStatementImport && hasBrokerStatementBalanceActionRequiredFailure;

            var shouldRollbackForRejected = shouldRollbackForBrokerStatementBalanceFailure || failedRows > 0;

            if (shouldRollbackForRejected)
            {
                await tx.RollbackAsync(cancellationToken);

                insertedRows = 0;

                for (var i = 0; i < results.Count; i++)
                {
                    if (results[i].TransactionId.HasValue)
                    {
                        results[i] = results[i] with
                        {
                            TransactionId = null,
                            Message = shouldRollbackForBrokerStatementBalanceFailure
                                ? MessageBrokerStatementAtomicRollbackDueToBalanceAction
                                : MessageAtomicRejectedRollback
                        };
                    }
                }
            }
            else
            {
                if (earliestAffectedMonth is DateOnly invalidationMonth)
                {
                    await monthlySnapshotService.InvalidateFromMonthAsync(
                        portfolio.Id,
                        invalidationMonth,
                        cancellationToken);
                }

                var committedResponse = new StockImportExecuteResponseDto
                {
                    SessionId = request.SessionId,
                    IsReplay = false,
                    Status = StatusCommitted,
                    Summary = new StockImportExecuteSummaryDto
                    {
                        TotalRows = request.Rows.Count,
                        InsertedRows = insertedRows,
                        FailedRows = failedRows,
                        ErrorCount = diagnostics.Count
                    },
                    Results = results,
                    Errors = diagnostics
                };

                await stockImportSessionStore.SaveExecutionResultAsync(
                    new StockImportExecuteSessionStateDto
                    {
                        SessionId = request.SessionId,
                        UserId = userId,
                        PortfolioId = request.PortfolioId,
                        ExecutionStatus = ExecutionStatusCompleted,
                        Message = null,
                        StartedAtUtc = executionStartedAtUtc,
                        CompletedAtUtc = DateTime.UtcNow,
                        Result = committedResponse
                    },
                    cancellationToken);

                await tx.CommitAsync(cancellationToken);

                logger.LogInformation(
                    "Stock import execute completed. PortfolioId={PortfolioId}, SessionId={SessionId}, Status={Status}, RequestedRows={RequestedRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}, ErrorCount={ErrorCount}",
                    request.PortfolioId,
                    request.SessionId,
                    committedResponse.Status,
                    request.Rows.Count,
                    insertedRows,
                    failedRows,
                    diagnostics.Count);

                return committedResponse;
            }

            var status = StatusRejected;

            logger.LogInformation(
                "Stock import execute completed. PortfolioId={PortfolioId}, SessionId={SessionId}, Status={Status}, RequestedRows={RequestedRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}, ErrorCount={ErrorCount}",
                request.PortfolioId,
                request.SessionId,
                status,
                request.Rows.Count,
                insertedRows,
                failedRows,
                diagnostics.Count);

            var response = new StockImportExecuteResponseDto
            {
                SessionId = request.SessionId,
                IsReplay = false,
                Status = status,
                Summary = new StockImportExecuteSummaryDto
                {
                    TotalRows = request.Rows.Count,
                    InsertedRows = insertedRows,
                    FailedRows = failedRows,
                    ErrorCount = diagnostics.Count
                },
                Results = results,
                Errors = diagnostics
            };

            await stockImportSessionStore.SaveExecutionResultAsync(
                new StockImportExecuteSessionStateDto
                {
                    SessionId = request.SessionId,
                    UserId = userId,
                    PortfolioId = request.PortfolioId,
                    ExecutionStatus = ExecutionStatusCompleted,
                    Message = null,
                    StartedAtUtc = executionStartedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    Result = response
                },
                cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Stock import execute operation failed. PortfolioId={PortfolioId}, SessionId={SessionId}, RequestedRows={RequestedRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}",
                request.PortfolioId,
                request.SessionId,
                request.Rows.Count,
                insertedRows,
                failedRows);

            await tx.RollbackAsync(cancellationToken);

            await stockImportSessionStore.SaveExecutionResultAsync(
                new StockImportExecuteSessionStateDto
                {
                    SessionId = request.SessionId,
                    UserId = userId,
                    PortfolioId = request.PortfolioId,
                    ExecutionStatus = ExecutionStatusFailed,
                    Message = MessageAtomicRejectedRollback,
                    StartedAtUtc = executionStartedAtUtc,
                    CompletedAtUtc = DateTime.UtcNow,
                    Result = null
                },
                cancellationToken);

            if (ex is BusinessRuleException or EntityNotFoundException or AccessDeniedException)
            {
                throw;
            }

            throw new InvalidOperationException(MessageAtomicRejectedRollback, ex);
        }
    }

    private async Task<ImportedStockTransactionCreationResult> CreateImportedTransactionAsync(
        Domain.Entities.Portfolio portfolio,
        Domain.Entities.CurrencyLedger boundLedger,
        StockImportSessionSnapshotDto sessionSnapshot,
        DateTime sessionBaselineAnchorDate,
        StockImportSessionRowSnapshotDto sessionRow,
        string normalizedTicker,
        string normalizedTradeSide,
        ExecuteStockImportRowRequest requestRow,
        StockImportBaselineExecutionDecisionRequest? baselineDecision,
        StockImportDefaultBalanceDecisionRequest? defaultBalanceDecision,
        List<StockTransaction> knownTransactionsInExecution,
        List<CurrencyTransaction> knownLedgerTransactionsInExecution,
        CancellationToken cancellationToken)
    {
        var transactionType = ResolveTransactionType(normalizedTradeSide);
        var balanceDecision = ResolveBalanceDecision(requestRow, defaultBalanceDecision, boundLedger);
        var sellBeforeBuyDecision = ResolveSellBeforeBuyDecision(requestRow, baselineDecision);
        var sellBeforeBuyAction = sellBeforeBuyDecision.Action;

        var transactionsForPosition = (IReadOnlyList<StockTransaction>)knownTransactionsInExecution;

        var parsedSessionCurrency = ParseCurrency(sessionRow.Currency);
        var marketResolution = ResolveImportMarket(
            normalizedTicker,
            parsedSessionCurrency,
            transactionsForPosition);

        if (!marketResolution.HasValue)
        {
            throw new StockImportRowFailureException(
                errorCode: ErrorCodeMarketResolutionRequired,
                fieldName: FieldMarket,
                message: "無法唯一判斷市場，請先分開匯入或補上可識別市場的資訊。",
                correctionGuidance: "當同一 ticker 存在多市場持股時，請提供可辨識市場資訊（例如對應幣別）或分批匯入。",
                invalidValue: normalizedTicker);
        }

        var market = marketResolution.Value;
        var currency = parsedSessionCurrency ?? StockTransaction.GuessCurrencyFromMarket(market);

        StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(currency, boundLedger);

        var tradeDate = DateTime.SpecifyKind(sessionRow.TradeDate!.Value.Date, DateTimeKind.Utc);
        var shares = sessionRow.Quantity!.Value;
        var pricePerShare = sessionRow.UnitPrice!.Value;
        var fees = sessionRow.Fees + sessionRow.Taxes;

        var subtotal = shares * pricePerShare;
        if (market == StockMarket.TW)
            subtotal = Math.Floor(subtotal);

        var grossAmount = subtotal + fees;

        decimal? exchangeRate;
        decimal? marketRate = null;
        var fxFromCurrency = boundLedger.CurrencyCode;
        var fxToCurrency = boundLedger.HomeCurrency;
        var requiresFxConversion = !string.Equals(fxFromCurrency, fxToCurrency, StringComparison.OrdinalIgnoreCase);
        var ledgerTransactions = (IReadOnlyList<CurrencyTransaction>)knownLedgerTransactionsInExecution;

        if (!requiresFxConversion)
        {
            exchangeRate = 1.0m;
        }
        else
        {
            var lifoRate = currencyLedgerService.CalculateExchangeRateForPurchase(
                ledgerTransactions,
                tradeDate,
                grossAmount);

            if (lifoRate > 0)
            {
                exchangeRate = lifoRate;
            }
            else
            {
                var fxResult = await txDateFxService.GetOrFetchAsync(
                    fxFromCurrency,
                    fxToCurrency,
                    tradeDate,
                    cancellationToken);

                if (fxResult is null)
                    throw new BusinessRuleException("無法計算匯率，請先在帳本中建立換匯紀錄");

                marketRate = fxResult.Rate;
                exchangeRate = marketRate;
            }
        }

        var usesPartialHistoryAssumption = false;
        var pendingSeededStockTransactions = new List<StockTransaction>();
        var pendingCurrencyTransactions = new List<CurrencyTransaction>();
        var pendingTopUpTransactions = new List<CurrencyTransaction>();

        decimal? realizedPnlHome = null;
        if (transactionType == TransactionType.Sell)
        {
            var currentPosition = portfolioCalculator.CalculatePositionByMarket(
                normalizedTicker,
                market,
                transactionsForPosition);

            if (currentPosition.TotalShares < shares)
            {
                if (currentPosition.TotalShares <= 0m)
                {
                    var seededOpeningPosition = await BuildOpeningAdjustmentFromBaselineAsync(
                        boundLedger,
                        sessionSnapshot,
                        sessionBaselineAnchorDate,
                        normalizedTicker,
                        market,
                        currency,
                        sellBeforeBuyAction,
                        requiredShares: shares,
                        portfolio.Id,
                        boundLedger.Id,
                        cancellationToken);

                    if (seededOpeningPosition is not null)
                    {
                        var transactionsWithSeededOpeningPosition =
                            new List<StockTransaction>(transactionsForPosition.Count + 1)
                            {
                                seededOpeningPosition
                            };
                        transactionsWithSeededOpeningPosition.AddRange(transactionsForPosition);

                        currentPosition = portfolioCalculator.CalculatePositionByMarket(
                            normalizedTicker,
                            market,
                            transactionsWithSeededOpeningPosition);

                        if (currentPosition.TotalShares < shares)
                        {
                            throw new BusinessRuleException($"持股不足。可賣出: {currentPosition.TotalShares:F4}，欲賣出: {shares:F4}");
                        }

                        await stockTransactionRepository.AddAsync(seededOpeningPosition, cancellationToken);

                        if (seededOpeningPosition.MarketValueAtImport is decimal openingMarketValueAtImport
                            && openingMarketValueAtImport > 0m)
                        {
                            decimal? openingHomeAmount = null;
                            decimal? openingExchangeRate = null;
                            if (string.Equals(boundLedger.CurrencyCode, boundLedger.HomeCurrency, StringComparison.OrdinalIgnoreCase))
                            {
                                openingExchangeRate = 1.0m;
                                openingHomeAmount = openingMarketValueAtImport;
                            }

                            var openingInitialBalanceTransaction = new CurrencyTransaction(
                                boundLedger.Id,
                                seededOpeningPosition.TransactionDate,
                                CurrencyTransactionType.InitialBalance,
                                openingMarketValueAtImport,
                                homeAmount: openingHomeAmount,
                                exchangeRate: openingExchangeRate,
                                relatedStockTransactionId: seededOpeningPosition.Id,
                                notes: ImportExecuteOpeningInitialBalanceNote);

                            pendingCurrencyTransactions.Add(openingInitialBalanceTransaction);

                            if (string.Equals(
                                sessionSnapshot.SelectedFormat,
                                StockImportParser.FormatBrokerStatement,
                                StringComparison.Ordinal))
                            {
                                var openingOffsetExchangeRate = openingExchangeRate ?? seededOpeningPosition.ExchangeRate;
                                var openingOffsetHomeAmount = openingHomeAmount;

                                if (!openingOffsetHomeAmount.HasValue && openingOffsetExchangeRate.HasValue)
                                {
                                    openingOffsetHomeAmount = Math.Round(
                                        openingMarketValueAtImport * openingOffsetExchangeRate.Value,
                                        2,
                                        MidpointRounding.AwayFromZero);
                                }

                                var openingInitialBalanceOffsetTransaction = new CurrencyTransaction(
                                    boundLedger.Id,
                                    seededOpeningPosition.TransactionDate,
                                    CurrencyTransactionType.Spend,
                                    openingMarketValueAtImport,
                                    homeAmount: openingOffsetHomeAmount,
                                    exchangeRate: openingOffsetExchangeRate,
                                    relatedStockTransactionId: seededOpeningPosition.Id,
                                    notes: ImportExecuteOpeningInitialBalanceOffsetNote);

                                pendingCurrencyTransactions.Add(openingInitialBalanceOffsetTransaction);
                            }
                        }

                        await txSnapshotService.UpsertSnapshotAsync(
                            portfolio.Id,
                            seededOpeningPosition.Id,
                            seededOpeningPosition.TransactionDate,
                            cancellationToken);

                        pendingSeededStockTransactions.Add(seededOpeningPosition);
                        usesPartialHistoryAssumption = true;
                    }
                    else
                    {
                        throw CreateSellBeforeBuyActionFailure(
                            requestRow,
                            normalizedTicker,
                            market,
                            shares,
                            currentPosition.TotalShares,
                            sellBeforeBuyDecision.DecisionScope,
                            sellBeforeBuyDecision.Reason,
                            sellBeforeBuyDecision.Action);
                    }
                }
                else
                {
                    throw new BusinessRuleException($"持股不足。可賣出: {currentPosition.TotalShares:F4}，欲賣出: {shares:F4}");
                }
            }

            if (currentPosition.TotalShares >= shares)
            {
                var tempSellTransaction = new StockTransaction(
                    portfolio.Id,
                    tradeDate,
                    normalizedTicker,
                    transactionType,
                    shares,
                    pricePerShare,
                    exchangeRate,
                    fees,
                    boundLedger.Id,
                    notes: null,
                    market,
                    currency);

                realizedPnlHome = portfolioCalculator.CalculateRealizedPnl(
                    currentPosition,
                    tempSellTransaction);
            }
        }

        var transaction = new StockTransaction(
            portfolio.Id,
            tradeDate,
            normalizedTicker,
            transactionType,
            shares,
            pricePerShare,
            exchangeRate,
            fees,
            boundLedger.Id,
            notes: null,
            market,
            currency);

        if (realizedPnlHome.HasValue)
            transaction.SetRealizedPnl(realizedPnlHome.Value);

        var linkedSpec = StockTransactionLinking.BuildLinkedCurrencyTransactionSpec(
            transactionType,
            transaction,
            boundLedger);

        if (linkedSpec != null)
        {
            if (transactionType == TransactionType.Buy)
            {
                var effectiveLedgerTransactions = ledgerTransactions
                    .Concat(pendingCurrencyTransactions)
                    .ToList();

                var currentBalance = CalculateBalanceAsOfDate(
                    currencyLedgerService,
                    effectiveLedgerTransactions,
                    tradeDate);
                var shortfall = linkedSpec.Amount - currentBalance;

                if (shortfall > 0)
                {
                    var balanceDecisionContext = BuildBalanceDecisionContext(
                        defaultBalanceDecision,
                        requestRow,
                        requiredAmount: linkedSpec.Amount,
                        availableBalance: currentBalance,
                        shortfall: shortfall);

                    var decisionValidation = StockBalanceActionRules.ValidateShortfallDecision(
                        balanceDecision.Action,
                        balanceDecision.TopUpTransactionType);

                    if (decisionValidation is StockBalanceDecisionValidationError validationError)
                    {
                        throw CreateBalanceActionFailure(
                            message: validationError.Message,
                            action: balanceDecision.Action,
                            topUpTransactionType: balanceDecision.TopUpTransactionType,
                            requiredAmount: linkedSpec.Amount,
                            availableBalance: currentBalance,
                            shortfall: shortfall,
                            decisionScope: balanceDecisionContext?.DecisionScope,
                            fieldName: validationError.FieldName,
                            invalidValue: validationError.InvalidValue);
                    }

                    switch (balanceDecision.Action)
                    {
                        case BalanceAction.Margin:
                        {
                            if (marketRate == null)
                            {
                                if (!requiresFxConversion)
                                {
                                    marketRate = 1.0m;
                                }
                                else
                                {
                                    var fxResult = await txDateFxService.GetOrFetchAsync(
                                        fxFromCurrency,
                                        fxToCurrency,
                                        tradeDate,
                                        cancellationToken);
                                    marketRate = fxResult?.Rate;
                                }
                            }

                            var marginMarketRate = marketRate ?? exchangeRate;
                            if (!marginMarketRate.HasValue)
                            {
                                throw CreateBalanceActionFailure(
                                    message: "無法計算匯率，請先在帳本中建立換匯紀錄",
                                    action: balanceDecision.Action,
                                    topUpTransactionType: balanceDecision.TopUpTransactionType,
                                    requiredAmount: linkedSpec.Amount,
                                    availableBalance: currentBalance,
                                    shortfall: shortfall,
                                    decisionScope: balanceDecisionContext?.DecisionScope);
                            }

                            var blendedRate = currencyLedgerService.CalculateExchangeRateWithMargin(
                                ledgerTransactions,
                                tradeDate,
                                grossAmount,
                                currentBalance,
                                marginMarketRate.Value);

                            transaction.SetExchangeRate(blendedRate);
                            break;
                        }

                        case BalanceAction.TopUp:
                        {
                            var topUpTransactionType = balanceDecision.TopUpTransactionType!.Value;

                            CurrencyTransactionTypePolicyValidationResult? topUpTypeValidationResult = null;
                            try
                            {
                                CurrencyTransactionTypePolicy.EnsureValidOrThrow(
                                    boundLedger.CurrencyCode,
                                    topUpTransactionType);
                            }
                            catch (BusinessRuleException)
                            {
                                topUpTypeValidationResult = CurrencyTransactionTypePolicy.Validate(
                                    boundLedger.CurrencyCode,
                                    topUpTransactionType);
                            }

                            if (topUpTypeValidationResult is { IsValid: false })
                            {
                                var firstDiagnostic = topUpTypeValidationResult.Diagnostics[0];
                                throw CreateBalanceActionFailure(
                                    message: $"{firstDiagnostic.Message} {firstDiagnostic.CorrectionGuidance}".Trim(),
                                    action: balanceDecision.Action,
                                    topUpTransactionType: balanceDecision.TopUpTransactionType,
                                    requiredAmount: linkedSpec.Amount,
                                    availableBalance: currentBalance,
                                    shortfall: shortfall,
                                    decisionScope: balanceDecisionContext?.DecisionScope,
                                    fieldName: StockBalanceActionRules.FieldTopUpTransactionType,
                                    invalidValue: topUpTransactionType.ToString());
                            }

                            var topUpAmount = shortfall;
                            if (topUpAmount <= 0m)
                            {
                                break;
                            }

                            decimal? topUpExchangeRate = null;
                            decimal? topUpHomeAmount = null;

                            if (boundLedger.CurrencyCode == boundLedger.HomeCurrency)
                            {
                                topUpExchangeRate = 1.0m;
                                topUpHomeAmount = topUpAmount;
                            }

                            if (topUpTransactionType == CurrencyTransactionType.ExchangeBuy && topUpExchangeRate == null)
                            {
                                if (marketRate == null)
                                {
                                    if (!requiresFxConversion)
                                    {
                                        marketRate = 1.0m;
                                    }
                                    else
                                    {
                                        var fxResult = await txDateFxService.GetOrFetchAsync(
                                            fxFromCurrency,
                                            fxToCurrency,
                                            tradeDate,
                                            cancellationToken);
                                        marketRate = fxResult?.Rate;
                                    }
                                }

                                if (marketRate == null)
                                {
                                    throw CreateBalanceActionFailure(
                                        message: "無法取得市場匯率，請選擇其他交易類型或手動在帳本新增換匯紀錄",
                                        action: balanceDecision.Action,
                                        topUpTransactionType: balanceDecision.TopUpTransactionType,
                                        requiredAmount: linkedSpec.Amount,
                                        availableBalance: currentBalance,
                                        shortfall: shortfall,
                                        decisionScope: balanceDecisionContext?.DecisionScope);
                                }

                                topUpExchangeRate = marketRate;
                                topUpHomeAmount = Math.Round(topUpAmount * marketRate.Value, 2);
                            }

                            var topUpTransaction = new CurrencyTransaction(
                                boundLedger.Id,
                                tradeDate,
                                topUpTransactionType,
                                topUpAmount,
                                homeAmount: topUpHomeAmount,
                                exchangeRate: topUpExchangeRate,
                                relatedStockTransactionId: transaction.Id,
                                notes: $"補足買入 {normalizedTicker} 差額");

                            pendingCurrencyTransactions.Add(topUpTransaction);
                            pendingTopUpTransactions.Add(topUpTransaction);

                            var postTopUpLedgerTransactions = ledgerTransactions
                                .Concat(pendingCurrencyTransactions)
                                .ToList();

                            var newLifoRate = currencyLedgerService.CalculateExchangeRateForPurchase(
                                postTopUpLedgerTransactions,
                                tradeDate,
                                grossAmount);

                            if (newLifoRate > 0)
                                transaction.SetExchangeRate(newLifoRate);

                            break;
                        }

                        default:
                            throw CreateBalanceActionFailure(
                                message: "帳本餘額不足，請選擇處理方式",
                                action: balanceDecision.Action,
                                topUpTransactionType: balanceDecision.TopUpTransactionType,
                                requiredAmount: linkedSpec.Amount,
                                availableBalance: currentBalance,
                                shortfall: shortfall,
                                decisionScope: balanceDecisionContext?.DecisionScope);
                    }
                }
            }

            decimal? linkedHomeAmount = null;
            decimal? linkedExchangeRate = null;
            if (boundLedger.CurrencyCode == boundLedger.HomeCurrency)
            {
                linkedExchangeRate = 1.0m;
                linkedHomeAmount = linkedSpec.Amount;
            }

            var noteAction = linkedSpec.TransactionType == CurrencyTransactionType.Spend ? "買入" : "賣出";
            var currencyTransaction = new CurrencyTransaction(
                boundLedger.Id,
                tradeDate,
                linkedSpec.TransactionType,
                linkedSpec.Amount,
                homeAmount: linkedHomeAmount,
                exchangeRate: linkedExchangeRate,
                relatedStockTransactionId: transaction.Id,
                notes: $"{noteAction} {normalizedTicker} × {shares}");

            pendingCurrencyTransactions.Add(currencyTransaction);
        }

        await stockTransactionRepository.AddAsync(transaction, cancellationToken);

        foreach (var pendingCurrencyTransaction in pendingCurrencyTransactions)
        {
            await currencyTransactionRepository.AddAsync(pendingCurrencyTransaction, cancellationToken);
        }

        foreach (var pendingTopUpTransaction in pendingTopUpTransactions)
        {
            await txSnapshotService.UpsertSnapshotAsync(
                portfolio.Id,
                pendingTopUpTransaction.Id,
                pendingTopUpTransaction.TransactionDate,
                cancellationToken);
        }

        await txSnapshotService.UpsertSnapshotAsync(
            portfolio.Id,
            transaction.Id,
            transaction.TransactionDate,
            cancellationToken);

        var createdTransactions = pendingSeededStockTransactions.Count > 0
            ? new List<StockTransaction>(pendingSeededStockTransactions.Count + 1)
            : new List<StockTransaction>(1);

        if (pendingSeededStockTransactions.Count > 0)
        {
            createdTransactions.AddRange(pendingSeededStockTransactions);
        }

        createdTransactions.Add(transaction);

        if (pendingCurrencyTransactions.Count > 0)
        {
            knownLedgerTransactionsInExecution.AddRange(pendingCurrencyTransactions);
        }

        var usesPartialHistoryAssumptionForWarning = usesPartialHistoryAssumption &&
            transactionType is TransactionType.Buy or TransactionType.Sell;

        var sellBeforeBuyDecisionContext = usesPartialHistoryAssumption
            ? BuildSellBeforeBuyDecisionContext(
                sellBeforeBuyAction,
                sellBeforeBuyDecision.DecisionScope,
                sellBeforeBuyDecision.Reason)
            : BuildSellBeforeBuyDecisionContext(
                SellBeforeBuyAction.None,
                null,
                SellBeforeBuyReasonNotApplicable);

        return new ImportedStockTransactionCreationResult(
            Transaction: transaction,
            CreatedTransactions: createdTransactions,
            UsesPartialHistoryAssumption: usesPartialHistoryAssumptionForWarning,
            SellBeforeBuyDecision: sellBeforeBuyDecisionContext);
    }

    private static (BalanceAction Action, CurrencyTransactionType? TopUpTransactionType) ResolveBalanceDecision(
        ExecuteStockImportRowRequest row,
        StockImportDefaultBalanceDecisionRequest? defaultDecision,
        Domain.Entities.CurrencyLedger boundLedger)
    {
        var action = row.BalanceAction
            ?? defaultDecision?.Action
            ?? BalanceAction.None;

        if (action == BalanceAction.None)
        {
            return (BalanceAction.None, null);
        }

        var topUpType = action == BalanceAction.TopUp
            ? row.TopUpTransactionType ?? defaultDecision?.TopUpTransactionType
            : null;

        if (action == BalanceAction.TopUp
            && !topUpType.HasValue
            && IsTwdLedger(boundLedger))
        {
            topUpType = CurrencyTransactionType.Deposit;
        }

        return (action, topUpType);
    }

    private static (SellBeforeBuyAction Action, string DecisionScope, string Reason) ResolveSellBeforeBuyDecision(
        ExecuteStockImportRowRequest row,
        StockImportBaselineExecutionDecisionRequest? baselineDecision)
    {
        if (row.SellBeforeBuyAction.HasValue)
        {
            return (
                row.SellBeforeBuyAction.Value,
                DecisionScopeRowOverride,
                SellBeforeBuyReasonRowOverride);
        }

        if (baselineDecision?.SellBeforeBuyAction.HasValue == true)
        {
            return (
                baselineDecision.SellBeforeBuyAction.Value,
                DecisionScopeGlobalDefault,
                SellBeforeBuyReasonGlobalDefault);
        }

        return (
            SellBeforeBuyAction.CreateAdjustment,
            DecisionScopeAutoDefault,
            SellBeforeBuyReasonAutoDefault);
    }

    private static StockImportSellBeforeBuyDecisionContextDto BuildSellBeforeBuyDecisionContext(
        SellBeforeBuyAction action,
        string? decisionScope,
        string? reason)
        => new()
        {
            Strategy = MapSellBeforeBuyStrategy(action),
            DecisionScope = decisionScope,
            Reason = reason
        };

    private static string MapSellBeforeBuyStrategy(SellBeforeBuyAction action)
        => action switch
        {
            SellBeforeBuyAction.UseOpeningPosition => SellBeforeBuyStrategyUseOpeningPosition,
            SellBeforeBuyAction.CreateAdjustment => SellBeforeBuyStrategyCreateAdjustment,
            _ => SellBeforeBuyStrategyNone
        };

    private async Task<StockTransaction?> BuildOpeningAdjustmentFromBaselineAsync(
        Domain.Entities.CurrencyLedger boundLedger,
        StockImportSessionSnapshotDto sessionSnapshot,
        DateTime sessionBaselineAnchorDate,
        string normalizedTicker,
        StockMarket market,
        Currency currency,
        SellBeforeBuyAction resolvedAction,
        decimal requiredShares,
        Guid portfolioId,
        Guid? currencyLedgerId,
        CancellationToken cancellationToken)
    {
        if (resolvedAction == SellBeforeBuyAction.None)
        {
            return null;
        }

        var openingPosition = ResolveOpeningPosition(sessionSnapshot, normalizedTicker);
        var baselineDate = sessionBaselineAnchorDate;

        if (resolvedAction == SellBeforeBuyAction.UseOpeningPosition)
        {
            if (openingPosition is null)
            {
                return null;
            }

            var openingShares = openingPosition.Quantity!.Value;
            if (openingShares <= 0m)
            {
                return null;
            }

            var openingExchangeRate = await ResolveOpeningExchangeRateAsync(
                boundLedger,
                baselineDate,
                cancellationToken);
            var pricePerShare = ResolveOpeningCostComponents(openingPosition, openingShares);

            var openingTransaction = new StockTransaction(
                portfolioId: portfolioId,
                transactionDate: baselineDate,
                ticker: normalizedTicker,
                transactionType: TransactionType.Adjustment,
                shares: openingShares,
                pricePerShare: pricePerShare,
                exchangeRate: openingExchangeRate,
                fees: 0m,
                currencyLedgerId: currencyLedgerId,
                notes: "import-execute-opening-baseline",
                market: market,
                currency: currency);

            openingTransaction.SetImportInitialization(
                marketValueAtImport: openingPosition.TotalCost,
                historicalTotalCost: openingPosition.HistoricalTotalCost);

            return openingTransaction;
        }

        if (resolvedAction == SellBeforeBuyAction.CreateAdjustment)
        {
            var resolvedShares = requiredShares;
            if (openingPosition?.Quantity is decimal openingShares && openingShares > 0m)
            {
                resolvedShares = openingShares;
            }

            var marketValueAtImport = await ResolveOpeningMarketValueAtImportAsync(
                normalizedTicker,
                market,
                baselineDate,
                resolvedShares,
                openingPosition,
                cancellationToken);

            if (marketValueAtImport <= 0m)
            {
                return null;
            }

            var openingExchangeRate = await ResolveOpeningExchangeRateAsync(
                boundLedger,
                baselineDate,
                cancellationToken);

            var pricePerShare = resolvedShares > 0m
                ? Math.Round(marketValueAtImport / resolvedShares, 4, MidpointRounding.AwayFromZero)
                : 0m;
            if (pricePerShare < 0m)
            {
                pricePerShare = 0m;
            }

            var openingTransaction = new StockTransaction(
                portfolioId: portfolioId,
                transactionDate: baselineDate,
                ticker: normalizedTicker,
                transactionType: TransactionType.Adjustment,
                shares: resolvedShares,
                pricePerShare: pricePerShare,
                exchangeRate: openingExchangeRate,
                fees: 0m,
                currencyLedgerId: currencyLedgerId,
                notes: "import-execute-adjustment",
                market: market,
                currency: currency);

            openingTransaction.SetImportInitialization(
                marketValueAtImport: marketValueAtImport,
                historicalTotalCost: openingPosition?.HistoricalTotalCost);

            return openingTransaction;
        }

        return null;
    }

    private static StockImportSessionOpeningPositionSnapshotDto? ResolveOpeningPosition(
        StockImportSessionSnapshotDto sessionSnapshot,
        string normalizedTicker)
    {
        var holdings = ResolveBaselineHoldings(sessionSnapshot.Baseline);

        var explicitOpeningPosition = holdings
            .FirstOrDefault(position =>
                !string.IsNullOrWhiteSpace(position.Ticker)
                && string.Equals(position.Ticker, normalizedTicker, StringComparison.OrdinalIgnoreCase)
                && position.Quantity is > 0m);

        return explicitOpeningPosition;
    }

    private static IReadOnlyList<StockImportSessionOpeningPositionSnapshotDto> ResolveBaselineHoldings(
        StockImportSessionBaselineSnapshotDto baseline)
    {
        if (baseline.CurrentHoldings.Count > 0)
        {
            return baseline.CurrentHoldings;
        }

        return baseline.OpeningPositions;
    }

    private async Task<decimal> ResolveOpeningExchangeRateAsync(
        Domain.Entities.CurrencyLedger boundLedger,
        DateTime baselineDate,
        CancellationToken cancellationToken)
    {
        if (string.Equals(boundLedger.CurrencyCode, boundLedger.HomeCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return 1.0m;
        }

        var fxResult = await txDateFxService.GetOrFetchAsync(
            boundLedger.CurrencyCode,
            boundLedger.HomeCurrency,
            baselineDate,
            cancellationToken);

        if (fxResult is null)
        {
            throw new BusinessRuleException("無法計算匯率，請先在帳本中建立換匯紀錄");
        }

        return fxResult.Rate;
    }

    private static decimal ResolveOpeningCostComponents(
        StockImportSessionOpeningPositionSnapshotDto openingPosition,
        decimal openingShares)
    {
        if (openingPosition.TotalCost is decimal totalCost && totalCost > 0m)
        {
            var unitCost = totalCost / openingShares;
            var roundedUnitCost = Math.Round(unitCost, 4, MidpointRounding.AwayFromZero);
            if (roundedUnitCost > 0m)
            {
                return roundedUnitCost;
            }
        }

        return 0m;
    }

    private async Task<decimal?> TryGetClosingPriceForBaselineAsync(
        string normalizedTicker,
        StockMarket market,
        DateTime baselineDate,
        CancellationToken cancellationToken)
    {
        var targetDate = DateOnly.FromDateTime(baselineDate.Date);

        foreach (var yahooSymbol in GetYahooSymbolsForHistoricalLookup(normalizedTicker, market))
        {
            try
            {
                var result = await yahooHistoricalPriceService.GetHistoricalPriceAsync(
                    yahooSymbol,
                    targetDate,
                    cancellationToken);

                if (result is { Price: > 0m })
                {
                    return result.Price;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to fetch baseline historical price. Ticker={Ticker}, Market={Market}, YahooSymbol={YahooSymbol}, BaselineDate={BaselineDate}",
                    normalizedTicker,
                    market,
                    yahooSymbol,
                    baselineDate);
            }
        }

        return null;
    }

    private async Task<decimal> ResolveOpeningMarketValueAtImportAsync(
        string normalizedTicker,
        StockMarket market,
        DateTime baselineDate,
        decimal shares,
        StockImportSessionOpeningPositionSnapshotDto? openingPosition,
        CancellationToken cancellationToken)
    {
        if (shares <= 0m)
        {
            return 0m;
        }

        var fetchedPrice = await TryGetClosingPriceForBaselineAsync(
            normalizedTicker,
            market,
            baselineDate,
            cancellationToken);

        if (fetchedPrice is > 0m)
        {
            return Math.Round(shares * fetchedPrice.Value, 2, MidpointRounding.AwayFromZero);
        }

        if (openingPosition?.TotalCost is decimal openingTotalCost && openingTotalCost > 0m)
        {
            return Math.Round(openingTotalCost, 2, MidpointRounding.AwayFromZero);
        }

        logger.LogInformation(
            "Historical price unavailable for opening adjustment. Ticker={Ticker}, Market={Market}, BaselineDate={BaselineDate}. Fallback to zero market value.",
            normalizedTicker,
            market,
            baselineDate);

        return 0m;
    }

    private static IEnumerable<string> GetYahooSymbolsForHistoricalLookup(string normalizedTicker, StockMarket market)
    {
        var isTaiwanStock = market == StockMarket.TW || IsTaiwanTicker(normalizedTicker);
        if (isTaiwanStock)
        {
            var baseTicker = normalizedTicker.Split('.')[0];
            var explicitSuffix = normalizedTicker.Contains('.')
                ? normalizedTicker[(normalizedTicker.LastIndexOf('.') + 1)..]
                : null;

            if (string.Equals(explicitSuffix, "TW", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{baseTicker}.TW";
                yield return $"{baseTicker}.TWO";
                yield break;
            }

            if (string.Equals(explicitSuffix, "TWO", StringComparison.OrdinalIgnoreCase))
            {
                yield return $"{baseTicker}.TWO";
                yield return $"{baseTicker}.TW";
                yield break;
            }

            yield return $"{baseTicker}.TW";
            yield return $"{baseTicker}.TWO";
            yield break;
        }

        yield return ConvertToYahooSymbol(normalizedTicker, market);
    }

    private static string ConvertToYahooSymbol(string ticker, StockMarket market)
    {
        if (ticker.Contains('.'))
        {
            return ticker;
        }

        return market switch
        {
            StockMarket.UK => $"{ticker}.L",
            StockMarket.EU => $"{ticker}.AS",
            _ => ticker
        };
    }

    private static bool IsTaiwanTicker(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return false;
        }

        var baseTicker = ticker.Trim().Split('.')[0];
        return baseTicker.Length > 0 && char.IsDigit(baseTicker[0]);
    }

    private static DateTime ResolveSessionBaselineAnchorDate(
        string selectedFormat,
        StockImportSessionSnapshotDto sessionSnapshot,
        IReadOnlyList<ExecuteStockImportRowRequest> requestRows,
        IReadOnlyDictionary<int, StockImportSessionRowSnapshotDto> sessionRowsByNumber)
    {
        var earliestTradeDate = requestRows
            .Where(row => !row.Exclude)
            .Select(row =>
            {
                sessionRowsByNumber.TryGetValue(row.RowNumber, out var sessionRow);
                return sessionRow?.TradeDate;
            })
            .Where(date => date.HasValue)
            .Select(date => date!.Value.Date)
            .DefaultIfEmpty(DateTime.UtcNow.Date)
            .Min();

        var tradeDate = DateTime.SpecifyKind(earliestTradeDate, DateTimeKind.Utc);

        if (string.Equals(selectedFormat, StockImportParser.FormatBrokerStatement, StringComparison.Ordinal))
        {
            return ResolveBrokerBaselineAnchorDate(tradeDate);
        }

        return ResolveBaselineDate(
            sessionSnapshot,
            tradeDate);
    }

    private static DateTime ResolveBrokerBaselineAnchorDate(DateTime tradeDate)
        => DateTime.SpecifyKind(tradeDate.Date.AddDays(-1), DateTimeKind.Utc);

    private static CurrencyTransaction? BuildOpeningLedgerBaselineTransaction(
        StockImportSessionSnapshotDto sessionSnapshot,
        Domain.Entities.CurrencyLedger boundLedger,
        IReadOnlyList<ExecuteStockImportRowRequest> requestRows,
        IReadOnlyDictionary<int, StockImportSessionRowSnapshotDto> sessionRowsByNumber,
        DateTime sessionBaselineAnchorDate)
    {
        if (sessionSnapshot.Baseline.OpeningLedgerBalance is not decimal openingLedgerBalance || openingLedgerBalance <= 0m)
        {
            return null;
        }

        var hasIncludedRow = requestRows.Any(row => !row.Exclude && sessionRowsByNumber.ContainsKey(row.RowNumber));
        if (!hasIncludedRow)
        {
            return null;
        }

        var baselineDate = sessionBaselineAnchorDate;

        decimal? homeAmount = null;
        decimal? exchangeRate = null;

        if (string.Equals(boundLedger.CurrencyCode, boundLedger.HomeCurrency, StringComparison.OrdinalIgnoreCase))
        {
            homeAmount = openingLedgerBalance;
            exchangeRate = 1.0m;
        }

        return new CurrencyTransaction(
            boundLedger.Id,
            baselineDate,
            CurrencyTransactionType.InitialBalance,
            openingLedgerBalance,
            homeAmount: homeAmount,
            exchangeRate: exchangeRate,
            relatedStockTransactionId: null,
            notes: "import-execute-opening-ledger-baseline");
    }

    private static DateTime ResolveBaselineDate(
        StockImportSessionSnapshotDto sessionSnapshot,
        DateTime tradeDate)
    {
        var explicitBaselineDate = sessionSnapshot.Baseline.AsOfDate ?? sessionSnapshot.Baseline.BaselineDate;

        var resolvedBaselineDate = explicitBaselineDate is DateTime baselineDate
            ? DateTime.SpecifyKind(baselineDate.Date, DateTimeKind.Utc)
            : DateTime.SpecifyKind(tradeDate.Date.AddDays(-1), DateTimeKind.Utc);

        // Year-boundary guard: if opening baseline lands on Jan 1 of the same import year,
        // historical performance treats it as an in-period event and can split the same economic value
        // into both valuation and external cash flow. Move it to the previous day (Dec 31).
        if (resolvedBaselineDate.Month == 1
            && resolvedBaselineDate.Day == 1
            && tradeDate.Year == resolvedBaselineDate.Year
            && tradeDate.Date >= resolvedBaselineDate.Date)
        {
            return DateTime.SpecifyKind(resolvedBaselineDate.AddDays(-1).Date, DateTimeKind.Utc);
        }

        return resolvedBaselineDate;
    }

    private static StockImportRowFailureException CreateSellBeforeBuyActionFailure(
        ExecuteStockImportRowRequest row,
        string normalizedTicker,
        StockMarket market,
        decimal requiredShares,
        decimal availableShares,
        string? decisionScope,
        string? reason,
        SellBeforeBuyAction resolvedAction)
    {
        var requiredText = requiredShares.ToString("0.####", CultureInfo.InvariantCulture);
        var availableText = availableShares.ToString("0.####", CultureInfo.InvariantCulture);

        var invalidValue = row.SellBeforeBuyAction?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(decisionScope))
        {
            invalidValue = string.IsNullOrWhiteSpace(invalidValue)
                ? $"scope={decisionScope};ticker={normalizedTicker};market={market}"
                : $"{invalidValue};scope={decisionScope};ticker={normalizedTicker};market={market}";
        }

        return new StockImportRowFailureException(
            errorCode: ErrorCodeSellBeforeBuyActionRequired,
            fieldName: FieldSellBeforeBuyAction,
            message: $"此列為 sell-before-buy / 零持股賣出情境（可用股數 {availableText}，欲賣出 {requiredText}），無法套用策略。",
            correctionGuidance: "請補齊 baseline currentHoldings/openingPositions 與成本資訊，或提供可用的歷史價格來源後重試。",
            invalidValue: invalidValue,
            balanceDecision: null,
            sellBeforeBuyDecision: BuildSellBeforeBuyDecisionContext(
                resolvedAction,
                decisionScope,
                reason));
    }

    private static bool IsTwdLedger(Domain.Entities.CurrencyLedger boundLedger)
        => string.Equals(boundLedger.CurrencyCode, TwdCurrencyCode, StringComparison.OrdinalIgnoreCase);

    private static decimal CalculateBalanceAsOfDate(
        CurrencyLedgerService currencyLedgerService,
        IEnumerable<CurrencyTransaction> ledgerTransactions,
        DateTime asOfDate)
    {
        var asOfDateOnly = asOfDate.Date;
        var transactionsUpToDate = ledgerTransactions
            .Where(transaction => transaction.TransactionDate.Date <= asOfDateOnly)
            .ToList();

        return currencyLedgerService.CalculateBalance(transactionsUpToDate);
    }

    private static void TrackEarliestAffectedMonth(ref DateOnly? earliestAffectedMonth, DateTime transactionDate)
    {
        var month = new DateOnly(transactionDate.Year, transactionDate.Month, 1);
        if (!earliestAffectedMonth.HasValue || month < earliestAffectedMonth.Value)
        {
            earliestAffectedMonth = month;
        }
    }

    private static StockImportBalanceDecisionContextDto? BuildBalanceDecisionContext(
        StockImportDefaultBalanceDecisionRequest? defaultDecision,
        ExecuteStockImportRowRequest row,
        decimal? requiredAmount = null,
        decimal? availableBalance = null,
        decimal? shortfall = null)
    {
        var action = row.BalanceAction ?? defaultDecision?.Action;
        var topUpType = action == BalanceAction.TopUp
            ? row.TopUpTransactionType ?? defaultDecision?.TopUpTransactionType
            : null;

        if (action is null && topUpType is null && !requiredAmount.HasValue && !availableBalance.HasValue && !shortfall.HasValue)
            return null;

        var resolvedRequiredAmount = requiredAmount ?? 0m;
        var resolvedAvailableBalance = availableBalance ?? 0m;
        var resolvedShortfall = shortfall ?? 0m;

        return new StockImportBalanceDecisionContextDto
        {
            RequiredAmount = resolvedRequiredAmount,
            AvailableBalance = resolvedAvailableBalance,
            Shortfall = resolvedShortfall,
            Action = action,
            TopUpTransactionType = topUpType,
            DecisionScope = ResolveDecisionScope(defaultDecision, row)
        };
    }

    private static string? ResolveDecisionScope(
        StockImportDefaultBalanceDecisionRequest? defaultDecision,
        ExecuteStockImportRowRequest row)
    {
        if (row.BalanceAction.HasValue || row.TopUpTransactionType.HasValue)
            return "row_override";

        if (defaultDecision?.Action.HasValue == true || defaultDecision?.TopUpTransactionType.HasValue == true)
            return "global_default";

        return null;
    }

    private static StockImportRowFailureException CreateBalanceActionFailure(
        string message,
        BalanceAction action,
        CurrencyTransactionType? topUpTransactionType,
        decimal requiredAmount,
        decimal availableBalance,
        decimal shortfall,
        string? decisionScope,
        string fieldName = FieldBalanceAction,
        string? invalidValue = null)
    {
        var resolvedInvalidValue = invalidValue ?? (action == BalanceAction.TopUp
            ? topUpTransactionType?.ToString() ?? action.ToString()
            : action.ToString());

        return new StockImportRowFailureException(
            errorCode: ErrorCodeBalanceActionRequired,
            fieldName: fieldName,
            message: message,
            correctionGuidance: "請先指定 Margin 或 TopUp，並在 TopUp 時提供合法的入帳類型。",
            invalidValue: resolvedInvalidValue,
            balanceDecision: new StockImportBalanceDecisionContextDto
            {
                RequiredAmount = requiredAmount,
                AvailableBalance = availableBalance,
                Shortfall = shortfall,
                Action = action,
                TopUpTransactionType = topUpTransactionType,
                DecisionScope = decisionScope
            });
    }

    private static (string FieldName, string? InvalidValue, string Message, string CorrectionGuidance) BuildBusinessRuleFailureDetail(
        string errorMessage,
        ExecuteStockImportRowRequest row,
        string? normalizedTradeSide)
    {
        if (errorMessage.Contains("持股不足", StringComparison.Ordinal))
        {
            return (
                FieldName: FieldPosition,
                InvalidValue: normalizedTradeSide,
                Message: errorMessage,
                CorrectionGuidance: "此列為賣出且可賣股數不足，請調整賣出股數、確認排序，或先匯入對應買入交易後重試。"
            );
        }

        if (errorMessage.Contains("尚未確認買賣方向", StringComparison.Ordinal))
        {
            return (
                FieldName: FieldConfirmedTradeSide,
                InvalidValue: row.ConfirmedTradeSide,
                Message: errorMessage,
                CorrectionGuidance: "請先確認此列為 buy 或 sell，或將此列排除後再執行。"
            );
        }

        if (errorMessage.Contains("匯率", StringComparison.Ordinal))
        {
            return (
                FieldName: FieldBalanceAction,
                InvalidValue: row.BalanceAction?.ToString(),
                Message: errorMessage,
                CorrectionGuidance: "查無可用匯率；請先補齊帳本換匯資料，或改用可執行的餘額處理方式後重試。"
            );
        }

        return (
            FieldName: FieldRow,
            InvalidValue: row.RowNumber.ToString(),
            Message: errorMessage,
            CorrectionGuidance: "請檢查該列交易資料與持倉/匯率條件後重試，必要時調整或排除此列。"
        );
    }

    private static string? ResolveInvalidValueForField(
        string fieldName,
        string? normalizedTradeSide,
        ExecuteStockImportRowRequest row)
    {
        return fieldName switch
        {
            FieldBalanceAction => row.BalanceAction?.ToString(),
            StockBalanceActionRules.FieldTopUpTransactionType => row.TopUpTransactionType?.ToString(),
            FieldSellBeforeBuyAction => row.SellBeforeBuyAction?.ToString(),
            FieldTicker => row.Ticker,
            FieldConfirmedTradeSide => row.ConfirmedTradeSide,
            FieldPosition => normalizedTradeSide,
            _ => normalizedTradeSide
        };
    }

    private static TransactionType ResolveTransactionType(string normalizedTradeSide)
        => normalizedTradeSide switch
        {
            TradeSideBuy => TransactionType.Buy,
            TradeSideSell => TransactionType.Sell,
            _ => throw new BusinessRuleException("此列尚未確認買賣方向。")
        };

    private static Currency? ParseCurrency(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            return null;

        if (Enum.TryParse<Currency>(currencyCode.Trim(), ignoreCase: true, out var parsed))
            return parsed;

        return null;
    }

    private static StockMarket? ResolveImportMarket(
        string normalizedTicker,
        Currency? sessionCurrency,
        IReadOnlyList<StockTransaction> transactionsForPosition)
    {
        var candidateMarkets = GetCandidateMarketsFromExistingTransactions(
            normalizedTicker,
            transactionsForPosition);

        if (candidateMarkets.Count == 1)
            return candidateMarkets[0];

        var inferredMarketFromCurrency = TryInferMarketFromCurrency(sessionCurrency);
        if (inferredMarketFromCurrency is StockMarket marketFromCurrency)
            return marketFromCurrency;

        if (candidateMarkets.Count > 1)
            return null;

        return StockTransaction.GuessMarketFromTicker(normalizedTicker);
    }

    private static List<StockMarket> GetCandidateMarketsFromExistingTransactions(
        string normalizedTicker,
        IReadOnlyList<StockTransaction> transactionsForPosition)
        => transactionsForPosition
            .Where(transaction => string.Equals(transaction.Ticker, normalizedTicker, StringComparison.OrdinalIgnoreCase))
            .Select(transaction => transaction.Market)
            .Distinct()
            .ToList();

    private static StockMarket? TryInferMarketFromCurrency(Currency? currency)
        => currency switch
        {
            Currency.TWD => StockMarket.TW,
            Currency.GBP => StockMarket.UK,
            Currency.EUR => StockMarket.EU,
            _ => null
        };

    private static void AddFailure(
        ExecuteStockImportRowRequest row,
        List<StockImportExecuteRowResultDto> results,
        List<StockImportDiagnosticDto> diagnostics,
        string errorCode,
        string fieldName,
        string? invalidValue,
        string message,
        string correctionGuidance,
        string? confirmedTradeSide,
        ILogger<ExecuteStockImportUseCase> logger,
        StockImportBalanceDecisionContextDto? balanceDecision = null,
        StockImportSellBeforeBuyDecisionContextDto? sellBeforeBuyDecision = null)
    {
        var normalizedInvalidValue = string.IsNullOrWhiteSpace(invalidValue) ? null : invalidValue;

        results.Add(new StockImportExecuteRowResultDto
        {
            RowNumber = row.RowNumber,
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            ConfirmedTradeSide = confirmedTradeSide,
            BalanceDecision = balanceDecision,
            SellBeforeBuyDecision = sellBeforeBuyDecision ?? BuildSellBeforeBuyDecisionContext(
                SellBeforeBuyAction.None,
                null,
                SellBeforeBuyReasonNotApplicable)
        });

        diagnostics.Add(new StockImportDiagnosticDto
        {
            RowNumber = row.RowNumber,
            FieldName = fieldName,
            InvalidValue = normalizedInvalidValue,
            ErrorCode = errorCode,
            Message = message,
            CorrectionGuidance = correctionGuidance
        });

        logger.LogWarning(
            "Stock import execute row failed. RowNumber={RowNumber}, ErrorCode={ErrorCode}, FieldName={FieldName}, HasInvalidValue={HasInvalidValue}, HasConfirmedTradeSide={HasConfirmedTradeSide}",
            row.RowNumber,
            errorCode,
            fieldName,
            normalizedInvalidValue is not null,
            confirmedTradeSide is not null);
    }

    private static string? NormalizeTicker(string? ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return null;

        return ticker.Trim().ToUpperInvariant();
    }

    private static string? NormalizeConfirmedTradeSide(string? confirmedTradeSide)
    {
        if (string.IsNullOrWhiteSpace(confirmedTradeSide))
            return null;

        if (string.Equals(confirmedTradeSide, "buy", StringComparison.OrdinalIgnoreCase))
            return "buy";

        if (string.Equals(confirmedTradeSide, "sell", StringComparison.OrdinalIgnoreCase))
            return "sell";

        return null;
    }
}
