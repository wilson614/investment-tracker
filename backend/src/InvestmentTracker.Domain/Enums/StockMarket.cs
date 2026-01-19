namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 支援的股票市場。
/// </summary>
public enum StockMarket
{
    /// <summary>台灣證券交易所</summary>
    TW = 1,

    /// <summary>美股市場（透過 Sina）</summary>
    US = 2,

    /// <summary>英國／倫敦證券交易所（透過 Sina）</summary>
    UK = 3
}
