namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// Type of portfolio for different currency handling modes.
/// </summary>
public enum PortfolioType
{
    /// <summary>
    /// Primary portfolio - tracks costs in home currency (TWD) with exchange rate conversion.
    /// </summary>
    Primary = 0,

    /// <summary>
    /// Foreign currency portfolio - tracks all metrics in a single source currency without exchange rate conversion.
    /// Used for portfolios where all stocks are in the same foreign currency (e.g., USD).
    /// </summary>
    ForeignCurrency = 1
}
