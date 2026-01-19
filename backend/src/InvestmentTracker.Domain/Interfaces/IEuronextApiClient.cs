namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// 從 Euronext 交易所抓取報價的 Client 介面。
/// </summary>
public interface IEuronextApiClient
{
    /// <summary>
    /// 取得 Euronext 上市標的的即時報價。
    /// </summary>
    /// <param name="isin">ISIN（International Securities Identification Number）。</param>
    /// <param name="mic">MIC（Market Identifier Code，例如：Amsterdam 的 XAMS）。</param>
    /// <param name="cancellationToken">Cancellation token。</param>
    /// <returns>包含價格與幣別的報價結果；若查無資料則回傳 null。</returns>
    Task<EuronextQuoteResult?> GetQuoteAsync(string isin, string mic, CancellationToken cancellationToken = default);
}

/// <summary>
/// Euronext 報價查詢結果。
/// </summary>
public record EuronextQuoteResult
{
    /// <summary>
    /// 標的目前價格。
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// 幣別代碼（例如："EUR"、"USD"）。
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// 最新成交時間（若有提供）。
    /// </summary>
    public DateTime? MarketTime { get; init; }

    /// <summary>
    /// 標的名稱。
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// 與前一日收盤相比的漲跌幅（例如："+1.25%"、"-0.50%"）。
    /// </summary>
    public string? ChangePercent { get; init; }

    /// <summary>
    /// 與前一日收盤相比的價格變動（絕對值）。
    /// </summary>
    public decimal? Change { get; init; }
}
