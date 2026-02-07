using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Services;

public record AllocationBreakdown(
    string Purpose,
    string PurposeDisplayName,
    decimal Amount);

public record TotalAssetsSummary(
    decimal InvestmentTotal,
    decimal BankTotal,
    decimal GrandTotal,
    decimal InvestmentPercentage,
    decimal BankPercentage,
    decimal TotalMonthlyInterest,
    decimal TotalYearlyInterest,
    decimal TotalAllocated = 0m,
    decimal Unallocated = 0m,
    IReadOnlyList<AllocationBreakdown>? AllocationBreakdown = null);

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

        var interestEstimations = bankAccounts
            .Select(account => new
            {
                Account = account,
                Estimation = interestEstimationService.Calculate(account)
            })
            .ToList();

        var totalMonthlyInterest = interestEstimations
            .Sum(x => ConvertInterestToTwd(x.Account, x.Estimation.MonthlyInterest, rates));

        var totalYearlyInterest = interestEstimations
            .Sum(x => ConvertInterestToTwd(x.Account, x.Estimation.YearlyInterest, rates));

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
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                [HomeCurrency] = 1m
            };
        }

        var normalized = exchangeRatesToTwd
            .Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Value > 0m)
            .ToDictionary(
                x => x.Key.Trim().ToUpperInvariant(),
                x => x.Value,
                StringComparer.OrdinalIgnoreCase);

        normalized[HomeCurrency] = 1m;

        return normalized;
    }

    private static decimal ConvertToTwd(BankAccount account, IReadOnlyDictionary<string, decimal> rates)
        => ConvertAmountToTwd(account.Currency, account.TotalAssets, rates);

    private static decimal ConvertInterestToTwd(
        BankAccount account,
        decimal interest,
        IReadOnlyDictionary<string, decimal> rates)
        => ConvertAmountToTwd(account.Currency, interest, rates);

    private static decimal ConvertAmountToTwd(
        string? currency,
        decimal amount,
        IReadOnlyDictionary<string, decimal> rates)
    {
        var normalizedCurrency = NormalizeCurrency(currency);

        if (string.Equals(normalizedCurrency, HomeCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        if (rates.TryGetValue(normalizedCurrency, out var exchangeRate) && exchangeRate > 0m)
            return Math.Round(amount * exchangeRate, 4);

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
