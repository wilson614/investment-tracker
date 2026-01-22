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

    /// <summary>
    /// 搜尋 Euronext 上市標的，取得 ticker 對應的 ISIN/MIC。
    /// </summary>
    /// <param name="query">搜尋字串（股票代碼或名稱）。</param>
    /// <param name="cancellationToken">Cancellation token。</param>
    /// <returns>搜尋結果列表；若查無資料則回傳空列表。</returns>
    Task<IReadOnlyList<EuronextSearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>
/// Euronext 搜尋結果。
/// </summary>
public record EuronextSearchResult
{
    /// <summary>股票代碼。</summary>
    public required string Ticker { get; init; }

    /// <summary>ISIN 識別碼。</summary>
    public required string Isin { get; init; }

    /// <summary>市場識別碼（MIC）。</summary>
    public required string Mic { get; init; }

    /// <summary>標的名稱。</summary>
    public string? Name { get; init; }

    /// <summary>幣別。</summary>
    public required string Currency { get; init; }
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
