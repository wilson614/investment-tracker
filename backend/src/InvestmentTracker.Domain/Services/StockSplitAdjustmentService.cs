using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

/// <summary>
/// Domain service for adjusting stock transaction values based on stock split events.
/// Adjustments are calculated at display time; original data is preserved unchanged.
/// </summary>
public class StockSplitAdjustmentService
{
    /// <summary>
    /// Calculates the cumulative split ratio for a given symbol/market at a specific transaction date.
    /// Returns the product of all split ratios for splits occurring AFTER the transaction date.
    /// </summary>
    /// <param name="symbol">Stock symbol</param>
    /// <param name="market">Stock market</param>
    /// <param name="transactionDate">Date of the original transaction</param>
    /// <param name="splits">All known stock splits</param>
    /// <returns>Cumulative split ratio (1.0 if no splits apply)</returns>
    public decimal GetCumulativeSplitRatio(
        string symbol,
        StockMarket market,
        DateTime transactionDate,
        IEnumerable<StockSplit> splits)
    {
        var applicableSplits = splits
            .Where(s => s.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)
                        && s.Market == market
                        && s.SplitDate > transactionDate.Date)
            .OrderBy(s => s.SplitDate)
            .ToList();

        if (!applicableSplits.Any())
            return 1.0m;

        return applicableSplits.Aggregate(1.0m, (acc, split) => acc * split.SplitRatio);
    }

    /// <summary>
    /// Gets the adjusted number of shares for a transaction considering all subsequent splits.
    /// AdjustedShares = OriginalShares × CumulativeSplitRatio
    /// </summary>
    public decimal GetAdjustedShares(
        decimal originalShares,
        string symbol,
        StockMarket market,
        DateTime transactionDate,
        IEnumerable<StockSplit> splits)
    {
        var ratio = GetCumulativeSplitRatio(symbol, market, transactionDate, splits);
        return originalShares * ratio;
    }

    /// <summary>
    /// Gets the adjusted price per share for a transaction considering all subsequent splits.
    /// AdjustedPrice = OriginalPrice / CumulativeSplitRatio
    /// </summary>
    public decimal GetAdjustedPrice(
        decimal originalPrice,
        string symbol,
        StockMarket market,
        DateTime transactionDate,
        IEnumerable<StockSplit> splits)
    {
        var ratio = GetCumulativeSplitRatio(symbol, market, transactionDate, splits);
        if (ratio == 0) return originalPrice;
        return originalPrice / ratio;
    }

    /// <summary>
    /// Calculates adjusted values for a transaction considering all applicable stock splits.
    /// Returns original values if no splits apply.
    /// </summary>
    public AdjustedTransactionValues GetAdjustedValues(
        StockTransaction transaction,
        IEnumerable<StockSplit> splits)
    {
        var market = DetectMarket(transaction.Ticker);
        var ratio = GetCumulativeSplitRatio(transaction.Ticker, market, transaction.TransactionDate, splits);

        return new AdjustedTransactionValues(
            OriginalShares: transaction.Shares,
            AdjustedShares: transaction.Shares * ratio,
            OriginalPrice: transaction.PricePerShare,
            AdjustedPrice: ratio != 0 ? transaction.PricePerShare / ratio : transaction.PricePerShare,
            SplitRatio: ratio,
            HasSplitAdjustment: ratio != 1.0m
        );
    }

    /// <summary>
    /// Detects the stock market based on ticker symbol pattern.
    /// Taiwan stocks: numeric only or numeric with suffix (e.g., 0050, 2330, 6547R)
    /// US/UK stocks: alphabetic (e.g., AAPL, VOO, VWRA)
    /// </summary>
    public StockMarket DetectMarket(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return StockMarket.US;

        // Taiwan stocks: start with digit (e.g., 0050, 2330, 6547R)
        if (char.IsDigit(ticker[0]))
            return StockMarket.TW;

        // Check for UK LSE patterns (commonly .L suffix, or known UK ETFs)
        if (ticker.EndsWith(".L", StringComparison.OrdinalIgnoreCase))
            return StockMarket.UK;

        // Default to US for alphabetic tickers
        return StockMarket.US;
    }
}

/// <summary>
/// Contains original and adjusted values for a transaction after applying split adjustments.
/// </summary>
public record AdjustedTransactionValues(
    decimal OriginalShares,
    decimal AdjustedShares,
    decimal OriginalPrice,
    decimal AdjustedPrice,
    decimal SplitRatio,
    bool HasSplitAdjustment
);
