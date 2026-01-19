using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

/// <summary>
/// 依據股票分割事件調整交易顯示值的領域服務（Domain Service）。
/// 調整在顯示時即時計算，原始資料保持不變。
/// </summary>
public class StockSplitAdjustmentService
{
    /// <summary>
    /// 計算指定代號／市場在某個交易日期的累積分割比例。
    /// 回傳所有「交易日期之後」發生的 split ratio 乘積。
    /// </summary>
    /// <param name="symbol">股票代號</param>
    /// <param name="market">股票市場</param>
    /// <param name="transactionDate">原始交易日期</param>
    /// <param name="splits">所有已知的股票分割事件</param>
    /// <returns>累積分割比例（若不適用任何分割則為 1.0）</returns>
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
    /// 取得考量後續所有分割事件後的調整股數。
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
    /// 取得考量後續所有分割事件後的調整每股價格。
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
    /// 計算套用所有適用股票分割事件後的交易調整值。
    /// 若不適用任何分割，則回傳原始值。
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
    /// 依據 ticker 樣式推測股票市場。
    /// 台股：純數字或數字加尾碼（例如：0050、2330、6547R）
    /// 美股／英股：字母（例如：AAPL、VOO、VWRA）
    /// </summary>
    public StockMarket DetectMarket(string ticker)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            return StockMarket.US;

        // 台股：以數字開頭（例如：0050、2330、6547R）
        if (char.IsDigit(ticker[0]))
            return StockMarket.TW;

        // 英股：常見為 .L 後綴（London Stock Exchange）
        if (ticker.EndsWith(".L", StringComparison.OrdinalIgnoreCase))
            return StockMarket.UK;

        // 預設：字母 ticker 視為美股
        return StockMarket.US;
    }
}

/// <summary>
/// 套用分割調整後，交易的原始值與調整值。
/// </summary>
public record AdjustedTransactionValues(
    decimal OriginalShares,
    decimal AdjustedShares,
    decimal OriginalPrice,
    decimal AdjustedPrice,
    decimal SplitRatio,
    bool HasSplitAdjustment
);
