using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Services;

public record TotalAssetsSummary(
    decimal InvestmentTotal,
    decimal BankTotal,
    decimal GrandTotal,
    decimal InvestmentPercentage,
    decimal BankPercentage,
    decimal TotalMonthlyInterest,
    decimal TotalYearlyInterest);

public class TotalAssetsService(InterestEstimationService interestEstimationService)
{
    private const string HomeCurrency = "TWD";

    public TotalAssetsSummary Calculate(
        decimal investmentTotal,
        IReadOnlyList<BankAccount> bankAccounts,
        IReadOnlyDictionary<string, decimal>? exchangeRatesToTwd = null)
    {
        var rates = NormalizeExchangeRates(exchangeRatesToTwd);

        var bankTotal = bankAccounts.Sum(a => ConvertToTwd(a, rates));

        var totalMonthlyInterest = bankAccounts
            .Sum(a => interestEstimationService.Calculate(a).MonthlyInterest);

        var totalYearlyInterest = bankAccounts
            .Sum(a => interestEstimationService.Calculate(a).YearlyInterest);

        var grandTotal = investmentTotal + bankTotal;

        var investmentPercentage = grandTotal > 0
            ? investmentTotal / grandTotal * 100m
            : 0m;

        var bankPercentage = grandTotal > 0
            ? bankTotal / grandTotal * 100m
            : 0m;

        return new TotalAssetsSummary(
            InvestmentTotal: investmentTotal,
            BankTotal: bankTotal,
            GrandTotal: grandTotal,
            InvestmentPercentage: investmentPercentage,
            BankPercentage: bankPercentage,
            TotalMonthlyInterest: totalMonthlyInterest,
            TotalYearlyInterest: totalYearlyInterest);
    }

    private static Dictionary<string, decimal> NormalizeExchangeRates(
        IReadOnlyDictionary<string, decimal>? exchangeRatesToTwd)
    {
        if (exchangeRatesToTwd == null || exchangeRatesToTwd.Count == 0)
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        return exchangeRatesToTwd
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value > 0m)
            .ToDictionary(
                x => x.Key.Trim().ToUpperInvariant(),
                x => x.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static decimal ConvertToTwd(BankAccount account, IReadOnlyDictionary<string, decimal> rates)
    {
        var currency = NormalizeCurrency(account.Currency);

        if (string.Equals(currency, HomeCurrency, StringComparison.OrdinalIgnoreCase))
            return account.TotalAssets;

        if (rates.TryGetValue(currency, out var exchangeRate) && exchangeRate > 0m)
            return Math.Round(account.TotalAssets * exchangeRate, 4);

        // Missing exchange rate: treat as unconverted and exclude from TWD total.
        return 0m;
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency)
            ? HomeCurrency
            : currency.Trim().ToUpperInvariant();
    }
}
