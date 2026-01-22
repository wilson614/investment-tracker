namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Euronext 股票代碼對應實體，儲存 ticker → ISIN/MIC 的對應關係。
/// 用於自動查詢並快取 Euronext 交易所的股票識別碼。
/// </summary>
public class EuronextSymbolMapping
{
    /// <summary>股票代碼（如 AGAC、SSAC）</summary>
    public string Ticker { get; private set; } = string.Empty;

    /// <summary>ISIN 識別碼（如 IE000FHBZDZ8）</summary>
    public string Isin { get; private set; } = string.Empty;

    /// <summary>市場識別碼（如 XAMS 代表阿姆斯特丹、XAMC 代表阿姆斯特丹貨幣區）</summary>
    public string Mic { get; private set; } = string.Empty;

    /// <summary>股票名稱（如 iShares Core Global Aggregate Bond UCITS ETF）</summary>
    public string? Name { get; private set; }

    /// <summary>報價幣別（如 USD、EUR）</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>資料建立時間</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>資料更新時間</summary>
    public DateTime UpdatedAt { get; private set; }

    // EF Core 必要的無參數建構子
    private EuronextSymbolMapping() { }

    public EuronextSymbolMapping(
        string ticker,
        string isin,
        string mic,
        string currency,
        string? name = null)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker is required", nameof(ticker));
        if (string.IsNullOrWhiteSpace(isin))
            throw new ArgumentException("ISIN is required", nameof(isin));
        if (string.IsNullOrWhiteSpace(mic))
            throw new ArgumentException("MIC is required", nameof(mic));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));

        Ticker = ticker.Trim().ToUpperInvariant();
        Isin = isin.Trim().ToUpperInvariant();
        Mic = mic.Trim().ToUpperInvariant();
        Currency = currency.Trim().ToUpperInvariant();
        Name = name?.Trim();
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(string isin, string mic, string currency, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(isin))
            throw new ArgumentException("ISIN is required", nameof(isin));
        if (string.IsNullOrWhiteSpace(mic))
            throw new ArgumentException("MIC is required", nameof(mic));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));

        Isin = isin.Trim().ToUpperInvariant();
        Mic = mic.Trim().ToUpperInvariant();
        Currency = currency.Trim().ToUpperInvariant();
        Name = name?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
