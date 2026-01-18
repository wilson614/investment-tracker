namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Global immutable cache for historical exchange rates by transaction date.
/// Key: (CurrencyPair, Date) - e.g., ("USDTWD", 2025-01-15)
/// Once fetched and persisted, values are never updated.
/// </summary>
public class HistoricalExchangeRateCache
{
    public int Id { get; private set; }
    
    /// <summary>
    /// Currency pair in format "FROMTO" (e.g., "USDTWD", "EURTWD")
    /// </summary>
    public string CurrencyPair { get; private set; } = string.Empty;
    
    /// <summary>
    /// The requested transaction date
    /// </summary>
    public DateTime RequestedDate { get; private set; }
    
    /// <summary>
    /// The exchange rate value
    /// </summary>
    public decimal Rate { get; private set; }
    
    /// <summary>
    /// The actual trading date the rate was retrieved for (may differ from RequestedDate due to weekends/holidays)
    /// </summary>
    public DateTime ActualDate { get; private set; }
    
    /// <summary>
    /// Source of the data (e.g., "Stooq", "Manual")
    /// </summary>
    public string Source { get; private set; } = string.Empty;
    
    /// <summary>
    /// When this cache entry was created
    /// </summary>
    public DateTime FetchedAt { get; private set; }

    private HistoricalExchangeRateCache() { }

    private HistoricalExchangeRateCache(
        string currencyPair,
        DateTime requestedDate,
        decimal rate,
        DateTime actualDate,
        string source)
    {
        CurrencyPair = currencyPair.ToUpperInvariant();
        RequestedDate = requestedDate.Date;
        Rate = rate;
        ActualDate = actualDate.Date;
        Source = source;
        FetchedAt = DateTime.UtcNow;
    }

    public static HistoricalExchangeRateCache Create(
        string fromCurrency,
        string toCurrency,
        DateTime requestedDate,
        decimal rate,
        DateTime actualDate,
        string source)
    {
        var currencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}";
        return new HistoricalExchangeRateCache(currencyPair, requestedDate, rate, actualDate, source);
    }

    public static HistoricalExchangeRateCache CreateManual(
        string fromCurrency,
        string toCurrency,
        DateTime requestedDate,
        decimal rate)
    {
        return Create(fromCurrency, toCurrency, requestedDate, rate, requestedDate, "Manual");
    }
}
