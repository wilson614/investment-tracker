using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// ETF 分類實體，用於 YTD 計算中的股息調整
/// </summary>
public class EtfClassification
{
    /// <summary>股票/ETF 代號</summary>
    public string Symbol { get; private set; } = string.Empty;

    /// <summary>市場識別碼（如 TW、US、XAMS）</summary>
    public string Market { get; private set; } = string.Empty;

    /// <summary>ETF 分類類型</summary>
    public EtfType Type { get; private set; }

    /// <summary>分類最後更新時間</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>設定分類的使用者（若為系統判定則為 null）</summary>
    public Guid? UpdatedByUserId { get; private set; }

    // EF Core 必要的無參數建構子
    private EtfClassification() { }

    public EtfClassification(
        string symbol,
        string market,
        EtfType type = EtfType.Unknown,
        Guid? updatedByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));
        if (string.IsNullOrWhiteSpace(market))
            throw new ArgumentException("Market is required", nameof(market));

        Symbol = symbol.Trim().ToUpperInvariant();
        Market = market.Trim().ToUpperInvariant();
        Type = type;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = updatedByUserId;
    }

    public void SetType(EtfType type, Guid? userId = null)
    {
        Type = type;
        UpdatedAt = DateTime.UtcNow;
        UpdatedByUserId = userId;
    }
}
