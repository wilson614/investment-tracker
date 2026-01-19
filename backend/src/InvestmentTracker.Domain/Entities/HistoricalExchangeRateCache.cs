namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 歷史匯率快取實體，儲存交易日期的匯率資料
/// Key: (幣別對, 日期) - 例如 (USDTWD, 2025-01-15)
/// 一旦取得並儲存後，數值不會再更新（不可變）
/// </summary>
public class HistoricalExchangeRateCache
{
    public int Id { get; private set; }
    
    /// <summary>
    /// 幣別對格式為「來源目標」（如 USDTWD、EURTWD）
    /// </summary>
    public string CurrencyPair { get; private set; } = string.Empty;
    
    /// <summary>
    /// 請求的交易日期
    /// </summary>
    public DateTime RequestedDate { get; private set; }
    
    /// <summary>
    /// 匯率數值
    /// </summary>
    public decimal Rate { get; private set; }
    
    /// <summary>
    /// 實際取得匯率的交易日期（可能因週末/假日與 RequestedDate 不同）
    /// </summary>
    public DateTime ActualDate { get; private set; }
    
    /// <summary>
    /// 資料來源（如 Stooq、Manual）
    /// </summary>
    public string Source { get; private set; } = string.Empty;
    
    /// <summary>
    /// 快取建立時間
    /// </summary>
    public DateTime FetchedAt { get; private set; }

    private HistoricalExchangeRateCache() { }

    private HistoricalExchangeRateCache(
        string currencyPair,
        DateTime requestedDate,
        decimal rate,
        DateTime actualDate,
        string source)
    {
        CurrencyPair = currencyPair.ToUpperInvariant();
        RequestedDate = requestedDate.Date;
        Rate = rate;
        ActualDate = actualDate.Date;
        Source = source;
        FetchedAt = DateTime.UtcNow;
    }

    public static HistoricalExchangeRateCache Create(
        string fromCurrency,
        string toCurrency,
        DateTime requestedDate,
        decimal rate,
        DateTime actualDate,
        string source)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";
        return new HistoricalExchangeRateCache(currencyPair, requestedDate, rate, actualDate, source);
    }

    public static HistoricalExchangeRateCache CreateManual(
        string fromCurrency,
        string toCurrency,
        DateTime requestedDate,
        decimal rate)
    {
        return Create(fromCurrency, toCurrency, requestedDate, rate, requestedDate, "Manual");
    }
}
