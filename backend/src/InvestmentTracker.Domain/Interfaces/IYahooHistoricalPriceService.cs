namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// 從 Yahoo Finance 取得歷史價格的服務介面。
/// 用於 Euronext 等 Stooq 不支援的市場。
/// </summary>
public interface IYahooHistoricalPriceService
{
    /// <summary>
    /// 取得指定日期的股票收盤價。
    /// </summary>
    /// <param name="symbol">Yahoo Finance 股票代號（如 AGAC.AS）</param>
    /// <param name="date">目標日期</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>價格結果；若查無資料則回傳 null。</returns>
    Task<YahooHistoricalPriceResult?> GetHistoricalPriceAsync(
        string symbol,
        DateOnly date,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Yahoo Finance 歷史價格查詢結果。
/// </summary>
public record YahooHistoricalPriceResult
{
    /// <summary>
    /// 收盤價。
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// 實際交易日期（可能與請求日期不同，例如遇到假日）。
    /// </summary>
    public required DateOnly ActualDate { get; init; }

    /// <summary>
    /// 幣別代碼（如 USD、EUR）。
    /// </summary>
    public required string Currency { get; init; }
}
