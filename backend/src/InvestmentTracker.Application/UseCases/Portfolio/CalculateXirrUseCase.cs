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
/// 計算投資組合 XIRR（Extended Internal Rate of Return）的 Use Case。
/// 會在計算持倉時套用拆股調整，以確保與現價比較一致。
/// US9：使用交易日 FX cache 自動補齊缺漏匯率。
/// </summary>
public class CalculateXirrUseCase(
    IPortfolioRepository portfolioRepository,
    IStockTransactionRepository transactionRepository,
    IStockSplitRepository stockSplitRepository,
    PortfolioCalculator portfolioCalculator,
    StockSplitAdjustmentService splitAdjustmentService,
    ITransactionDateExchangeRateService txDateFxService,
    ICurrentUserService currentUserService,
    ILogger<CalculateXirrUseCase> logger)
{
    private const string ImportExecuteOpeningBaselineNote = "import-execute-opening-baseline";
    private const string ImportExecuteAdjustmentNote = "import-execute-adjustment";
    public async Task<XirrResultDto> ExecuteAsync(
        Guid portfolioId,
        CalculateXirrRequest request,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Portfolio), portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
        {
            throw new AccessDeniedException("You do not have access to this portfolio");
        }

        var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);
        var asOfDate = (request.AsOfDate ?? DateTime.UtcNow.Date).Date;
        var homeCurrency = portfolio.HomeCurrency.ToUpperInvariant();

        // 建立現金流清單（包含截至 asOfDate 的有效交易）
        // US9：自動補齊缺漏匯率
        var cashFlows = new List<CashFlow>();
        var missingFxDates = new List<MissingExchangeRateDto>();

        var scopedTransactions = transactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate)
            .ToList();

        var orderedTransactions = scopedTransactions
            .OrderBy(t => t.TransactionDate)
            .ToList();

        foreach (var tx in orderedTransactions)
        {
            var txCurrency = tx.Currency.ToString().ToUpperInvariant();
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
                    "Missing exchange rate for transaction {TxId} on {Date} ({FromCurrency}->{ToCurrency})",
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

        // 將目前投資組合市值作為最後一筆現金流
        if (request.CurrentPrices is { Count: > 0 })
        {
            logger.LogDebug("XIRR: Received {Count} current prices", request.CurrentPrices.Count);

            // 轉為不分大小寫的 dictionary，避免 ticker 比對失敗
            var currentPrices = new Dictionary<string, CurrentPriceInfo>(
                request.CurrentPrices, StringComparer.OrdinalIgnoreCase);

            // 使用拆股調整後的持倉，確保與現價比較一致
            var positions = portfolioCalculator.RecalculateAllPositionsWithSplitAdjustments(
                scopedTransactions, stockSplits, splitAdjustmentService).ToList();

            logger.LogDebug("XIRR: Found {Count} positions", positions.Count);

            var currentValue = 0m;

            foreach (var position in positions)
            {
                if (currentPrices.TryGetValue(position.Ticker, out var priceInfo))
                {
                    // 使用本位幣市值（price × shares × current exchange rate）
                    var positionValue = position.TotalShares * priceInfo.Price * priceInfo.ExchangeRate;
                    logger.LogDebug("XIRR: Position {Ticker}: {Shares} shares * {Price} * {Rate} = {Value}",
                        position.Ticker, position.TotalShares, priceInfo.Price, priceInfo.ExchangeRate, positionValue);
                    currentValue += positionValue;
                }
                else
                {
                    logger.LogDebug("XIRR: No price found for position {Ticker}. Available prices: {Keys}",
                        position.Ticker, string.Join(", ", currentPrices.Keys));
                }
            }

            logger.LogDebug("XIRR: Total current value = {Value}", currentValue);

            if (currentValue > 0)
            {
                cashFlows.Add(new CashFlow(currentValue, asOfDate));
            }
        }
        else
        {
            logger.LogDebug("XIRR: No current prices provided. CurrentPrices is {Status}",
                request.CurrentPrices == null ? "null" : $"empty (count: {request.CurrentPrices.Count})");
        }

        logger.LogDebug("XIRR: Total cash flows = {Count}", cashFlows.Count);

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

    /// <summary>
    /// 計算單一持倉（ticker）的 XIRR。
    /// </summary>
    public async Task<XirrResultDto> ExecuteForPositionAsync(
        Guid portfolioId,
        string ticker,
        CalculatePositionXirrRequest request,
        CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException(nameof(Portfolio), portfolioId);

        if (portfolio.UserId != currentUserService.UserId)
        {
            throw new AccessDeniedException("You do not have access to this portfolio");
        }

        var allTransactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);
        var asOfDate = (request.AsOfDate ?? DateTime.UtcNow.Date).Date;
        var homeCurrency = portfolio.HomeCurrency.ToUpperInvariant();
        var scopedTransactions = allTransactions
            .Where(t => !t.IsDeleted && t.TransactionDate.Date <= asOfDate)
            .ToList();

        // 僅保留此 ticker 的交易
        var tickerTransactions = scopedTransactions
            .Where(t => t.Ticker.Equals(ticker, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TransactionDate)
            .ToList();

        if (tickerTransactions.Count == 0)
        {
            return new XirrResultDto
            {
                Xirr = null,
                XirrPercentage = null,
                CashFlowCount = 0,
                AsOfDate = asOfDate,
                EarliestTransactionDate = null
            };
        }

        // 建立現金流清單（包含所有交易，並自動補齊匯率）
        var cashFlows = new List<CashFlow>();
        var missingFxDates = new List<MissingExchangeRateDto>();

        foreach (var tx in tickerTransactions)
        {
            var txCurrency = tx.Currency.ToString().ToUpperInvariant();
            var fxRate = await GetExchangeRateForTransactionAsync(tx, homeCurrency, cancellationToken);

            if (!fxRate.HasValue)
            {
                missingFxDates.Add(new MissingExchangeRateDto
                {
                    TransactionDate = tx.TransactionDate,
                    Currency = txCurrency
                });
                continue;
            }

            if (TryBuildCashFlowAmount(tx, fxRate.Value, out var cashFlowAmount))
            {
                cashFlows.Add(new CashFlow(cashFlowAmount, tx.TransactionDate));
            }
        }

        // 將目前持倉市值作為最後一筆現金流
        if (request.CurrentPrice.HasValue)
        {
            // 使用拆股調整後的持倉，確保與現價比較一致
            var position = portfolioCalculator.CalculatePositionWithSplitAdjustments(
                ticker, scopedTransactions, stockSplits, splitAdjustmentService);

            if (position.TotalShares > 0)
            {
                // 使用本位幣市值
                var currentValue = position.TotalShares * request.CurrentPrice.Value * (request.CurrentExchangeRate ?? 1m);
                cashFlows.Add(new CashFlow(currentValue, asOfDate));
            }
        }

        var xirr = portfolioCalculator.CalculateXirr(cashFlows);

        // 取得最早的交易日期
        var earliestDate = tickerTransactions.FirstOrDefault()?.TransactionDate;

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
    /// 1. 若交易本身已有 ExchangeRate 且目標幣別即 homeCurrency，直接使用
    /// 2. 若交易原幣別與目標幣別相同，回傳 1.0
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

        // 若交易本身已有匯率且為「交易幣別 -> 本位幣」語義，優先使用
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
