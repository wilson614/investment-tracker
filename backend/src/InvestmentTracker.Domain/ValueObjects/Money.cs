namespace InvestmentTracker.Domain.ValueObjects;

/// <summary>
/// 表示一筆帶有幣別代碼的金額。
/// 這是一個不可變（immutable）的 Value Object，用於金融計算。
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
    /// 使用指定匯率將此金額換算成另一種幣別。
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
