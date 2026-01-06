namespace InvestmentTracker.Domain.ValueObjects;

/// <summary>
/// Represents an exchange rate between two currencies at a specific point in time.
/// Immutable value object.
/// </summary>
public sealed record ExchangeRate
{
    public string FromCurrency { get; }
    public string ToCurrency { get; }
    public decimal Rate { get; }
    public DateTime AsOf { get; }

    public ExchangeRate(string fromCurrency, string toCurrency, decimal rate, DateTime? asOf = null)
    {
        if (string.IsNullOrWhiteSpace(fromCurrency))
            throw new ArgumentException("From currency is required", nameof(fromCurrency));

        if (string.IsNullOrWhiteSpace(toCurrency))
            throw new ArgumentException("To currency is required", nameof(toCurrency));

        if (rate <= 0)
            throw new ArgumentException("Exchange rate must be positive", nameof(rate));

        FromCurrency = fromCurrency.ToUpperInvariant();
        ToCurrency = toCurrency.ToUpperInvariant();
        Rate = rate;
        AsOf = asOf ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Gets the inverse exchange rate (swap from/to currencies).
    /// </summary>
    public ExchangeRate Inverse()
        => new(ToCurrency, FromCurrency, 1m / Rate, AsOf);

    /// <summary>
    /// Converts an amount from the source currency to the target currency.
    /// </summary>
    public decimal Convert(decimal amount) => amount * Rate;

    public override string ToString() => $"1 {FromCurrency} = {Rate:N6} {ToCurrency}";
}
