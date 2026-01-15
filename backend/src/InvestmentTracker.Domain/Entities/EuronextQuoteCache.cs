namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Cache for Euronext real-time quotes with stale indicator for API failure handling.
/// </summary>
public class EuronextQuoteCache
{
    /// <summary>ISIN identifier (e.g., "IE000FHBZDZ8")</summary>
    public string Isin { get; private set; } = string.Empty;

    /// <summary>Market Identifier Code (e.g., "XAMS" for Amsterdam)</summary>
    public string Mic { get; private set; } = string.Empty;

    /// <summary>Last fetched price</summary>
    public decimal Price { get; private set; }

    /// <summary>Currency of the quote (e.g., "USD", "EUR")</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>When the quote was fetched from API</summary>
    public DateTime FetchedAt { get; private set; }

    /// <summary>Quote timestamp from the API (market time)</summary>
    public DateTime? MarketTime { get; private set; }

    /// <summary>True if the last fetch failed and data is stale</summary>
    public bool IsStale { get; private set; }

    /// <summary>Change percentage from previous close (e.g., "+1.25%")</summary>
    public string? ChangePercent { get; private set; }

    /// <summary>Absolute price change from previous close</summary>
    public decimal? Change { get; private set; }

    // Required by EF Core
    private EuronextQuoteCache() { }

    public EuronextQuoteCache(
        string isin,
        string mic,
        decimal price,
        string currency,
        DateTime? marketTime = null,
        string? changePercent = null,
        decimal? change = null)
    {
        if (string.IsNullOrWhiteSpace(isin))
            throw new ArgumentException("ISIN is required", nameof(isin));
        if (string.IsNullOrWhiteSpace(mic))
            throw new ArgumentException("MIC is required", nameof(mic));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        Isin = isin.Trim().ToUpperInvariant();
        Mic = mic.Trim().ToUpperInvariant();
        Price = Math.Round(price, 4);
        Currency = currency.Trim().ToUpperInvariant();
        FetchedAt = DateTime.UtcNow;
        MarketTime = marketTime;
        IsStale = false;
        ChangePercent = changePercent;
        Change = change;
    }

    public void UpdateQuote(decimal price, string currency, DateTime? marketTime = null, string? changePercent = null, decimal? change = null)
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        Price = Math.Round(price, 4);
        Currency = currency.Trim().ToUpperInvariant();
        FetchedAt = DateTime.UtcNow;
        MarketTime = marketTime;
        IsStale = false;
        ChangePercent = changePercent;
        Change = change;
    }

    public void MarkAsStale()
    {
        IsStale = true;
    }
}
