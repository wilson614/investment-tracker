namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Result of fetching a year-end stock price.
/// </summary>
public class YearEndPriceResult
{
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime ActualDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool FromCache { get; set; }
}

/// <summary>
/// Result of fetching a year-end exchange rate.
/// </summary>
public class YearEndExchangeRateResult
{
    public decimal Rate { get; set; }
    public string CurrencyPair { get; set; } = string.Empty;
    public DateTime ActualDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool FromCache { get; set; }
}
