using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.FundAllocation;

internal static class FundAllocationBankAssetsCalculator
{
    private const string HomeCurrency = "TWD";

    internal static async Task<decimal> CalculateTotalBankAssetsInTwdAsync(
        Guid userId,
        IBankAccountRepository bankAccountRepository,
        IYahooHistoricalPriceService yahooHistoricalPriceService,
        TotalAssetsService totalAssetsService,
        CancellationToken cancellationToken)
    {
        var bankAccounts = await bankAccountRepository.GetByUserIdAsync(userId, cancellationToken);
        if (bankAccounts.Count == 0)
            return 0m;

        var rates = await GetBankExchangeRatesToTwdAsync(
            bankAccounts,
            yahooHistoricalPriceService,
            DateOnly.FromDateTime(DateTime.UtcNow.Date),
            cancellationToken);

        var summary = totalAssetsService.Calculate(
            investmentTotal: 0m,
            bankAccounts: bankAccounts,
            exchangeRatesToTwd: rates);

        return summary.BankTotal;
    }

    private static async Task<IReadOnlyDictionary<string, decimal>> GetBankExchangeRatesToTwdAsync(
        IReadOnlyList<global::InvestmentTracker.Domain.Entities.BankAccount> bankAccounts,
        IYahooHistoricalPriceService yahooHistoricalPriceService,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var currencies = bankAccounts
            .Select(a => a.Currency)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [HomeCurrency] = 1m
        };

        foreach (var currency in currencies)
        {
            if (string.Equals(currency, HomeCurrency, StringComparison.OrdinalIgnoreCase))
                continue;

            var fx = await yahooHistoricalPriceService.GetExchangeRateAsync(
                fromCurrency: currency,
                toCurrency: HomeCurrency,
                date: date,
                cancellationToken: cancellationToken);

            if (fx is { Rate: > 0m })
            {
                rates[currency] = fx.Rate;
            }
        }

        return rates;
    }
}
