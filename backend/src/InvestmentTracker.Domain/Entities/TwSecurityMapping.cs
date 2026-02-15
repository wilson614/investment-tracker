namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 台股證券名稱與代號對照實體，供券商對帳單匯入時解析標的使用。
/// </summary>
public class TwSecurityMapping
{
    public const string SourceTwseIsin = "twse_isin";

    /// <summary>股票代碼（例如 2330）</summary>
    public string Ticker { get; private set; } = string.Empty;

    /// <summary>證券名稱（例如 台積電）</summary>
    public string SecurityName { get; private set; } = string.Empty;

    /// <summary>ISIN 識別碼（可空）</summary>
    public string? Isin { get; private set; }

    /// <summary>市場代碼（可空，例如 TWSE、TPEX）</summary>
    public string? Market { get; private set; }

    /// <summary>報價幣別（可空，例如 TWD）</summary>
    public string? Currency { get; private set; }

    /// <summary>資料來源（目前僅支援 twse_isin）</summary>
    public string Source { get; private set; } = SourceTwseIsin;

    /// <summary>最後同步時間（UTC）</summary>
    public DateTime LastSyncedAt { get; private set; }

    /// <summary>建立時間（UTC）</summary>
    public DateTime CreatedAt { get; private set; }

    /// <summary>更新時間（UTC）</summary>
    public DateTime UpdatedAt { get; private set; }

    // EF Core 需要的無參數建構子
    private TwSecurityMapping() { }

    public TwSecurityMapping(
        string ticker,
        string securityName,
        DateTime lastSyncedAt,
        string source = SourceTwseIsin,
        string? isin = null,
        string? market = null,
        string? currency = null)
    {
        Ticker = NormalizeRequiredUpper(ticker, nameof(ticker), "Ticker");
        SecurityName = NormalizeSecurityName(securityName, nameof(securityName));
        Isin = NormalizeOptionalUpper(isin);
        Market = NormalizeOptionalUpper(market);
        Currency = NormalizeOptionalUpper(currency);
        Source = NormalizeSource(source, nameof(source));
        LastSyncedAt = EnsureUtc(lastSyncedAt);

        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Update(
        string securityName,
        DateTime lastSyncedAt,
        string source = SourceTwseIsin,
        string? isin = null,
        string? market = null,
        string? currency = null)
    {
        SecurityName = NormalizeSecurityName(securityName, nameof(securityName));
        Isin = NormalizeOptionalUpper(isin);
        Market = NormalizeOptionalUpper(market);
        Currency = NormalizeOptionalUpper(currency);
        Source = NormalizeSource(source, nameof(source));
        LastSyncedAt = EnsureUtc(lastSyncedAt);
        UpdatedAt = DateTime.UtcNow;
    }

    private static string NormalizeRequiredUpper(string value, string paramName, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{fieldName} is required", paramName);

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeSecurityName(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Security name is required", paramName);

        return CollapseSpaces(value.Trim().Replace('\u3000', ' '));
    }

    private static string? NormalizeOptionalUpper(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim().ToUpperInvariant();
    }

    private static string NormalizeSource(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Source is required", paramName);

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized != SourceTwseIsin)
            throw new ArgumentException($"Unsupported source: {value}", paramName);

        return normalized;
    }

    private static string CollapseSpaces(string value)
    {
        while (value.Contains("  ", StringComparison.Ordinal))
        {
            value = value.Replace("  ", " ", StringComparison.Ordinal);
        }

        return value;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}
