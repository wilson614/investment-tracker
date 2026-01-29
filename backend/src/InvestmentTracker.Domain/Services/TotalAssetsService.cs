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
    public TotalAssetsSummary Calculate(decimal investmentTotal, IReadOnlyList<BankAccount> bankAccounts)
    {
        var bankTotal = bankAccounts.Sum(a => a.TotalAssets);

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
}
