namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Response for historical year performance calculation.
/// </summary>
public record YearPerformanceDto
{
    /// <summary>The year for this performance calculation.</summary>
    public int Year { get; init; }

    /// <summary>XIRR for the year (annualized return rate).</summary>
    public double? Xirr { get; init; }

    /// <summary>XIRR as percentage (e.g., 12.5 for 12.5%).</summary>
    public double? XirrPercentage { get; init; }

    /// <summary>Total return percentage for the year.</summary>
    public double? TotalReturnPercentage { get; init; }

    /// <summary>Portfolio value at year start (home currency).</summary>
    public decimal? StartValueHome { get; init; }

    /// <summary>Portfolio value at year end (home currency).</summary>
    public decimal? EndValueHome { get; init; }

    /// <summary>Net contributions during the year (home currency).</summary>
    public decimal NetContributionsHome { get; init; }

    /// <summary>Number of cash flows used in XIRR calculation.</summary>
    public int CashFlowCount { get; init; }

    /// <summary>Positions with missing reference prices needed for calculation.</summary>
    public IReadOnlyList<MissingPriceDto> MissingPrices { get; init; } = [];

    /// <summary>Whether this performance can be fully calculated (no missing prices).</summary>
    public bool IsComplete => MissingPrices.Count == 0;
}

/// <summary>
/// Position with missing price for historical calculation.
/// </summary>
public record MissingPriceDto
{
    /// <summary>Stock ticker symbol.</summary>
    public string Ticker { get; init; } = string.Empty;

    /// <summary>Date for which price is needed.</summary>
    public DateTime Date { get; init; }

    /// <summary>Whether this is a year-start reference price (typically prior-year Dec) or year-end/as-of price.</summary>
    public string PriceType { get; init; } = "YearEnd";
}

/// <summary>
/// Response for available performance years.
/// </summary>
public record AvailableYearsDto
{
    /// <summary>List of years with transaction data.</summary>
    public IReadOnlyList<int> Years { get; init; } = [];

    /// <summary>Earliest year with transactions.</summary>
    public int? EarliestYear { get; init; }

    /// <summary>Current year.</summary>
    public int CurrentYear { get; init; } = DateTime.UtcNow.Year;
}

/// <summary>
/// Request for calculating year performance.
/// </summary>
public record CalculateYearPerformanceRequest
{
    /// <summary>The year to calculate performance for.</summary>
    public int Year { get; init; }

    /// <summary>Reference prices for positions (keyed by ticker) used for calculation.</summary>
    public Dictionary<string, YearEndPriceInfo>? YearEndPrices { get; init; }
}

/// <summary>
/// Year-end price information for a position.
/// </summary>
public record YearEndPriceInfo
{
    /// <summary>Price at the requested reference date in source currency.</summary>
    public decimal Price { get; init; }

    /// <summary>Exchange rate at the reference date (source to home currency).</summary>
    public decimal ExchangeRate { get; init; }
}
