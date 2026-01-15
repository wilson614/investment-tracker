namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Client interface for fetching quotes from Euronext exchange.
/// </summary>
public interface IEuronextApiClient
{
    /// <summary>
    /// Fetches a real-time quote for a Euronext-listed security.
    /// </summary>
    /// <param name="isin">The ISIN (International Securities Identification Number).</param>
    /// <param name="mic">The Market Identifier Code (e.g., XAMS for Amsterdam).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Quote result containing price and currency, or null if not found.</returns>
    Task<EuronextQuoteResult?> GetQuoteAsync(string isin, string mic, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a Euronext quote fetch operation.
/// </summary>
public record EuronextQuoteResult
{
    /// <summary>
    /// The current price of the security.
    /// </summary>
    public required decimal Price { get; init; }

    /// <summary>
    /// The currency code (e.g., "EUR", "USD").
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// The time of the last trade, if available.
    /// </summary>
    public DateTime? MarketTime { get; init; }

    /// <summary>
    /// The name of the security.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// The change percentage from previous close (e.g., "+1.25%", "-0.50%").
    /// </summary>
    public string? ChangePercent { get; init; }

    /// <summary>
    /// The absolute price change from previous close.
    /// </summary>
    public decimal? Change { get; init; }
}
