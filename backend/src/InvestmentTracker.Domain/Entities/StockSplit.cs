using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 股票分割記錄實體，用於自動調整歷史交易數值
/// </summary>
public class StockSplit : BaseEntity
{
    /// <summary>股票/ETF 代號（如 0050、AAPL）</summary>
    public string Symbol { get; private set; } = string.Empty;

    /// <summary>股票交易市場</summary>
    public StockMarket Market { get; private set; }

    /// <summary>分割生效日期</summary>
    public DateTime SplitDate { get; private set; }

    /// <summary>股數乘數（例如：1拆4 為 4.0，2併1 為 0.5）</summary>
    public decimal SplitRatio { get; private set; }

    /// <summary>易讀描述（例如「1拆4」）</summary>
    public string? Description { get; private set; }

    // EF Core 必要的無參數建構子
    private StockSplit() { }

    public StockSplit(string symbol, StockMarket market, DateTime splitDate, decimal splitRatio, string? description = null)
    {
        SetSymbol(symbol);
        SetMarket(market);
        SetSplitDate(splitDate);
        SetSplitRatio(splitRatio);
        Description = description;
    }

    public void SetSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        if (symbol.Length > 20)
            throw new ArgumentException("Symbol cannot exceed 20 characters", nameof(symbol));

        Symbol = symbol.Trim().ToUpperInvariant();
    }

    public void SetMarket(StockMarket market)
    {
        if (!Enum.IsDefined(typeof(StockMarket), market))
            throw new ArgumentException("Invalid market", nameof(market));

        Market = market;
    }

    public void SetSplitDate(DateTime splitDate)
    {
        if (splitDate == default)
            throw new ArgumentException("Split date is required", nameof(splitDate));

        // 確保 UTC Kind 以相容 PostgreSQL
        SplitDate = DateTime.SpecifyKind(splitDate.Date, DateTimeKind.Utc);
    }

    public void SetSplitRatio(decimal splitRatio)
    {
        if (splitRatio <= 0)
            throw new ArgumentException("Split ratio must be greater than 0", nameof(splitRatio));

        SplitRatio = splitRatio;
    }

    public void SetDescription(string? description)
    {
        if (description != null && description.Length > 100)
            throw new ArgumentException("Description cannot exceed 100 characters", nameof(description));

        Description = description?.Trim();
    }

    public void Update(DateTime splitDate, decimal splitRatio, string? description)
    {
        SetSplitDate(splitDate);
        SetSplitRatio(splitRatio);
        SetDescription(description);
    }
}
