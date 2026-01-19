namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 年末歷史資料快取的資料類型。
/// </summary>
public enum HistoricalDataType
{
    /// <summary>年末股價</summary>
    StockPrice = 0,

    /// <summary>年末匯率</summary>
    ExchangeRate = 1
}
