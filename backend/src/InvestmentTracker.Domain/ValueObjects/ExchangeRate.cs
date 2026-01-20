namespace InvestmentTracker.Domain.ValueObjects;

/// <summary>
/// 表示兩種幣別之間在特定時間點的匯率。
/// 這是一個不可變（immutable）的 Value Object。
/// </summary>
public sealed record ExchangeRate(string FromCurrency, string ToCurrency, decimal Rate, DateTime AsOf)
{
    public ExchangeRate(string fromCurrency, string toCurrency, decimal rate, DateTime? asOf = null)
        : this(
            FromCurrency: NormalizeCurrencyOrThrow(fromCurrency, nameof(fromCurrency)),
            ToCurrency: NormalizeCurrencyOrThrow(toCurrency, nameof(toCurrency)),
            Rate: ValidateRateOrThrow(rate, nameof(rate)),
            AsOf: asOf ?? DateTime.UtcNow)
    {
    }

    private static string NormalizeCurrencyOrThrow(string currency, string paramName)
    {
        return string.IsNullOrWhiteSpace(currency) ? throw new ArgumentException("Currency is required", paramName) : currency.ToUpperInvariant();
    }

    private static decimal ValidateRateOrThrow(decimal rate, string paramName)
    {
        if (rate <= 0)
            throw new ArgumentException("Exchange rate must be positive", paramName);

        return rate;
    }


    /// <summary>
    /// 取得反向匯率（交換 From/To 幣別）。
    /// </summary>
    public ExchangeRate Inverse()
        => new(ToCurrency, FromCurrency, 1m / Rate, AsOf);

    /// <summary>
    /// 將金額從來源幣別換算到目標幣別。
    /// </summary>
    public decimal Convert(decimal amount) => amount * Rate;

    public override string ToString() => $"1 {FromCurrency} = {Rate:N6} {ToCurrency}";
}
