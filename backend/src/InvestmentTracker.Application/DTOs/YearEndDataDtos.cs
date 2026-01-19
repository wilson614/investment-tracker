namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// 年末股價抓取結果。
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
/// 年末匯率抓取結果。
/// </summary>
public class YearEndExchangeRateResult
{
    public decimal Rate { get; set; }
    public string CurrencyPair { get; set; } = string.Empty;
    public DateTime ActualDate { get; set; }
    public string Source { get; set; } = string.Empty;
    public bool FromCache { get; set; }
}
