using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using BankAccountEntity = InvestmentTracker.Domain.Entities.BankAccount;
using CurrencyLedgerEntity = InvestmentTracker.Domain.Entities.CurrencyLedger;

namespace InvestmentTracker.Application.UseCases.AvailableFunds;

/// <summary>
/// Get available funds summary for current user.
/// </summary>
public class GetAvailableFundsSummaryUseCase(
    ICurrencyLedgerRepository ledgerRepository,
    IBankAccountRepository bankAccountRepository,
    IInstallmentRepository installmentRepository,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    AvailableFundsService availableFundsService,
    CurrencyLedgerService currencyLedgerService,
    ICurrentUserService currentUserService)
{
    private const string BaseCurrency = "TWD";

    public async Task<AvailableFundsSummaryResponse> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var valuationDate = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var ledgersTask = ledgerRepository.GetByUserIdAsync(userId, cancellationToken);
        var bankAccountsTask = bankAccountRepository.GetByUserIdAsync(userId, cancellationToken);
        var installmentsTask = installmentRepository.GetAllByUserIdAsync(userId, cancellationToken);

        await Task.WhenAll(ledgersTask, bankAccountsTask, installmentsTask);

        var ledgers = await ledgersTask;
        var bankAccounts = await bankAccountsTask;
        var installments = await installmentsTask;
        var ledgerBalances = await GetLedgerBalancesAsync(ledgers, cancellationToken);

        var exchangeRatesToBaseCurrency = await GetExchangeRatesToBaseCurrencyAsync(
            bankAccounts.Select(account => account.Currency)
                .Concat(ledgerBalances.Select(ledger => ledger.Currency)),
            valuationDate,
            cancellationToken);

        var calculation = availableFundsService.Calculate(
            ledgerBalances,
            bankAccounts,
            installments,
            currency =>
            {
                var normalizedCurrency = NormalizeCurrency(currency);
                return exchangeRatesToBaseCurrency.TryGetValue(normalizedCurrency, out var exchangeRate)
                       && exchangeRate > 0m
                    ? exchangeRate
                    : 0m;
            });

        var utcToday = DateTime.UtcNow.Date;
        var fixedDepositSummaries = bankAccounts
            .Where(account => IsMaturedFixedDeposit(account, utcToday))
            .Select(account =>
            {
                var maturedAmount = GetMaturedFixedDepositAmount(account);
                return new FixedDepositSummary(
                    Id: account.Id,
                    BankName: account.BankName,
                    Principal: maturedAmount,
                    Currency: account.Currency,
                    PrincipalInBaseCurrency: ConvertToBaseCurrency(
                        maturedAmount,
                        account.Currency,
                        exchangeRatesToBaseCurrency));
            })
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

    private async Task<IReadOnlyList<LedgerBalance>> GetLedgerBalancesAsync(
        IReadOnlyList<CurrencyLedgerEntity> ledgers,
        CancellationToken cancellationToken)
    {
        var activeLedgers = ledgers
            .Where(ledger => ledger.IsActive)
            .ToList();

        if (activeLedgers.Count == 0)
            return [];

        var ledgersWithTransactions = await Task.WhenAll(activeLedgers
            .Select(ledger => ledgerRepository.GetByIdWithTransactionsAsync(ledger.Id, cancellationToken)));

        return ledgersWithTransactions
            .Where(ledger => ledger is not null)
            .Select(ledger => new LedgerBalance(
                Balance: currencyLedgerService.CalculateBalance(ledger!.Transactions),
                Currency: ledger.CurrencyCode))
            .ToList();
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

    private static bool IsMaturedFixedDeposit(BankAccountEntity account, DateTime utcToday)
    {
        if (account.AccountType != BankAccountType.FixedDeposit)
            return false;

        if (account.FixedDepositStatus is FixedDepositStatus.Closed or FixedDepositStatus.EarlyWithdrawal)
            return false;

        return account.FixedDepositStatus == FixedDepositStatus.Matured
            || (account.MaturityDate.HasValue && account.MaturityDate.Value.Date <= utcToday.Date);
    }

    private static decimal GetMaturedFixedDepositAmount(BankAccountEntity account)
    {
        var interest = account.ActualInterest ?? account.ExpectedInterest ?? 0m;
        return account.TotalAssets + interest;
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
