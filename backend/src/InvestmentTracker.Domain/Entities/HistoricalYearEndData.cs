using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 年末歷史資料快取實體，儲存年末股價和匯率
/// 用於避免重複呼叫 Stooq/TWSE API 並防止頻率限制問題
/// </summary>
public class HistoricalYearEndData
{
    /// <summary>自動遞增主鍵</summary>
    public int Id { get; private set; }

    /// <summary>資料類型：StockPrice 或 ExchangeRate</summary>
    public HistoricalDataType DataType { get; private set; }

    /// <summary>股票代號或幣別對（如 VT、0050、USDTWD）</summary>
    public string Ticker { get; private set; } = string.Empty;

    /// <summary>年份（如 2024）</summary>
    public int Year { get; private set; }

    /// <summary>價格或匯率數值</summary>
    public decimal Value { get; private set; }

    /// <summary>價格的原始幣別（如 USD、TWD）</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>價格記錄的實際交易日期</summary>
    public DateTime ActualDate { get; private set; }

    /// <summary>資料來源：Stooq、TWSE 或 Manual</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>資料取得/輸入時間</summary>
    public DateTime FetchedAt { get; private set; }

    // EF Core 必要的無參數建構子
    private HistoricalYearEndData() { }

    public HistoricalYearEndData(
        HistoricalDataType dataType,
        string ticker,
        int year,
        decimal value,
        string currency,
        DateTime actualDate,
        string source)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker is required", nameof(ticker));
        if (year is < 2000 or > 2100)
            throw new ArgumentException("Year must be between 2000 and 2100", nameof(year));
        if (value <= 0)
            throw new ArgumentException("Value must be positive", nameof(value));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source is required", nameof(source));

        DataType = dataType;
        Ticker = ticker.Trim().ToUpperInvariant();
        Year = year;
        Value = Math.Round(value, 6);
        Currency = currency.Trim().ToUpperInvariant();
        ActualDate = actualDate;
        Source = source.Trim();
        FetchedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 建立股價快取項目
    /// </summary>
    public static HistoricalYearEndData CreateStockPrice(
        string ticker,
        int year,
        decimal price,
        string currency,
        DateTime actualDate,
        string source)
    {
        return new HistoricalYearEndData(
            HistoricalDataType.StockPrice,
            ticker,
            year,
            price,
            currency,
            actualDate,
            source);
    }

    /// <summary>
    /// 建立匯率快取項目
    /// </summary>
    public static HistoricalYearEndData CreateExchangeRate(
        string currencyPair,
        int year,
        decimal rate,
        DateTime actualDate,
        string source)
    {
        // 匯率以 TWD 為目標幣別儲存
        return new HistoricalYearEndData(
            HistoricalDataType.ExchangeRate,
            currencyPair,
            year,
            rate,
            "TWD",
            actualDate,
            source);
    }
}
