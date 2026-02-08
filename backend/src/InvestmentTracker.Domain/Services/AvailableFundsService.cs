using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

public record LedgerBalance(
    decimal Balance,
    string Currency);

public record AvailableFundsCalculation(
    decimal TotalBankAssets,
    decimal FixedDepositsPrincipal,
    decimal UnpaidInstallmentBalance,
    decimal AvailableFunds);

public class AvailableFundsService
{
    private const string BaseCurrency = "TWD";

    public AvailableFundsCalculation Calculate(
        IEnumerable<LedgerBalance> ledgers,
        IEnumerable<BankAccount> bankAccounts,
        IEnumerable<Installment> installments,
        Func<string, decimal> getExchangeRate)
    {
        ArgumentNullException.ThrowIfNull(ledgers);
        ArgumentNullException.ThrowIfNull(bankAccounts);
        ArgumentNullException.ThrowIfNull(installments);
        ArgumentNullException.ThrowIfNull(getExchangeRate);

        var utcToday = DateTime.UtcNow.Date;

        var totalLedgerBalances = ledgers.Sum(ledger =>
            ConvertToBaseCurrency(ledger.Balance, ledger.Currency, getExchangeRate));

        var totalBankBalances = bankAccounts.Sum(account =>
            ConvertToBaseCurrency(account.TotalAssets, account.Currency, getExchangeRate));

        var maturedFixedDeposits = bankAccounts
            .Where(account => IsMaturedFixedDeposit(account, utcToday))
            .ToList();

        var maturedFixedDepositTotal = maturedFixedDeposits
            .Sum(account => ConvertToBaseCurrency(
                GetMaturedFixedDepositAmount(account),
                account.Currency,
                getExchangeRate));

        var maturedFixedDepositPrincipal = maturedFixedDeposits
            .Sum(account => ConvertToBaseCurrency(
                account.TotalAssets,
                account.Currency,
                getExchangeRate));

        var maturedFixedDepositInterest = maturedFixedDepositTotal - maturedFixedDepositPrincipal;

        var totalAssetsForAvailableFunds = totalLedgerBalances + totalBankBalances + maturedFixedDepositInterest;

        var unpaidInstallmentBalance = installments
            .Where(installment => installment.Status == InstallmentStatus.Active)
            .Sum(installment => installment.MonthlyPayment * installment.RemainingInstallments);

        var availableFunds = totalAssetsForAvailableFunds - unpaidInstallmentBalance;

        return new AvailableFundsCalculation(
            TotalBankAssets: totalAssetsForAvailableFunds,
            FixedDepositsPrincipal: maturedFixedDepositTotal,
            UnpaidInstallmentBalance: unpaidInstallmentBalance,
            AvailableFunds: availableFunds);
    }

    private static bool IsMaturedFixedDeposit(BankAccount account, DateTime utcToday)
    {
        if (account.AccountType != BankAccountType.FixedDeposit)
            return false;

        if (account.FixedDepositStatus is FixedDepositStatus.Closed or FixedDepositStatus.EarlyWithdrawal)
            return false;

        return account.FixedDepositStatus == FixedDepositStatus.Matured
            || (account.MaturityDate.HasValue && account.MaturityDate.Value.Date <= utcToday.Date);
    }

    private static decimal GetMaturedFixedDepositAmount(BankAccount account)
    {
        var interest = account.ActualInterest ?? account.ExpectedInterest ?? 0m;
        return account.TotalAssets + interest;
    }

    private static decimal ConvertToBaseCurrency(
        decimal amount,
        string currency,
        Func<string, decimal> getExchangeRate)
    {
        if (string.Equals(currency, BaseCurrency, StringComparison.OrdinalIgnoreCase))
            return amount;

        return Math.Round(amount * getExchangeRate(currency), 2);
    }
}
