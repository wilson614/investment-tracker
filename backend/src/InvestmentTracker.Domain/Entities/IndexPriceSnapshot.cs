namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 指數價格快照實體，儲存月底指數價格用於 CAPE 調整計算
/// 每月擷取一次作為參考價格
/// </summary>
public class IndexPriceSnapshot
{
    public int Id { get; set; }

    /// <summary>
    /// 市場識別碼（如 All Country、US Large、Taiwan）
    /// </summary>
    public string MarketKey { get; set; } = string.Empty;

    /// <summary>
    /// 年月格式為 YYYYMM（如 202512 代表 2025 年 12 月）
    /// </summary>
    public string YearMonth { get; set; } = string.Empty;

    /// <summary>
    /// 月底收盤價。當 IsNotAvailable 為 true 時為 null
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// 價格記錄時間
    /// </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>
    /// 表示價格永久無法取得（如 ETF 該年度尚未上市）
    /// 當為 true 時，Price 應為 null 且不應再進行 API 呼叫
    /// </summary>
    public bool IsNotAvailable { get; set; }
}
