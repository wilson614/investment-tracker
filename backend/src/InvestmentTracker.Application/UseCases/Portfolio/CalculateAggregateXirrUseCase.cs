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
    public async Task<XirrResultDto> ExecuteAsync(
        CalculateXirrRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolios = await portfolioRepository.GetByUserIdAsync(userId, cancellationToken);
        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);

        var allTransactions = new List<StockTransaction>();
        foreach (var portfolio in portfolios)
        {
            var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolio.Id, cancellationToken);
            allTransactions.AddRange(transactions);
        }

        // 建立現金流清單（包含所有未刪除的交易）
        // US9：自動補齊缺漏匯率
        var cashFlows = new List<CashFlow>();
        var missingFxDates = new List<MissingExchangeRateDto>();

        var orderedTransactions = allTransactions
            .Where(t => !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ToList();

        foreach (var tx in orderedTransactions)
        {
            var fxRate = await GetExchangeRateForTransactionAsync(tx, cancellationToken);

            if (!fxRate.HasValue)
            {
                // 紀錄缺漏匯率（用於回報給前端）
                var currency = tx.IsTaiwanStock ? "TWD" : "USD"; // Default assumption
                missingFxDates.Add(new MissingExchangeRateDto
                {
                    TransactionDate = tx.TransactionDate,
                    Currency = currency
                });
                logger.LogWarning("Aggregate XIRR: Missing exchange rate for transaction {TxId} on {Date}",
                    tx.Id, tx.TransactionDate.ToString("yyyy-MM-dd"));
                continue; // XIRR 計算中略過此交易
            }

            switch (tx.TransactionType)
            {
                case TransactionType.Buy:
                {
                    // 使用本位幣成本（TotalCostSource × ExchangeRate）
                    var homeCost = tx.TotalCostSource * fxRate.Value;
                    cashFlows.Add(new CashFlow(-homeCost, tx.TransactionDate));
                    break;
                }
                case TransactionType.Sell:
                {
                    // 使用本位幣賣出收入
                    var proceeds = (tx.Shares * tx.PricePerShare - tx.Fees) * fxRate.Value;
                    cashFlows.Add(new CashFlow(proceeds, tx.TransactionDate));
                    break;
                }
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
                orderedTransactions, stockSplits, splitAdjustmentService).ToList();

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
                cashFlows.Add(new CashFlow(currentValue, request.AsOfDate ?? DateTime.UtcNow.Date));
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
            AsOfDate = request.AsOfDate ?? DateTime.UtcNow.Date,
            EarliestTransactionDate = earliestDate,
            MissingExchangeRates = missingFxDates.Count > 0 ? missingFxDates : null
        };
    }

    /// <summary>
    /// 取得單筆交易的匯率：
    /// 1. 若交易本身已有 ExchangeRate，直接使用
    /// 2. 若為台股（IsTaiwanStock），回傳 1.0
    /// 3. 否則嘗試從交易日 FX cache 取得
    /// </summary>
    private async Task<decimal?> GetExchangeRateForTransactionAsync(
        StockTransaction tx,
        CancellationToken cancellationToken)
    {
        // 若交易本身已有匯率，直接使用
        if (tx.HasExchangeRate)
        {
            return tx.ExchangeRate!.Value;
        }

        // 台股以 TWD 計價，不需要換匯
        if (tx.IsTaiwanStock)
        {
            return 1.0m;
        }

        // 嘗試從交易日 FX cache 取得
        // 非台股預設以 USD 為外幣（最常見外幣）
        var fxResult = await txDateFxService.GetOrFetchAsync(
            "USD", "TWD", tx.TransactionDate, cancellationToken);

        return fxResult?.Rate;
    }
}
