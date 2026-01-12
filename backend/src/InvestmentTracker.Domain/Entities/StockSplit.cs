using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Records stock split events for automatic adjustment of historical transaction values.
/// </summary>
public class StockSplit : BaseEntity
{
    /// <summary>Stock/ETF symbol (e.g., "0050", "AAPL")</summary>
    public string Symbol { get; private set; } = string.Empty;

    /// <summary>Market where the stock is traded</summary>
    public StockMarket Market { get; private set; }

    /// <summary>Effective date of the split</summary>
    public DateTime SplitDate { get; private set; }

    /// <summary>Multiplier for shares (e.g., 4.0 for 1:4 split, 0.5 for 2:1 reverse)</summary>
    public decimal SplitRatio { get; private set; }

    /// <summary>Human-readable description (e.g., "1拆4")</summary>
    public string? Description { get; private set; }

    // Required by EF Core
    private StockSplit() { }

    public StockSplit(string symbol, StockMarket market, DateTime splitDate, decimal splitRatio, string? description = null)
    {
        SetSymbol(symbol);
        SetMarket(market);
        SetSplitDate(splitDate);
        SetSplitRatio(splitRatio);
        Description = description;
    }

    public void SetSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("Symbol is required", nameof(symbol));

        if (symbol.Length > 20)
            throw new ArgumentException("Symbol cannot exceed 20 characters", nameof(symbol));

        Symbol = symbol.Trim().ToUpperInvariant();
    }

    public void SetMarket(StockMarket market)
    {
        if (!Enum.IsDefined(typeof(StockMarket), market))
            throw new ArgumentException("Invalid market", nameof(market));

        Market = market;
    }

    public void SetSplitDate(DateTime splitDate)
    {
        if (splitDate == default)
            throw new ArgumentException("Split date is required", nameof(splitDate));

        // Ensure UTC kind for PostgreSQL compatibility
        SplitDate = DateTime.SpecifyKind(splitDate.Date, DateTimeKind.Utc);
    }

    public void SetSplitRatio(decimal splitRatio)
    {
        if (splitRatio <= 0)
            throw new ArgumentException("Split ratio must be greater than 0", nameof(splitRatio));

        SplitRatio = splitRatio;
    }

    public void SetDescription(string? description)
    {
        if (description != null && description.Length > 100)
            throw new ArgumentException("Description cannot exceed 100 characters", nameof(description));

        Description = description?.Trim();
    }

    public void Update(DateTime splitDate, decimal splitRatio, string? description)
    {
        SetSplitDate(splitDate);
        SetSplitRatio(splitRatio);
        SetDescription(description);
    }
}
