namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// CAPE 資料快照實體，儲存來自 Research Affiliates 的 CAPE（景氣循環調整本益比）資料
/// 此為全域資料，非使用者專屬
/// </summary>
public class CapeDataSnapshot
{
    public int Id { get; set; }

    /// <summary>
    /// 資料來源日期（如 2026-01-02）
    /// </summary>
    public required string DataDate { get; set; }

    /// <summary>
    /// JSON 序列化的 CAPE 項目清單
    /// </summary>
    public required string ItemsJson { get; set; }

    /// <summary>
    /// 從 API 取得資料的時間
    /// </summary>
    public DateTime FetchedAt { get; set; }

    /// <summary>
    /// 記錄建立時間
    /// </summary>
    public DateTime CreatedAt { get; set; }
}
