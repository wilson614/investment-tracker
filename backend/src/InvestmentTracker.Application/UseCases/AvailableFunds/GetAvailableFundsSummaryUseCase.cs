using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.AvailableFunds;

/// <summary>
/// Get available funds summary for current user.
/// </summary>
public class GetAvailableFundsSummaryUseCase(
    IBankAccountRepository bankAccountRepository,
    IFixedDepositRepository fixedDepositRepository,
    IInstallmentRepository installmentRepository,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    AvailableFundsService availableFundsService,
    ICurrentUserService currentUserService)
{
    private const string BaseCurrency = "TWD";

    public async Task<AvailableFundsSummaryResponse> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var valuationDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var bankAccountsTask = bankAccountRepository.GetByUserIdAsync(userId, cancellationToken);
        var fixedDepositsTask = fixedDepositRepository.GetAllByUserIdAsync(userId, cancellationToken);
        var installmentsTask = installmentRepository.GetAllByUserIdAsync(userId, cancellationToken);

        await Task.WhenAll(bankAccountsTask, fixedDepositsTask, installmentsTask);

        var bankAccounts = await bankAccountsTask;
        var fixedDeposits = await fixedDepositsTask;
        var installments = await installmentsTask;

        var exchangeRatesToBaseCurrency = await GetExchangeRatesToBaseCurrencyAsync(
            bankAccounts.Select(account => account.Currency)
                .Concat(fixedDeposits.Select(deposit => deposit.Currency)),
            valuationDate,
            cancellationToken);

        var calculation = availableFundsService.Calculate(
            bankAccounts,
            fixedDeposits,
            installments,
            currency =>
            {
                var normalizedCurrency = NormalizeCurrency(currency);
                return exchangeRatesToBaseCurrency.TryGetValue(normalizedCurrency, out var exchangeRate)
                       && exchangeRate > 0m
                    ? exchangeRate
                    : 0m;
            });

        var fixedDepositSummaries = fixedDeposits
            .Where(deposit => deposit.Status == FixedDepositStatus.Active)
            .Select(deposit => new FixedDepositSummary(
                Id: deposit.Id,
                BankName: deposit.BankAccount.BankName,
                Principal: deposit.Principal,
                Currency: deposit.Currency,
                PrincipalInBaseCurrency: ConvertToBaseCurrency(
                    deposit.Principal,
                    deposit.Currency,
                    exchangeRatesToBaseCurrency)))
            .ToList();

        var installmentSummaries = installments
            .Where(installment => installment.Status == InstallmentStatus.Active)
            .Select(installment => new InstallmentSummary(
                Id: installment.Id,
                Description: installment.Description,
                CreditCardName: installment.CreditCard.CardName,
                UnpaidBalance: Math.Round(installment.MonthlyPayment * installment.RemainingInstallments, 2)))
            .ToList();

        var committedFunds = calculation.FixedDepositsPrincipal + calculation.UnpaidInstallmentBalance;

        return new AvailableFundsSummaryResponse(
            TotalBankAssets: calculation.TotalBankAssets,
            AvailableFunds: calculation.AvailableFunds,
            CommittedFunds: committedFunds,
            Breakdown: new CommittedFundsBreakdown(
                FixedDepositsPrincipal: calculation.FixedDepositsPrincipal,
                UnpaidInstallmentBalance: calculation.UnpaidInstallmentBalance,
                FixedDeposits: fixedDepositSummaries,
                Installments: installmentSummaries),
            Currency: BaseCurrency);
    }

    private async Task<IReadOnlyDictionary<string, decimal>> GetExchangeRatesToBaseCurrencyAsync(
        IEnumerable<string?> currencies,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        var normalizedCurrencies = currencies
            .Where(currency => !string.IsNullOrWhiteSpace(currency))
            .Select(currency => currency!.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
        {
            [BaseCurrency] = 1m
        };

        foreach (var currency in normalizedCurrencies)
        {
            if (string.Equals(currency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
                continue;

            var fx = await yahooHistoricalPriceService.GetExchangeRateAsync(
                fromCurrency: currency,
                toCurrency: BaseCurrency,
                date: date,
                cancellationToken: cancellationToken);

            if (fx is { Rate: > 0m })
            {
                rates[currency] = fx.Rate;
            }
        }

        return rates;
    }

    private static decimal ConvertToBaseCurrency(
        decimal amount,
        string? currency,
        IReadOnlyDictionary<string, decimal> exchangeRatesToBaseCurrency)
    {
        var normalizedCurrency = NormalizeCurrency(currency);

        if (string.Equals(normalizedCurrency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        if (exchangeRatesToBaseCurrency.TryGetValue(normalizedCurrency, out var exchangeRate)
            && exchangeRate > 0m)
        {
            return Math.Round(amount * exchangeRate, 2);
        }

        return 0m;
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency)
            ? BaseCurrency
            : currency.Trim().ToUpperInvariant();
    }
}
