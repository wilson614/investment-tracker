using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Application.UseCases.Portfolio;

/// <summary>
/// 計算目前使用者所有投資組合合併後的 XIRR（Extended Internal Rate of Return）。
/// 會在計算持倉時套用拆股調整，以確保與現價比較一致。
/// US9：使用交易日 FX cache 自動補齊缺漏匯率。
/// </summary>
public class CalculateAggregateXirrUseCase(
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository transactionRepository,
    IStockSplitRepository stockSplitRepository,
    PortfolioCalculator portfolioCalculator,
    StockSplitAdjustmentService splitAdjustmentService,
    ITransactionDateExchangeRateService txDateFxService,
    ICurrentUserService currentUserService,
    ILogger<CalculateAggregateXirrUseCase> logger)
{
    private const string ImportExecuteOpeningBaselineNote = "import-execute-opening-baseline";
    private const string ImportExecuteAdjustmentNote = "import-execute-adjustment";
    public async Task<XirrResultDto> ExecuteAsync(
        CalculateXirrRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);

        var asOfDate = (request.AsOfDate ?? DateTime.UtcNow.Date).Date;
        var portfolioHomeCurrencyMap = portfolios.ToDictionary(p => p.Id, p => p.HomeCurrency.ToUpperInvariant());

        var allTransactions = new List<StockTransaction>();
        foreach (var portfolio in portfolios)
        {
            var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolio.Id, cancellationToken);
            allTransactions.AddRange(transactions);
        }

        // 建立現金流清單（包含截至 asOfDate 的有效交易）
        // US9：自動補齊缺漏匯率
        var cashFlows = new List<CashFlow>();
        var missingFxDates = new List<MissingExchangeRateDto>();

        var scopedTransactions = allTransactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate)
            .ToList();

        var orderedTransactions = scopedTransactions
            .OrderBy(t => t.TransactionDate)
            .ToList();

        foreach (var tx in orderedTransactions)
        {
            var txCurrency = tx.Currency.ToString().ToUpperInvariant();
            if (!portfolioHomeCurrencyMap.TryGetValue(tx.PortfolioId, out var homeCurrency))
                continue;

            var fxRate = await GetExchangeRateForTransactionAsync(tx, homeCurrency, cancellationToken);

            if (!fxRate.HasValue)
            {
                // 紀錄缺漏匯率（用於回報給前端）
                missingFxDates.Add(new MissingExchangeRateDto
                {
                    TransactionDate = tx.TransactionDate,
                    Currency = txCurrency
                });
                logger.LogWarning(
                    "Aggregate XIRR: Missing exchange rate for transaction {TxId} on {Date} ({FromCurrency}->{ToCurrency})",
                    tx.Id,
                    tx.TransactionDate.ToString("yyyy-MM-dd"),
                    txCurrency,
                    homeCurrency);
                continue; // XIRR 計算中略過此交易
            }

            if (TryBuildCashFlowAmount(tx, fxRate.Value, out var cashFlowAmount))
            {
                cashFlows.Add(new CashFlow(cashFlowAmount, tx.TransactionDate));
            }
        }

        // 將目前所有投資組合合併市值作為最後一筆現金流
        if (request.CurrentPrices is { Count: > 0 })
        {
            logger.LogDebug("Aggregate XIRR: Received {Count} current prices", request.CurrentPrices.Count);

            // 轉為不分大小寫的 dictionary，避免 ticker 比對失敗
            var currentPrices = new Dictionary<string, CurrentPriceInfo>(
                request.CurrentPrices, StringComparer.OrdinalIgnoreCase);

            // 使用拆股調整後的持倉，確保與現價比較一致
            var positions = portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
                scopedTransactions, stockSplits, splitAdjustmentService).ToList();

            logger.LogDebug("Aggregate XIRR: Found {Count} positions", positions.Count);

            var currentValue = 0m;

            foreach (var position in positions)
            {
                if (currentPrices.TryGetValue(position.Ticker, out var priceInfo))
                {
                    // 使用本位幣市值（price × shares × current exchange rate）
                    var positionValue = position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
                    logger.LogDebug(
                        "Aggregate XIRR: Position {Ticker}: {Shares} shares * {Price} * {Rate} = {Value}",
                        position.Ticker,
                        position.TotalShares,
                        priceInfo.Price,
                        priceInfo.ExchangeRate,
                        positionValue);
                    currentValue += positionValue;
                }
                else
                {
                    logger.LogDebug("Aggregate XIRR: No price found for position {Ticker}. Available prices: {Keys}",
                        position.Ticker,
                        string.Join(", ", currentPrices.Keys));
                }
            }

            logger.LogDebug("Aggregate XIRR: Total current value = {Value}", currentValue);

            if (currentValue > 0)
            {
                cashFlows.Add(new CashFlow(currentValue, asOfDate));
            }
        }
        else
        {
            logger.LogDebug("Aggregate XIRR: No current prices provided. CurrentPrices is {Status}",
                request.CurrentPrices == null ? "null" : $"empty (count: {request.CurrentPrices.Count})");
        }

        logger.LogDebug("Aggregate XIRR: Total cash flows = {Count}", cashFlows.Count);

        var xirr = portfolioCalculator.CalculateXirr(cashFlows);

        // 取得最早的交易日期
        var earliestDate = orderedTransactions.FirstOrDefault()?.TransactionDate;

        return new XirrResultDto
        {
            Xirr = xirr,
            XirrPercentage = xirr * 100,
            CashFlowCount = cashFlows.Count,
            AsOfDate = asOfDate,
            EarliestTransactionDate = earliestDate,
            MissingExchangeRates = missingFxDates.Count > 0 ? missingFxDates : null
        };
    }

    private static bool TryBuildCashFlowAmount(
        StockTransaction tx,
        decimal fxRate,
        out decimal amount)
    {
        switch (tx.TransactionType)
        {
            case TransactionType.Buy:
                // 使用本位幣成本（TotalCostSource × ExchangeRate）
                amount = -(tx.TotalCostSource * fxRate);
                return true;

            case TransactionType.Sell:
                // 使用本位幣賣出收入（台股小計 floor + fees 由 NetProceedsSource 統一處理）
                amount = tx.NetProceedsSource * fxRate;
                return true;

            case TransactionType.Adjustment:
                if (!IsImportInitializationAdjustment(tx))
                {
                    amount = 0m;
                    return false;
                }

                // 匯入初始化 Adjustment（import-execute-opening-baseline / import-execute-adjustment）
                // 需視為期初外部投入，否則只有賣出/現值正向現金流時 XIRR 會因缺少負向現金流而為 null。
                amount = -(ResolveAdjustmentCostSource(tx) * fxRate);
                return amount != 0m;

            default:
                amount = 0m;
                return false;
        }
    }

    private static bool IsImportInitializationAdjustment(StockTransaction tx)
    {
        if (tx.TransactionType != TransactionType.Adjustment)
        {
            return false;
        }

        var note = tx.Notes?.Trim();
        return string.Equals(note, ImportExecuteOpeningBaselineNote, StringComparison.Ordinal)
            || string.Equals(note, ImportExecuteAdjustmentNote, StringComparison.Ordinal);
    }

    private static decimal ResolveAdjustmentCostSource(StockTransaction tx)
    {
        if (tx.HistoricalTotalCost.HasValue)
        {
            return tx.HistoricalTotalCost.Value;
        }

        if (tx.MarketValueAtImport.HasValue)
        {
            return tx.MarketValueAtImport.Value;
        }

        return tx.TotalCostSource;
    }

    /// <summary>
    /// 取得單筆交易換算到目標幣別的匯率：
    /// 1. 若交易原幣別與目標幣別相同，回傳 1.0
    /// 2. 若交易本身已有 ExchangeRate，直接使用
    /// 3. 否則嘗試從交易日 FX cache 取得
    /// </summary>
    private async Task<decimal?> GetExchangeRateForTransactionAsync(
        StockTransaction tx,
        string homeCurrency,
        CancellationToken cancellationToken)
    {
        var txCurrency = tx.Currency.ToString().ToUpperInvariant();
        var normalizedHomeCurrency = homeCurrency.ToUpperInvariant();

        if (string.Equals(txCurrency, normalizedHomeCurrency, StringComparison.OrdinalIgnoreCase))
            return 1m;

        if (tx.HasExchangeRate)
            return tx.ExchangeRate!.Value;

        var fxResult = await txDateFxService.GetOrFetchAsync(
            txCurrency,
            normalizedHomeCurrency,
            tx.TransactionDate,
            cancellationToken);

        return fxResult?.Rate;
    }
}
