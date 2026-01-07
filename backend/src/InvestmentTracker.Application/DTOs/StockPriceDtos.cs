namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Supported stock markets
/// </summary>
public enum StockMarket
{
    /// <summary>Taiwan Stock Exchange</summary>
    TW = 1,
    /// <summary>US Stock Market (via Sina)</summary>
    US = 2,
    /// <summary>UK/London Stock Exchange (via Sina)</summary>
    UK = 3
}

/// <summary>
/// Request for fetching stock price
/// </summary>
public record StockPriceRequest
{
    public StockMarket Market { get; init; }
    public string Symbol { get; init; } = string.Empty;
}

/// <summary>
/// Stock quote response
/// </summary>
public record StockQuoteResponse
{
    public string Symbol { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public decimal? Change { get; init; }
    public string? ChangePercent { get; init; }
    public StockMarket Market { get; init; }
    public string Source { get; init; } = string.Empty;
    public DateTime FetchedAt { get; init; }
}
