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
    /// <summary>Exchange rate to home currency (e.g., USD/TWD)</summary>
    public decimal? ExchangeRate { get; init; }
    /// <summary>Exchange rate pair description (e.g., "USD/TWD")</summary>
    public string? ExchangeRatePair { get; init; }
}

/// <summary>
/// Exchange rate response
/// </summary>
public record ExchangeRateResponse
{
    public string FromCurrency { get; init; } = string.Empty;
    public string ToCurrency { get; init; } = string.Empty;
    public decimal Rate { get; init; }
    public string Source { get; init; } = string.Empty;
    public DateTime FetchedAt { get; init; }
}

/// <summary>
/// YTD return for a single benchmark ETF
/// </summary>
public record MarketYtdReturnDto
{
    /// <summary>Market category (e.g., "All Country", "US Large", "Taiwan", "Emerging Markets")</summary>
    public string MarketKey { get; init; } = string.Empty;

    /// <summary>ETF ticker symbol (e.g., "VWRA", "VUAA", "0050", "VFEM")</summary>
    public string Symbol { get; init; } = string.Empty;

    /// <summary>Human-readable name</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Jan 1 reference price (previous year-end closing price)</summary>
    public decimal? Jan1Price { get; init; }

    /// <summary>Current price</summary>
    public decimal? CurrentPrice { get; init; }

    /// <summary>Total dividends paid YTD (for dividend-adjusted return)</summary>
    public decimal? DividendsPaid { get; init; }

    /// <summary>YTD return percentage (price return only, excluding dividends)</summary>
    public decimal? YtdReturnPercent { get; init; }

    /// <summary>YTD total return percentage (including dividends)</summary>
    public decimal? YtdTotalReturnPercent { get; init; }

    /// <summary>When current price was fetched</summary>
    public DateTime? FetchedAt { get; init; }

    /// <summary>Error message if price fetch failed</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Collection of YTD returns for all benchmarks
/// </summary>
public record MarketYtdComparisonDto
{
    /// <summary>Year for YTD calculation</summary>
    public int Year { get; init; }

    /// <summary>List of benchmark YTD returns</summary>
    public List<MarketYtdReturnDto> Benchmarks { get; init; } = [];

    /// <summary>When this data was generated</summary>
    public DateTime GeneratedAt { get; init; }
}
