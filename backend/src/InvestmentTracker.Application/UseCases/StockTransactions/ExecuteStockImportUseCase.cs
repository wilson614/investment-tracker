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
    StockImportBalanceDecisionContextDto? balanceDecision = null)
    : BusinessRuleException(message)
{
    public string ErrorCode { get; } = errorCode;
    public string FieldName { get; } = fieldName;
    public string CorrectionGuidance { get; } = correctionGuidance;
    public string? InvalidValue { get; } = invalidValue;
    public StockImportBalanceDecisionContextDto? BalanceDecision { get; } = balanceDecision;
}

public interface IExecuteStockImportUseCase
{
    Task<StockImportExecuteResponseDto> ExecuteAsync(
        ExecuteStockImportRequest request,
        CancellationToken cancellationToken = default);
}

internal readonly record struct ImportedStockTransactionCreationResult(
    StockTransaction Transaction,
    IReadOnlyList<StockTransaction> SeededTransactions,
    IReadOnlyList<CurrencyTransaction> CreatedCurrencyTransactions,
    bool UsesPartialHistoryAssumption);

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
    ILogger<ExecuteStockImportUseCase> logger) : IExecuteStockImportUseCase
{
    private const string StatusCommitted = "committed";
    private const string StatusPartiallyCommitted = "partially_committed";
    private const string StatusRejected = "rejected";

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
    private const string WarningMessagePartialPeriodAssumption = "偵測到匯入可能是部分期間；已依使用者指定方式套用 sell-before-buy 處理。";
    private const string WarningCorrectionGuidancePartialPeriodAssumption = "若要提升帳本成本與績效精準度，建議補匯入更早交易紀錄或提供完整期初持倉基準。";

    private const string TradeSideBuy = "buy";
    private const string TradeSideSell = "sell";
    private const string TwdCurrencyCode = "TWD";

    public async Task<StockImportExecuteResponseDto> ExecuteAsync(
        ExecuteStockImportRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.SessionId == Guid.Empty)
            throw new BusinessRuleException("SessionId is required.");

        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(request.PortfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", request.PortfolioId);

        if (portfolio.UserId != userId)
            throw new AccessDeniedException();

        var session = await stockImportSessionStore.TryConsumeForOwnerAsync(
            request.SessionId,
            userId,
            request.PortfolioId,
            cancellationToken);

        if (session is null)
        {
            var existingSession = await stockImportSessionStore.GetAsync(request.SessionId, cancellationToken);
            if (existingSession is not null
                && (existingSession.UserId != userId || existingSession.PortfolioId != request.PortfolioId))
            {
                throw new AccessDeniedException("Import session does not match current user or portfolio.");
            }

            throw new BusinessRuleException("Import session not found, expired, or already consumed.");
        }

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

        var boundLedger = await currencyLedgerRepository.GetByIdAsync(portfolio.BoundCurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", portfolio.BoundCurrencyLedgerId);

        await using var tx = await transactionManager.BeginTransactionAsync(cancellationToken);

        try
        {
            var importedTransactionsInBatch = new List<StockTransaction>();
            var importedCurrencyTransactionsInBatch = new List<CurrencyTransaction>();

            var persistedTransactions = (await stockTransactionRepository.GetByPortfolioIdAsync(
                portfolio.Id,
                cancellationToken)).ToList();

            var persistedLedgerTransactions = (await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                boundLedger.Id,
                cancellationToken)).ToList();

            var openingLedgerTransaction = BuildOpeningLedgerBaselineTransaction(
                session,
                boundLedger,
                request.Rows,
                sessionRowsByNumber);

            if (openingLedgerTransaction is not null)
            {
                await currencyTransactionRepository.AddAsync(openingLedgerTransaction, cancellationToken);
                importedCurrencyTransactionsInBatch.Add(openingLedgerTransaction);

                await txSnapshotService.UpsertSnapshotAsync(
                    portfolio.Id,
                    openingLedgerTransaction.Id,
                    openingLedgerTransaction.TransactionDate,
                    cancellationToken);

                var openingMonth = new DateOnly(
                    openingLedgerTransaction.TransactionDate.Year,
                    openingLedgerTransaction.TransactionDate.Month,
                    1);

                await monthlySnapshotService.InvalidateFromMonthAsync(
                    portfolio.Id,
                    openingMonth,
                    cancellationToken);
            }

            foreach (var row in request.Rows.OrderBy(r => r.RowNumber))
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
                        logger: logger);
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
                        ConfirmedTradeSide = normalizedTradeSide
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
                        logger: logger);
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
                        logger: logger);
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
                        logger: logger);
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
                        logger: logger);
                    failedRows++;
                    continue;
                }

                try
                {
                    var creationResult = await CreateImportedTransactionAsync(
                        portfolio,
                        boundLedger,
                        session,
                        sessionRow,
                        normalizedTicker,
                        normalizedTradeSide,
                        row,
                        request.BaselineDecision,
                        request.DefaultBalanceAction,
                        persistedTransactions,
                        persistedLedgerTransactions,
                        importedTransactionsInBatch,
                        importedCurrencyTransactionsInBatch,
                        cancellationToken);

                    var createdTransaction = creationResult.Transaction;

                    insertedRows++;
                    importedTransactionsInBatch.AddRange(creationResult.SeededTransactions);
                    importedTransactionsInBatch.Add(createdTransaction);
                    importedCurrencyTransactionsInBatch.AddRange(creationResult.CreatedCurrencyTransactions);

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
                        ConfirmedTradeSide = normalizedTradeSide
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
                        logger: logger);
                    failedRows++;
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
                        logger: logger);
                    failedRows++;
                }
            }

            if (insertedRows > 0)
            {
                await tx.CommitAsync(cancellationToken);
            }
            else
            {
                await tx.RollbackAsync(cancellationToken);
            }

            var status = ResolveStatus(insertedRows, failedRows);

            logger.LogInformation(
                "Stock import execute completed. PortfolioId={PortfolioId}, SessionId={SessionId}, Status={Status}, RequestedRows={RequestedRows}, InsertedRows={InsertedRows}, FailedRows={FailedRows}, ErrorCount={ErrorCount}",
                request.PortfolioId,
                request.SessionId,
                status,
                request.Rows.Count,
                insertedRows,
                failedRows,
                diagnostics.Count);

            return new StockImportExecuteResponseDto
            {
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
            throw;
        }
    }

    private async Task<ImportedStockTransactionCreationResult> CreateImportedTransactionAsync(
        Domain.Entities.Portfolio portfolio,
        Domain.Entities.CurrencyLedger boundLedger,
        StockImportSessionSnapshotDto sessionSnapshot,
        StockImportSessionRowSnapshotDto sessionRow,
        string normalizedTicker,
        string normalizedTradeSide,
        ExecuteStockImportRowRequest requestRow,
        StockImportBaselineExecutionDecisionRequest? baselineDecision,
        StockImportDefaultBalanceDecisionRequest? defaultBalanceDecision,
        IReadOnlyList<StockTransaction> persistedTransactions,
        IReadOnlyList<CurrencyTransaction> persistedLedgerTransactions,
        IReadOnlyList<StockTransaction> importedTransactionsInBatch,
        IReadOnlyList<CurrencyTransaction> importedCurrencyTransactionsInBatch,
        CancellationToken cancellationToken)
    {
        var transactionType = ResolveTransactionType(normalizedTradeSide);
        var balanceDecision = ResolveBalanceDecision(requestRow, defaultBalanceDecision, boundLedger);
        var sellBeforeBuyAction = ResolveSellBeforeBuyAction(requestRow, baselineDecision);

        var transactionsForPosition = persistedTransactions
            .Concat(importedTransactionsInBatch)
            .GroupBy(transaction => transaction.Id)
            .Select(group => group.First())
            .OrderByDescending(transaction => transaction.TransactionDate)
            .ThenByDescending(transaction => transaction.CreatedAt)
            .ToList();

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
        IReadOnlyList<CurrencyTransaction>? ledgerTransactions = null;

        if (currency == Currency.TWD)
        {
            exchangeRate = 1.0m;
        }
        else
        {
            ledgerTransactions = MergeCurrencyTransactions(
                persistedLedgerTransactions,
                importedCurrencyTransactionsInBatch);

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
                    portfolio.BaseCurrency,
                    portfolio.HomeCurrency,
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
                    EnsureSellBeforeBuyActionResolved(
                        sellBeforeBuyAction,
                        baselineDecision,
                        requestRow,
                        normalizedTicker,
                        market,
                        shares,
                        currentPosition.TotalShares);

                    var seededOpeningPosition = BuildOpeningAdjustmentFromBaseline(
                        sessionSnapshot,
                        normalizedTicker,
                        market,
                        tradeDate,
                        currency,
                        sellBeforeBuyAction,
                        requiredShares: shares,
                        portfolio.Id,
                        boundLedger.Id);

                    if (seededOpeningPosition is not null)
                    {
                        transactionsForPosition =
                        [
                            seededOpeningPosition,
                            .. transactionsForPosition
                        ];

                        currentPosition = portfolioCalculator.CalculatePositionByMarket(
                            normalizedTicker,
                            market,
                            transactionsForPosition);

                        if (currentPosition.TotalShares < shares)
                        {
                            throw new BusinessRuleException($"持股不足。可賣出: {currentPosition.TotalShares:F4}，欲賣出: {shares:F4}");
                        }

                        await stockTransactionRepository.AddAsync(seededOpeningPosition, cancellationToken);

                        var seededAffectedFromMonth = new DateOnly(
                            seededOpeningPosition.TransactionDate.Year,
                            seededOpeningPosition.TransactionDate.Month,
                            1);
                        await monthlySnapshotService.InvalidateFromMonthAsync(
                            portfolio.Id,
                            seededAffectedFromMonth,
                            cancellationToken);

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
                            ResolveSellBeforeBuyDecisionScope(baselineDecision, requestRow));
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

        var pendingCurrencyTransactions = new List<CurrencyTransaction>();
        var pendingTopUpTransactions = new List<CurrencyTransaction>();

        if (linkedSpec != null)
        {
            if (transactionType == TransactionType.Buy)
            {
                if (ledgerTransactions is null)
                {
                    ledgerTransactions = MergeCurrencyTransactions(
                        persistedLedgerTransactions,
                        importedCurrencyTransactionsInBatch);
                }

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
                                var fxResult = await txDateFxService.GetOrFetchAsync(
                                    portfolio.BaseCurrency,
                                    portfolio.HomeCurrency,
                                    tradeDate,
                                    cancellationToken);
                                marketRate = fxResult?.Rate;
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

            if (pendingTopUpTransactions.Contains(pendingCurrencyTransaction))
            {
                await txSnapshotService.UpsertSnapshotAsync(
                    portfolio.Id,
                    pendingCurrencyTransaction.Id,
                    pendingCurrencyTransaction.TransactionDate,
                    cancellationToken);
            }
        }

        var affectedFromMonth = new DateOnly(tradeDate.Year, tradeDate.Month, 1);
        await monthlySnapshotService.InvalidateFromMonthAsync(
            portfolio.Id,
            affectedFromMonth,
            cancellationToken);

        await txSnapshotService.UpsertSnapshotAsync(
            portfolio.Id,
            transaction.Id,
            transaction.TransactionDate,
            cancellationToken);

        var usesPartialHistoryAssumptionForWarning = usesPartialHistoryAssumption &&
            transactionType is TransactionType.Buy or TransactionType.Sell;

        return new ImportedStockTransactionCreationResult(
            Transaction: transaction,
            SeededTransactions: pendingSeededStockTransactions,
            CreatedCurrencyTransactions: pendingCurrencyTransactions,
            UsesPartialHistoryAssumption: usesPartialHistoryAssumptionForWarning);
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

    private static SellBeforeBuyAction ResolveSellBeforeBuyAction(
        ExecuteStockImportRowRequest row,
        StockImportBaselineExecutionDecisionRequest? baselineDecision)
        => row.SellBeforeBuyAction
            ?? baselineDecision?.SellBeforeBuyAction
            ?? SellBeforeBuyAction.None;

    private static string? ResolveSellBeforeBuyDecisionScope(
        StockImportBaselineExecutionDecisionRequest? baselineDecision,
        ExecuteStockImportRowRequest row)
    {
        if (row.SellBeforeBuyAction.HasValue)
        {
            return "row_override";
        }

        if (baselineDecision?.SellBeforeBuyAction.HasValue == true)
        {
            return "global_default";
        }

        return null;
    }

    private static void EnsureSellBeforeBuyActionResolved(
        SellBeforeBuyAction resolvedAction,
        StockImportBaselineExecutionDecisionRequest? baselineDecision,
        ExecuteStockImportRowRequest row,
        string normalizedTicker,
        StockMarket market,
        decimal requiredShares,
        decimal availableShares)
    {
        if (resolvedAction is SellBeforeBuyAction.UseOpeningPosition or SellBeforeBuyAction.CreateAdjustment)
        {
            return;
        }

        var decisionScope = ResolveSellBeforeBuyDecisionScope(baselineDecision, row);

        throw CreateSellBeforeBuyActionFailure(
            row,
            normalizedTicker,
            market,
            requiredShares,
            availableShares,
            decisionScope);
    }

    private static StockTransaction? BuildOpeningAdjustmentFromBaseline(
        StockImportSessionSnapshotDto sessionSnapshot,
        string normalizedTicker,
        StockMarket market,
        DateTime tradeDate,
        Currency currency,
        SellBeforeBuyAction resolvedAction,
        decimal requiredShares,
        Guid portfolioId,
        Guid? currencyLedgerId)
    {
        if (resolvedAction == SellBeforeBuyAction.None)
        {
            return null;
        }

        var openingPosition = ResolveOpeningPosition(sessionSnapshot, normalizedTicker);

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

            var baselineDate = ResolveBaselineDate(sessionSnapshot, tradeDate);
            var (pricePerShare, exchangeRate) = ResolveOpeningCostComponents(openingPosition, openingShares, currency);

            return new StockTransaction(
                portfolioId: portfolioId,
                transactionDate: baselineDate,
                ticker: normalizedTicker,
                transactionType: TransactionType.Adjustment,
                shares: openingShares,
                pricePerShare: pricePerShare,
                exchangeRate: exchangeRate,
                fees: 0m,
                currencyLedgerId: currencyLedgerId,
                notes: "import-execute-opening-baseline",
                market: market,
                currency: currency);
        }

        if (resolvedAction == SellBeforeBuyAction.CreateAdjustment)
        {
            var baselineDate = ResolveBaselineDate(sessionSnapshot, tradeDate);

            if (openingPosition is null)
            {
                return new StockTransaction(
                    portfolioId: portfolioId,
                    transactionDate: baselineDate,
                    ticker: normalizedTicker,
                    transactionType: TransactionType.Adjustment,
                    shares: requiredShares,
                    pricePerShare: 0m,
                    exchangeRate: 1m,
                    fees: 0m,
                    currencyLedgerId: currencyLedgerId,
                    notes: "import-execute-adjustment",
                    market: market,
                    currency: currency);
            }

            var openingShares = openingPosition.Quantity!.Value;
            if (openingShares <= 0m)
            {
                return new StockTransaction(
                    portfolioId: portfolioId,
                    transactionDate: baselineDate,
                    ticker: normalizedTicker,
                    transactionType: TransactionType.Adjustment,
                    shares: requiredShares,
                    pricePerShare: 0m,
                    exchangeRate: 1m,
                    fees: 0m,
                    currencyLedgerId: currencyLedgerId,
                    notes: "import-execute-adjustment",
                    market: market,
                    currency: currency);
            }

            var (pricePerShare, exchangeRate) = ResolveOpeningCostComponents(openingPosition, openingShares, currency);

            return new StockTransaction(
                portfolioId: portfolioId,
                transactionDate: baselineDate,
                ticker: normalizedTicker,
                transactionType: TransactionType.Adjustment,
                shares: openingShares,
                pricePerShare: pricePerShare,
                exchangeRate: exchangeRate,
                fees: 0m,
                currencyLedgerId: currencyLedgerId,
                notes: "import-execute-adjustment",
                market: market,
                currency: currency);
        }

        return null;
    }

    private static StockImportSessionOpeningPositionSnapshotDto? ResolveOpeningPosition(
        StockImportSessionSnapshotDto sessionSnapshot,
        string normalizedTicker)
    {
        var explicitOpeningPosition = sessionSnapshot.Baseline.OpeningPositions
            .FirstOrDefault(position =>
                !string.IsNullOrWhiteSpace(position.Ticker)
                && string.Equals(position.Ticker, normalizedTicker, StringComparison.OrdinalIgnoreCase)
                && position.Quantity is > 0m);

        return explicitOpeningPosition;
    }

    private static (decimal PricePerShare, decimal ExchangeRate) ResolveOpeningCostComponents(
        StockImportSessionOpeningPositionSnapshotDto openingPosition,
        decimal openingShares,
        Currency _)
    {
        const decimal exchangeRate = 1m;

        if (openingPosition.TotalCost is decimal totalCost && totalCost > 0m)
        {
            var unitCost = totalCost / openingShares;
            var roundedUnitCost = Math.Round(unitCost, 4, MidpointRounding.AwayFromZero);
            if (roundedUnitCost > 0m)
            {
                return (roundedUnitCost, exchangeRate);
            }
        }

        return (0m, exchangeRate);
    }

    private static CurrencyTransaction? BuildOpeningLedgerBaselineTransaction(
        StockImportSessionSnapshotDto sessionSnapshot,
        Domain.Entities.CurrencyLedger boundLedger,
        IReadOnlyList<ExecuteStockImportRowRequest> requestRows,
        IReadOnlyDictionary<int, StockImportSessionRowSnapshotDto> sessionRowsByNumber)
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

        var baselineDate = ResolveBaselineDate(
            sessionSnapshot,
            DateTime.SpecifyKind(earliestTradeDate, DateTimeKind.Utc));

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
        if (sessionSnapshot.Baseline.BaselineDate is DateTime baselineDate)
        {
            return DateTime.SpecifyKind(baselineDate.Date, DateTimeKind.Utc);
        }

        return DateTime.SpecifyKind(tradeDate.Date.AddDays(-1), DateTimeKind.Utc);
    }

    private static StockImportRowFailureException CreateSellBeforeBuyActionFailure(
        ExecuteStockImportRowRequest row,
        string normalizedTicker,
        StockMarket market,
        decimal requiredShares,
        decimal availableShares,
        string? decisionScope)
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
            message: $"此列為 sell-before-buy / 零持股賣出情境（可用股數 {availableText}，欲賣出 {requiredText}），需先指定處理方式。",
            correctionGuidance: "請在 execute 請求提供 baselineDecision.sellBeforeBuyAction 或 rows[].sellBeforeBuyAction（UseOpeningPosition / CreateAdjustment）。",
            invalidValue: invalidValue,
            balanceDecision: null);
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

    private static List<CurrencyTransaction> MergeCurrencyTransactions(
        IEnumerable<CurrencyTransaction> persisted,
        IEnumerable<CurrencyTransaction> importedInBatch)
    {
        return persisted
            .Concat(importedInBatch)
            .GroupBy(transaction => transaction.Id)
            .Select(group => group.First())
            .OrderBy(transaction => transaction.TransactionDate)
            .ThenBy(transaction => transaction.CreatedAt)
            .ToList();
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

    private static string ResolveStatus(int insertedRows, int failedRows)
    {
        if (failedRows == 0)
            return StatusCommitted;

        return insertedRows > 0
            ? StatusPartiallyCommitted
            : StatusRejected;
    }

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
        StockImportBalanceDecisionContextDto? balanceDecision = null)
    {
        var normalizedInvalidValue = string.IsNullOrWhiteSpace(invalidValue) ? null : invalidValue;

        results.Add(new StockImportExecuteRowResultDto
        {
            RowNumber = row.RowNumber,
            Success = false,
            ErrorCode = errorCode,
            Message = message,
            ConfirmedTradeSide = confirmedTradeSide,
            BalanceDecision = balanceDecision
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
