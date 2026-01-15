namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// Classification of ETF distribution type for YTD calculation.
/// </summary>
public enum EtfType
{
    /// <summary>Type not determined - treated as Accumulating for calculations</summary>
    Unknown = 0,

    /// <summary>Accumulating ETF - reinvests dividends, no adjustment needed</summary>
    Accumulating = 1,

    /// <summary>Distributing ETF - pays out dividends, may need adjustment</summary>
    Distributing = 2
}
