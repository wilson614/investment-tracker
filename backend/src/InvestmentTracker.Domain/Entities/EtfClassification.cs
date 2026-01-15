using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Tracks ETF type classification for dividend adjustment in YTD calculations.
/// </summary>
public class EtfClassification
{
    /// <summary>Stock/ETF ticker symbol</summary>
    public string Symbol { get; private set; } = string.Empty;

    /// <summary>Market identifier (e.g., "TW", "US", "XAMS")</summary>
    public string Market { get; private set; } = string.Empty;

    /// <summary>ETF classification type</summary>
    public EtfType Type { get; private set; }

    /// <summary>When the classification was last updated</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>User who set the classification (null if system-determined)</summary>
    public Guid? UpdatedByUserId { get; private set; }

    // Required by EF Core
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
