namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Euronext 報價快取實體，包含過期指示器以處理 API 失敗情況
/// </summary>
public class EuronextQuoteCache
{
    /// <summary>ISIN 識別碼（如 IE000FHBZDZ8）</summary>
    public string Isin { get; private set; } = string.Empty;

    /// <summary>市場識別碼（如 XAMS 代表阿姆斯特丹）</summary>
    public string Mic { get; private set; } = string.Empty;

    /// <summary>最新取得的價格</summary>
    public decimal Price { get; private set; }

    /// <summary>報價幣別（如 USD、EUR）</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>從 API 取得報價的時間</summary>
    public DateTime FetchedAt { get; private set; }

    /// <summary>API 回傳的報價時間戳（市場時間）</summary>
    public DateTime? MarketTime { get; private set; }

    /// <summary>若最後一次取得失敗且資料已過期則為 true</summary>
    public bool IsStale { get; private set; }

    /// <summary>相較前一收盤的變動百分比（如 +1.25%）</summary>
    public string? ChangePercent { get; private set; }

    /// <summary>相較前一收盤的價格變動</summary>
    public decimal? Change { get; private set; }

    // EF Core 必要的無參數建構子
    private EuronextQuoteCache() { }

    public EuronextQuoteCache(
        string isin,
        string mic,
        decimal price,
        string currency,
        DateTime? marketTime = null,
        string? changePercent = null,
        decimal? change = null)
    {
        if (string.IsNullOrWhiteSpace(isin))
            throw new ArgumentException("ISIN is required", nameof(isin));
        if (string.IsNullOrWhiteSpace(mic))
            throw new ArgumentException("MIC is required", nameof(mic));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        Isin = isin.Trim().ToUpperInvariant();
        Mic = mic.Trim().ToUpperInvariant();
        Price = Math.Round(price, 4);
        Currency = currency.Trim().ToUpperInvariant();
        FetchedAt = DateTime.UtcNow;
        MarketTime = marketTime;
        IsStale = false;
        ChangePercent = changePercent;
        Change = change;
    }

    public void UpdateQuote(decimal price, string currency, DateTime? marketTime = null, string? changePercent = null, decimal? change = null)
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        Price = Math.Round(price, 4);
        Currency = currency.Trim().ToUpperInvariant();
        FetchedAt = DateTime.UtcNow;
        MarketTime = marketTime;
        IsStale = false;
        ChangePercent = changePercent;
        Change = change;
    }

    public void MarkAsStale()
    {
        IsStale = true;
    }
}
