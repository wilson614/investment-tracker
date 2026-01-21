using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

// 註：PortfolioCalculator 會處理 TransactionType.Split（手動分割調整）。
// 若要依 StockSplits 資料表自動套用分割調整，請在 Application layer 使用 StockSplitAdjustmentService
// 先調整歷史交易數值，再呼叫 CalculatePosition。

/// <summary>
/// 投資組合計算的領域服務（Domain Service），包含部位追蹤、移動平均成本與損益（PnL）計算。
/// </summary>
public class PortfolioCalculator
{
    /// <summary>
    /// Calculates the current position for a ticker based on transaction history.
    /// Uses moving average cost method for cost basis calculation.
    /// </summary>
    public StockPosition CalculatePosition(string ticker, IEnumerable<StockTransaction> transactions)
    {
        var tickerTransactions = transactions
            .Where(t => t.Ticker == ticker && !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        if (tickerTransactions.Count == 0)
        {
            return new StockPosition(ticker, 0m, 0m, 0m, 0m, 0m);
        }

        var totalShares = 0m;
        var totalCostHome = 0m;
        var totalCostSource = 0m;

        foreach (var transaction in tickerTransactions)
        {
            switch (transaction.TransactionType)
            {
                case TransactionType.Buy:
                    totalShares += transaction.Shares;
                    // 僅在有匯率時才納入本位幣成本
                    if (transaction.TotalCostHome.HasValue)
                        totalCostHome += transaction.TotalCostHome.Value;
                    totalCostSource += transaction.TotalCostSource;
                    break;

                case TransactionType.Sell:
                    if (totalShares > 0)
                    {
                        // 賣出前先計算每股平均成本
                        var avgCostPerShareHome = totalCostHome / totalShares;
                        var avgCostPerShareSource = totalCostSource / totalShares;
                        // 扣除賣出股數對應的成本
                        totalCostHome -= transaction.Shares * avgCostPerShareHome;
                        totalCostSource -= transaction.Shares * avgCostPerShareSource;
                        totalShares -= transaction.Shares;
                    }
                    break;

                case TransactionType.Split:
                    // 調整股數，但總成本不變
                    // 分割比例存放在 Shares 欄位（例如：2 表示 2:1 split）
                    totalShares *= transaction.Shares;
                    break;

                case TransactionType.Adjustment:
                    // 直接調整股數與／或成本
                    totalShares += transaction.Shares;
                    // 僅在有匯率時才納入本位幣成本
                    if (transaction.TotalCostHome.HasValue)
                        totalCostHome += transaction.TotalCostHome.Value;
                    totalCostSource += transaction.TotalCostSource;
                    break;
            }
        }

        // 避免因四捨五入導致的負值
        totalShares = Math.Max(0, totalShares);
        totalCostHome = Math.Max(0, totalCostHome);
        totalCostSource = Math.Max(0, totalCostSource);

        var averageCostHome = totalShares > 0 ? totalCostHome / totalShares : 0m;
        var averageCostSource = totalShares > 0 ? totalCostSource / totalShares : 0m;

        return new StockPosition(ticker, totalShares, totalCostHome, totalCostSource, averageCostHome, averageCostSource);
    }

    /// <summary>
    /// 依據目前市價計算未實現損益（unrealized PnL）。
    /// </summary>
    public UnrealizedPnl CalculateUnrealizedPnl(
        StockPosition position,
        decimal currentPriceSource,
        decimal currentExchangeRate)
    {
        if (position.TotalShares == 0)
        {
            return new UnrealizedPnl(0m, 0m, 0m);
        }

        var currentValueHome = position.TotalShares * currentPriceSource * currentExchangeRate;
        var unrealizedPnlHome = currentValueHome - position.TotalCostHome;
        var percentage = position.TotalCostHome > 0
            ? unrealizedPnlHome / position.TotalCostHome * 100
            : 0m;

        return new UnrealizedPnl(currentValueHome, unrealizedPnlHome, percentage);
    }

    /// <summary>
    /// 以平均成本法計算單筆賣出交易的已實現損益（realized PnL）。
    /// </summary>
    public decimal CalculateRealizedPnl(StockPosition positionBeforeSell, StockTransaction sellTransaction)
    {
        if (sellTransaction.TransactionType != TransactionType.Sell)
        {
            throw new ArgumentException("Transaction must be a sell transaction", nameof(sellTransaction));
        }

        // 賣出股數對應的成本基礎（使用平均成本）
        var costBasis = sellTransaction.Shares * positionBeforeSell.AverageCostPerShareHome;

        // 賣出金額小計（Shares × Price）
        var subtotal = sellTransaction.Shares * sellTransaction.PricePerShare;

        // 台股小計採無條件捨去
        if (sellTransaction.IsTaiwanStock)
            subtotal = Math.Floor(subtotal);

        // 本位幣賣出入帳金額（扣除手續費）
        // 若無匯率則使用 1.0（表示來源幣別 = 本位幣）
        var exchangeRate = sellTransaction.ExchangeRate ?? 1.0m;
        var saleProceeds = (subtotal - sellTransaction.Fees) * exchangeRate;

        return saleProceeds - costBasis;
    }

    /// <summary>
    /// Recalculates all positions for a portfolio.
    /// </summary>
    public IEnumerable<StockPosition> RecalculateAllPositions(IEnumerable<StockTransaction> transactions)
    {
        var transactionList = transactions.Where(t => !t.IsDeleted).ToList();
        var tickers = transactionList.Select(t => t.Ticker).Distinct();

        return tickers.Select(ticker => CalculatePosition(ticker, transactionList));
    }

    /// <summary>
    /// Recalculates all positions with stock split adjustments applied.
    /// Uses the StockSplitAdjustmentService to adjust historical transactions.
    /// </summary>
    public IEnumerable<StockPosition> RecalculateAllPositionsWithSplitAdjustments(
        IEnumerable<StockTransaction> transactions,
        IEnumerable<StockSplit> splits,
        StockSplitAdjustmentService splitService)
    {
        var transactionList = transactions.Where(t => !t.IsDeleted).ToList();
        var splitList = splits.ToList();
        var tickers = transactionList.Select(t => t.Ticker).Distinct();

        return tickers.Select(ticker => CalculatePositionWithSplitAdjustments(
            ticker, transactionList, splitList, splitService));
    }

    /// <summary>
    /// Calculates position for a ticker with stock split adjustments applied.
    /// </summary>
    public StockPosition CalculatePositionWithSplitAdjustments(
        string ticker,
        IEnumerable<StockTransaction> transactions,
        IReadOnlyList<StockSplit> splits,
        StockSplitAdjustmentService splitService)
    {
        var tickerTransactions = transactions
            .Where(t => t.Ticker == ticker && !t.IsDeleted)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        if (tickerTransactions.Count == 0)
        {
            return new StockPosition(ticker, 0m, 0m, 0m, 0m, 0m);
        }

        var totalShares = 0m;
        var totalCostHome = 0m;
        var totalCostSource = 0m;

        foreach (var transaction in tickerTransactions)
        {
            // Get split-adjusted values for this transaction
            var adjusted = splitService.GetAdjustedValues(transaction, splits);
            var adjustedShares = adjusted.AdjustedShares;

            switch (transaction.TransactionType)
            {
                case TransactionType.Buy:
                    totalShares += adjustedShares;
                    // Total cost remains unchanged after split adjustment
                    // Only add to home currency totals if exchange rate exists
                    if (transaction.TotalCostHome.HasValue)
                        totalCostHome += transaction.TotalCostHome.Value;
                    totalCostSource += transaction.TotalCostSource;
                    break;

                case TransactionType.Sell:
                    if (totalShares > 0)
                    {
                        var avgCostPerShareHome = totalCostHome / totalShares;
                        var avgCostPerShareSource = totalCostSource / totalShares;
                        totalCostHome -= adjustedShares * avgCostPerShareHome;
                        totalCostSource -= adjustedShares * avgCostPerShareSource;
                        totalShares -= adjustedShares;
                    }
                    break;

                case TransactionType.Split:
                    // Manual split transactions still apply
                    totalShares *= transaction.Shares;
                    break;

                case TransactionType.Adjustment:
                    totalShares += adjustedShares;
                    // Only add to home currency totals if exchange rate exists
                    if (transaction.TotalCostHome.HasValue)
                        totalCostHome += transaction.TotalCostHome.Value;
                    totalCostSource += transaction.TotalCostSource;
                    break;
            }
        }

        totalShares = Math.Max(0, totalShares);
        totalCostHome = Math.Max(0, totalCostHome);
        totalCostSource = Math.Max(0, totalCostSource);

        var averageCostHome = totalShares > 0 ? totalCostHome / totalShares : 0m;
        var averageCostSource = totalShares > 0 ? totalCostSource / totalShares : 0m;

        return new StockPosition(ticker, totalShares, totalCostHome, totalCostSource, averageCostHome, averageCostSource);
    }

    /// <summary>
    /// Calculates XIRR (Extended Internal Rate of Return) using Newton-Raphson method.
    /// </summary>
    /// <param name="cashFlows">List of cash flows with amounts and dates</param>
    /// <param name="maxIterations">Maximum iterations for convergence</param>
    /// <param name="tolerance">Tolerance for convergence check</param>
    /// <returns>Annual rate of return, or null if calculation fails</returns>
    public double? CalculateXirr(
        IEnumerable<CashFlow> cashFlows,
        int maxIterations = 100,
        double tolerance = 1e-7)
    {
        var flows = cashFlows.OrderBy(cf => cf.Date).ToList();

        // Need at least 2 cash flows
        if (flows.Count < 2)
            return null;

        // Need both positive and negative cash flows
        var hasPositive = flows.Any(cf => cf.Amount > 0);
        var hasNegative = flows.Any(cf => cf.Amount < 0);
        if (!hasPositive || !hasNegative)
            return null;

        var firstDate = flows[0].Date;

        // Convert to year fractions
        var yearFractions = flows.Select(cf => (cf.Date - firstDate).TotalDays / 365.0).ToList();
        var amounts = flows.Select(cf => (double)cf.Amount).ToList();

        // Initial guess
        var rate = 0.1;

        // Newton-Raphson iteration
        for (var i = 0; i < maxIterations; i++)
        {
            var npv = CalculateNpv(amounts, yearFractions, rate);
            var derivative = CalculateNpvDerivative(amounts, yearFractions, rate);

            if (Math.Abs(derivative) < 1e-10)
            {
                // Derivative too small, try different starting point
                rate += 0.1;
                continue;
            }

            var newRate = rate - npv / derivative;

            // Check for convergence
            if (Math.Abs(newRate - rate) < tolerance)
            {
                return Math.Round(newRate, 6);
            }

            // Prevent rate from becoming invalid/extreme
            if (double.IsNaN(newRate) || double.IsInfinity(newRate))
            {
                break;
            }

            newRate = newRate switch
            {
                < -0.999 => -0.999,
                > 1_000_000 => 1_000_000,
                _ => newRate
            };

            rate = newRate;
        }

        // Try alternative method with bisection if Newton-Raphson fails
        return CalculateXirrBisection(amounts, yearFractions);
    }

    private static double CalculateNpv(List<double> amounts, List<double> yearFractions, double rate)
    {
        double npv = 0;
        for (var i = 0; i < amounts.Count; i++)
        {
            npv += amounts[i] / Math.Pow(1 + rate, yearFractions[i]);
        }
        return npv;
    }

    private static double CalculateNpvDerivative(List<double> amounts, List<double> yearFractions, double rate)
    {
        double derivative = 0;
        for (var i = 0; i < amounts.Count; i++)
        {
            derivative -= yearFractions[i] * amounts[i] / Math.Pow(1 + rate, yearFractions[i] + 1);
        }
        return derivative;
    }

    private static double? CalculateXirrBisection(List<double> amounts, List<double> yearFractions, int maxIterations = 100)
    {
        static bool SameSign(double a, double b) =>
            (a > 0 && b > 0) || (a < 0 && b < 0);

        var low = -0.999;
        var high = 10.0;
        const double maxHigh = 1_000_000;

        var npvLow = CalculateNpv(amounts, yearFractions, low);
        var npvHigh = CalculateNpv(amounts, yearFractions, high);

        if (Math.Abs(npvLow) < 1e-7)
            return Math.Round(low, 6);

        if (Math.Abs(npvHigh) < 1e-7)
            return Math.Round(high, 6);

        // Expand high bound for very short holding periods (high annualized returns)
        while (SameSign(npvLow, npvHigh) && high < maxHigh)
        {
            high = Math.Min(maxHigh, high * 10);
            npvHigh = CalculateNpv(amounts, yearFractions, high);

            if (Math.Abs(npvHigh) < 1e-7)
                return Math.Round(high, 6);
        }

        // Check if solution exists in range
        if (SameSign(npvLow, npvHigh))
            return null;

        for (var i = 0; i < maxIterations; i++)
        {
            var mid = (low + high) / 2;
            var npvMid = CalculateNpv(amounts, yearFractions, mid);

            if (Math.Abs(npvMid) < 1e-7)
                return Math.Round(mid, 6);

            if (SameSign(npvLow, npvMid))
            {
                low = mid;
                npvLow = npvMid;
            }
            else
            {
                high = mid;
            }
        }

        return Math.Round((low + high) / 2, 6);
    }
}

/// <summary>
/// Represents a stock position with calculated values.
/// </summary>
public record StockPosition(
    string Ticker,
    decimal TotalShares,
    decimal TotalCostHome,
    decimal TotalCostSource,
    decimal AverageCostPerShareHome,
    decimal AverageCostPerShareSource);

/// <summary>
/// Represents unrealized profit/loss calculation result.
/// </summary>
public record UnrealizedPnl(
    decimal CurrentValueHome,
    decimal UnrealizedPnlHome,
    decimal UnrealizedPnlPercentage);

/// <summary>
/// Represents a cash flow for XIRR calculation.
/// Negative amounts are outflows (investments), positive amounts are inflows (returns).
/// </summary>
public record CashFlow(decimal Amount, DateTime Date);
