using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

internal sealed class StockImportRowFailureException(
    string errorCode,
    string fieldName,
    string message,
    string correctionGuidance,
    string? invalidValue = null)
    : BusinessRuleException(message)
{
    public string ErrorCode { get; } = errorCode;
    public string FieldName { get; } = fieldName;
    public string CorrectionGuidance { get; } = correctionGuidance;
    public string? InvalidValue { get; } = invalidValue;
}

public interface IExecuteStockImportUseCase
{
    Task<StockImportExecuteResponseDto> ExecuteAsync(
        ExecuteStockImportRequest request,
        CancellationToken cancellationToken = default);
}

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
    IAppDbTransactionManager transactionManager) : IExecuteStockImportUseCase
{
    private const string StatusCommitted = "committed";
    private const string StatusPartiallyCommitted = "partially_committed";
    private const string StatusRejected = "rejected";

    private const string ErrorCodeSymbolUnresolved = "SYMBOL_UNRESOLVED";
    private const string ErrorCodeTradeSideConfirmationRequired = "TRADE_SIDE_CONFIRMATION_REQUIRED";
    private const string ErrorCodeBalanceActionRequired = "BALANCE_ACTION_REQUIRED";
    private const string ErrorCodeBusinessRuleViolation = "BUSINESS_RULE_VIOLATION";

    private const string FieldTicker = "ticker";
    private const string FieldConfirmedTradeSide = "confirmedTradeSide";
    private const string FieldRow = "row";

    private const string MessageExcluded = "此列已由使用者排除。";
    private const string MessageCreated = "Created";

    private const string TradeSideBuy = "buy";
    private const string TradeSideSell = "sell";

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

        var session = await stockImportSessionStore.GetAsync(request.SessionId, cancellationToken)
            ?? throw new BusinessRuleException("Import session not found or expired.");

        if (session.UserId != userId || session.PortfolioId != request.PortfolioId)
            throw new AccessDeniedException("Import session does not match current user or portfolio.");

        var sessionRowsByNumber = session.Rows.ToDictionary(r => r.RowNumber);

        var results = new List<StockImportExecuteRowResultDto>(request.Rows.Count);
        var diagnostics = new List<StockImportDiagnosticDto>();

        var insertedRows = 0;
        var failedRows = 0;

        var boundLedger = await currencyLedgerRepository.GetByIdAsync(portfolio.BoundCurrencyLedgerId, cancellationToken)
            ?? throw new EntityNotFoundException("CurrencyLedger", portfolio.BoundCurrencyLedgerId);

        await using var tx = await transactionManager.BeginTransactionAsync(cancellationToken);

        foreach (var row in request.Rows.OrderBy(r => r.RowNumber))
        {
            if (!sessionRowsByNumber.TryGetValue(row.RowNumber, out var sessionRow))
            {
                AddFailure(
                    row,
                    results,
                    diagnostics,
                    errorCode: ErrorCodeSymbolUnresolved,
                    fieldName: FieldTicker,
                    invalidValue: row.RowNumber.ToString(),
                    message: "此列不屬於目前預覽 Session。",
                    correctionGuidance: "請重新預覽後再執行。",
                    confirmedTradeSide: NormalizeConfirmedTradeSide(row.ConfirmedTradeSide));
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
                    confirmedTradeSide: normalizedTradeSide);
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
                    confirmedTradeSide: null);
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
                    confirmedTradeSide: normalizedTradeSide);
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
                    confirmedTradeSide: normalizedTradeSide);
                failedRows++;
                continue;
            }

            try
            {
                var createdTransaction = await CreateImportedTransactionAsync(
                    portfolio,
                    boundLedger,
                    sessionRow,
                    normalizedTicker,
                    normalizedTradeSide,
                    row,
                    request.DefaultBalanceAction,
                    cancellationToken);

                insertedRows++;

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
                    balanceDecision: BuildBalanceDecisionContext(request.DefaultBalanceAction, row));
                failedRows++;
            }
            catch (BusinessRuleException ex)
            {
                AddFailure(
                    row,
                    results,
                    diagnostics,
                    errorCode: ErrorCodeBusinessRuleViolation,
                    fieldName: FieldRow,
                    invalidValue: row.RowNumber.ToString(),
                    message: ex.Message,
                    correctionGuidance: "請檢查該列交易資料與持倉/匯率條件後重試，必要時調整或排除此列。",
                    confirmedTradeSide: normalizedTradeSide,
                    balanceDecision: BuildBalanceDecisionContext(request.DefaultBalanceAction, row));
                failedRows++;
            }
        }

        if (insertedRows > 0)
        {
            await tx.CommitAsync(cancellationToken);
            await stockImportSessionStore.RemoveAsync(request.SessionId, cancellationToken);
        }

        var status = ResolveStatus(insertedRows, failedRows);

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

    private async Task<StockTransaction> CreateImportedTransactionAsync(
        Domain.Entities.Portfolio portfolio,
        Domain.Entities.CurrencyLedger boundLedger,
        StockImportSessionRowSnapshotDto sessionRow,
        string normalizedTicker,
        string normalizedTradeSide,
        ExecuteStockImportRowRequest requestRow,
        StockImportDefaultBalanceDecisionRequest? defaultBalanceDecision,
        CancellationToken cancellationToken)
    {
        var transactionType = ResolveTransactionType(normalizedTradeSide);
        var balanceDecision = ResolveBalanceDecision(requestRow, defaultBalanceDecision);

        var market = StockTransaction.GuessMarketFromTicker(normalizedTicker);
        var currency = ParseCurrency(sessionRow.Currency) ?? StockTransaction.GuessCurrencyFromMarket(market);

        StockTransactionLinking.ValidateCurrencyMatchesBoundLedger(currency, boundLedger);

        var tradeDate = DateTime.SpecifyKind(sessionRow.TradeDate!.Value.Date, DateTimeKind.Utc);
        var shares = sessionRow.Quantity!.Value;
        var pricePerShare = sessionRow.UnitPrice!.Value;
        var fees = transactionType == TransactionType.Buy
            ? sessionRow.Fees + sessionRow.Taxes
            : sessionRow.Fees;

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
            ledgerTransactions = await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                boundLedger.Id,
                cancellationToken);

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

                if (fxResult == null)
                    throw new BusinessRuleException("無法計算匯率，請先在帳本中建立換匯紀錄");

                marketRate = fxResult.Rate;
                exchangeRate = marketRate;
            }
        }

        decimal? realizedPnlHome = null;
        if (transactionType == TransactionType.Sell)
        {
            var existingTransactions = await stockTransactionRepository.GetByPortfolioIdAsync(
                portfolio.Id,
                cancellationToken);

            var currentPosition = portfolioCalculator.CalculatePosition(normalizedTicker, existingTransactions);
            if (currentPosition.TotalShares < shares)
                throw new BusinessRuleException($"持股不足。可賣出: {currentPosition.TotalShares:F4}，欲賣出: {shares:F4}");

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

        if (linkedSpec != null)
        {
            if (transactionType == TransactionType.Buy)
            {
                ledgerTransactions ??= await currencyTransactionRepository.GetByLedgerIdOrderedAsync(
                    boundLedger.Id,
                    cancellationToken);

                var currentBalance = currencyLedgerService.CalculateBalance(ledgerTransactions);
                var shortfall = linkedSpec.Amount - currentBalance;

                if (shortfall > 0)
                {
                    switch (balanceDecision.Action)
                    {
                        case BalanceAction.None:
                            throw CreateBalanceActionFailure(
                                "帳本餘額不足，請選擇處理方式",
                                balanceDecision.Action,
                                balanceDecision.TopUpTransactionType);

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
                            if (marginMarketRate == null)
                            {
                                throw CreateBalanceActionFailure(
                                    "無法計算匯率，請先在帳本中建立換匯紀錄",
                                    balanceDecision.Action,
                                    balanceDecision.TopUpTransactionType);
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
                            if (!balanceDecision.TopUpTransactionType.HasValue)
                            {
                                throw CreateBalanceActionFailure(
                                    "補足餘額需指定交易類型",
                                    balanceDecision.Action,
                                    balanceDecision.TopUpTransactionType);
                            }

                            if (!IsTopUpIncomeType(balanceDecision.TopUpTransactionType.Value))
                            {
                                throw CreateBalanceActionFailure(
                                    "補足餘額的交易類型必須為入帳類型",
                                    balanceDecision.Action,
                                    balanceDecision.TopUpTransactionType);
                            }

                            decimal? topUpExchangeRate = null;
                            decimal? topUpHomeAmount = null;

                            if (boundLedger.CurrencyCode == boundLedger.HomeCurrency)
                            {
                                topUpExchangeRate = 1.0m;
                                topUpHomeAmount = shortfall;
                            }

                            if (balanceDecision.TopUpTransactionType.Value == CurrencyTransactionType.ExchangeBuy && topUpExchangeRate == null)
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

                                if (marketRate == null)
                                {
                                    throw CreateBalanceActionFailure(
                                        "無法取得市場匯率，請選擇其他交易類型或手動在帳本新增換匯紀錄",
                                        balanceDecision.Action,
                                        balanceDecision.TopUpTransactionType);
                                }

                                topUpExchangeRate = marketRate;
                                topUpHomeAmount = Math.Round(shortfall * marketRate.Value, 2);
                            }

                            var topUpTransaction = new CurrencyTransaction(
                                boundLedger.Id,
                                tradeDate,
                                balanceDecision.TopUpTransactionType.Value,
                                shortfall,
                                homeAmount: topUpHomeAmount,
                                exchangeRate: topUpExchangeRate,
                                relatedStockTransactionId: transaction.Id,
                                notes: $"補足買入 {normalizedTicker} 差額");

                            pendingCurrencyTransactions.Add(topUpTransaction);

                            var effectiveLedgerTransactions = ledgerTransactions
                                .Concat(pendingCurrencyTransactions)
                                .ToList();

                            var newLifoRate = currencyLedgerService.CalculateExchangeRateForPurchase(
                                effectiveLedgerTransactions,
                                tradeDate,
                                grossAmount);

                            if (newLifoRate > 0)
                                transaction.SetExchangeRate(newLifoRate);

                            break;
                        }

                        default:
                            throw CreateBalanceActionFailure(
                                "帳本餘額不足，請選擇處理方式",
                                balanceDecision.Action,
                                balanceDecision.TopUpTransactionType);
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

        return transaction;
    }

    private static (BalanceAction Action, CurrencyTransactionType? TopUpTransactionType) ResolveBalanceDecision(
        ExecuteStockImportRowRequest row,
        StockImportDefaultBalanceDecisionRequest? defaultDecision)
    {
        var action = row.BalanceAction
            ?? defaultDecision?.Action
            ?? BalanceAction.None;

        var topUpType = row.TopUpTransactionType
            ?? defaultDecision?.TopUpTransactionType;

        return (action, topUpType);
    }

    private static StockImportBalanceDecisionContextDto? BuildBalanceDecisionContext(
        StockImportDefaultBalanceDecisionRequest? defaultDecision,
        ExecuteStockImportRowRequest row)
    {
        var action = row.BalanceAction ?? defaultDecision?.Action;
        var topUpType = row.TopUpTransactionType ?? defaultDecision?.TopUpTransactionType;

        if (action is null && topUpType is null)
            return null;

        return new StockImportBalanceDecisionContextDto
        {
            Action = action,
            TopUpTransactionType = topUpType,
            DecisionScope = row.BalanceAction.HasValue ? "row_override" : "global_default"
        };
    }

    private static StockImportRowFailureException CreateBalanceActionFailure(
        string message,
        BalanceAction action,
        CurrencyTransactionType? topUpTransactionType)
    {
        var invalidValue = action == BalanceAction.TopUp
            ? topUpTransactionType?.ToString() ?? action.ToString()
            : action.ToString();

        return new StockImportRowFailureException(
            errorCode: ErrorCodeBalanceActionRequired,
            fieldName: "balanceAction",
            message: message,
            correctionGuidance: "請先指定 Margin 或 TopUp，並在 TopUp 時提供合法的入帳類型。",
            invalidValue: invalidValue);
    }

    private static string? ResolveInvalidValueForField(
        string fieldName,
        string? normalizedTradeSide,
        ExecuteStockImportRowRequest row)
    {
        return fieldName switch
        {
            "balanceAction" => row.BalanceAction?.ToString(),
            FieldTicker => row.Ticker,
            FieldConfirmedTradeSide => normalizedTradeSide,
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

    private static bool IsTopUpIncomeType(CurrencyTransactionType transactionType)
        => transactionType is CurrencyTransactionType.ExchangeBuy
            or CurrencyTransactionType.Interest
            or CurrencyTransactionType.InitialBalance
            or CurrencyTransactionType.OtherIncome
            or CurrencyTransactionType.Deposit;

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
        ICollection<StockImportExecuteRowResultDto> results,
        ICollection<StockImportDiagnosticDto> diagnostics,
        string errorCode,
        string fieldName,
        string? invalidValue,
        string message,
        string correctionGuidance,
        string? confirmedTradeSide,
        StockImportBalanceDecisionContextDto? balanceDecision = null)
    {
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
            InvalidValue = string.IsNullOrWhiteSpace(invalidValue) ? null : invalidValue,
            ErrorCode = errorCode,
            Message = message,
            CorrectionGuidance = correctionGuidance
        });
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
