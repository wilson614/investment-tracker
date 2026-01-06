namespace InvestmentTracker.Domain.ValueObjects;

/// <summary>
/// Represents a monetary value with currency code.
/// Immutable value object for financial calculations.
/// </summary>
public sealed record Money
{
    public decimal Amount { get; }
    public string CurrencyCode { get; }

    public Money(decimal amount, string currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode))
            throw new ArgumentException("Currency code is required", nameof(currencyCode));

        if (currencyCode.Length != 3)
            throw new ArgumentException("Currency code must be 3 characters (ISO 4217)", nameof(currencyCode));

        Amount = amount;
        CurrencyCode = currencyCode.ToUpperInvariant();
    }

    /// <summary>
    /// Converts this money to another currency using the specified exchange rate.
    /// </summary>
    public Money ConvertTo(string targetCurrency, decimal rate)
    {
        if (rate <= 0)
            throw new ArgumentException("Exchange rate must be positive", nameof(rate));

        return new Money(Amount * rate, targetCurrency);
    }

    public static Money Zero(string currencyCode) => new(0m, currencyCode);

    public static Money operator +(Money a, Money b)
    {
        if (a.CurrencyCode != b.CurrencyCode)
            throw new InvalidOperationException($"Cannot add {a.CurrencyCode} and {b.CurrencyCode}");
        return new Money(a.Amount + b.Amount, a.CurrencyCode);
    }

    public static Money operator -(Money a, Money b)
    {
        if (a.CurrencyCode != b.CurrencyCode)
            throw new InvalidOperationException($"Cannot subtract {a.CurrencyCode} and {b.CurrencyCode}");
        return new Money(a.Amount - b.Amount, a.CurrencyCode);
    }

    public static Money operator *(Money money, decimal multiplier)
        => new(money.Amount * multiplier, money.CurrencyCode);

    public override string ToString() => $"{Amount:N2} {CurrencyCode}";
}
