using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Services;

public sealed record InterestEstimation(decimal MonthlyInterest, decimal YearlyInterest);

/// <summary>
/// Interest estimation calculator for bank accounts.
/// </summary>
public class InterestEstimationService
{
    public InterestEstimation Calculate(BankAccount account)
    {
        var effectivePrincipal = account.InterestCap <= 0
            ? account.TotalAssets
            : Math.Min(account.TotalAssets, account.InterestCap);

        var monthlyInterest = effectivePrincipal * (account.InterestRate / 100m / 12m);
        monthlyInterest = Math.Round(monthlyInterest, 2);

        var yearlyInterest = Math.Round(monthlyInterest * 12m, 2);

        return new InterestEstimation(monthlyInterest, yearlyInterest);
    }
}
