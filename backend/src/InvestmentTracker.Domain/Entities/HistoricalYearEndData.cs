using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Global cache for historical year-end stock prices and exchange rates.
/// Used to avoid repeated API calls to Stooq/TWSE and prevent rate limit issues.
/// </summary>
public class HistoricalYearEndData
{
    /// <summary>Auto-increment primary key</summary>
    public int Id { get; private set; }

    /// <summary>Type of data: StockPrice or ExchangeRate</summary>
    public HistoricalDataType DataType { get; private set; }

    /// <summary>Stock ticker or currency pair (e.g., "VT", "0050", "USDTWD")</summary>
    public string Ticker { get; private set; } = string.Empty;

    /// <summary>The year (e.g., 2024)</summary>
    public int Year { get; private set; }

    /// <summary>Price or exchange rate value</summary>
    public decimal Value { get; private set; }

    /// <summary>Original currency of the price (e.g., "USD", "TWD")</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Actual trading date the price was recorded</summary>
    public DateTime ActualDate { get; private set; }

    /// <summary>Data source: "Stooq", "TWSE", or "Manual"</summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary>Timestamp when data was fetched/entered</summary>
    public DateTime FetchedAt { get; private set; }

    // Required by EF Core
    private HistoricalYearEndData() { }

    public HistoricalYearEndData(
        HistoricalDataType dataType,
        string ticker,
        int year,
        decimal value,
        string currency,
        DateTime actualDate,
        string source)
    {
        if (string.IsNullOrWhiteSpace(ticker))
            throw new ArgumentException("Ticker is required", nameof(ticker));
        if (year < 2000 || year > 2100)
            throw new ArgumentException("Year must be between 2000 and 2100", nameof(year));
        if (value <= 0)
            throw new ArgumentException("Value must be positive", nameof(value));
        if (string.IsNullOrWhiteSpace(currency))
            throw new ArgumentException("Currency is required", nameof(currency));
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("Source is required", nameof(source));

        DataType = dataType;
        Ticker = ticker.Trim().ToUpperInvariant();
        Year = year;
        Value = Math.Round(value, 6);
        Currency = currency.Trim().ToUpperInvariant();
        ActualDate = actualDate;
        Source = source.Trim();
        FetchedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates a stock price cache entry.
    /// </summary>
    public static HistoricalYearEndData CreateStockPrice(
        string ticker,
        int year,
        decimal price,
        string currency,
        DateTime actualDate,
        string source)
    {
        return new HistoricalYearEndData(
            HistoricalDataType.StockPrice,
            ticker,
            year,
            price,
            currency,
            actualDate,
            source);
    }

    /// <summary>
    /// Creates an exchange rate cache entry.
    /// </summary>
    public static HistoricalYearEndData CreateExchangeRate(
        string currencyPair,
        int year,
        decimal rate,
        DateTime actualDate,
        string source)
    {
        // Exchange rates are stored with TWD as the target currency
        return new HistoricalYearEndData(
            HistoricalDataType.ExchangeRate,
            currencyPair,
            year,
            rate,
            "TWD",
            actualDate,
            source);
    }
}
