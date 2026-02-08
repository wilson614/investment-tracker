using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

public record AvailableFundsCalculation(
    decimal TotalBankAssets,
    decimal FixedDepositsPrincipal,
    decimal UnpaidInstallmentBalance,
    decimal AvailableFunds);

public class AvailableFundsService
{
    private const string BaseCurrency = "TWD";

    public AvailableFundsCalculation Calculate(
        IEnumerable<BankAccount> bankAccounts,
        IEnumerable<FixedDeposit> fixedDeposits,
        IEnumerable<Installment> installments,
        Func<string, decimal> getExchangeRate)
    {
        ArgumentNullException.ThrowIfNull(bankAccounts);
        ArgumentNullException.ThrowIfNull(fixedDeposits);
        ArgumentNullException.ThrowIfNull(installments);
        ArgumentNullException.ThrowIfNull(getExchangeRate);

        var totalBankAssets = bankAccounts.Sum(account =>
            ConvertToBaseCurrency(account.TotalAssets, account.Currency, getExchangeRate));

        var fixedDepositsPrincipal = fixedDeposits
            .Where(deposit => deposit.Status == FixedDepositStatus.Active)
            .Sum(deposit => ConvertToBaseCurrency(deposit.Principal, deposit.Currency, getExchangeRate));

        var unpaidInstallmentBalance = installments
            .Where(installment => installment.Status == InstallmentStatus.Active)
            .Sum(installment => installment.MonthlyPayment * installment.RemainingInstallments);

        var availableFunds = totalBankAssets - fixedDepositsPrincipal - unpaidInstallmentBalance;

        return new AvailableFundsCalculation(
            TotalBankAssets: totalBankAssets,
            FixedDepositsPrincipal: fixedDepositsPrincipal,
            UnpaidInstallmentBalance: unpaidInstallmentBalance,
            AvailableFunds: availableFunds);
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
