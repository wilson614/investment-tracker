namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Stores end-of-month index prices for CAPE adjustment calculation
/// These are captured monthly and used as reference prices
/// </summary>
public class IndexPriceSnapshot
{
    public int Id { get; set; }

    /// <summary>
    /// Market key (e.g., "All Country", "US Large", "Taiwan")
    /// </summary>
    public string MarketKey { get; set; } = string.Empty;

    /// <summary>
    /// Year and month in YYYYMM format (e.g., "202512" for December 2025)
    /// </summary>
    public string YearMonth { get; set; } = string.Empty;

    /// <summary>
    /// End of month closing price. Null when IsNotAvailable is true.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// When this price was recorded
    /// </summary>
    public DateTime RecordedAt { get; set; }

    /// <summary>
    /// Indicates the price is permanently unavailable (e.g., ETF not listed in that year).
    /// When true, Price should be null and no further API calls should be made.
    /// </summary>
    public bool IsNotAvailable { get; set; }
}
