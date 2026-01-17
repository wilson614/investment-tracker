namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// Type of historical year-end data being cached.
/// </summary>
public enum HistoricalDataType
{
    /// <summary>Year-end stock price</summary>
    StockPrice = 0,

    /// <summary>Year-end exchange rate</summary>
    ExchangeRate = 1
}
