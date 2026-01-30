using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

public class TotalAssetsServiceTests
{
    private readonly TotalAssetsService _service = new(new InterestEstimationService());

    [Fact]
    public void Calculate_NoInvestmentsAndNoBankAccounts_ReturnsAllZeros()
    {
        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: []);

        result.InvestmentTotal.Should().Be(0m);
        result.BankTotal.Should().Be(0m);
        result.GrandTotal.Should().Be(0m);
        result.InvestmentPercentage.Should().Be(0m);
        result.BankPercentage.Should().Be(0m);
        result.TotalMonthlyInterest.Should().Be(0m);
        result.TotalYearlyInterest.Should().Be(0m);
    }

    [Fact]
    public void Calculate_OnlyInvestments_ReturnsInvestmentOnly()
    {
        var result = _service.Calculate(
            investmentTotal: 100m,
            bankAccounts: []);

        result.InvestmentTotal.Should().Be(100m);
        result.BankTotal.Should().Be(0m);
        result.GrandTotal.Should().Be(100m);
        result.InvestmentPercentage.Should().Be(100m);
        result.BankPercentage.Should().Be(0m);
        result.TotalMonthlyInterest.Should().Be(0m);
        result.TotalYearlyInterest.Should().Be(0m);
    }

    [Fact]
    public void Calculate_OnlyBankAccounts_ReturnsBankOnly()
    {
        var userId = Guid.NewGuid();

        var accounts = new List<BankAccount>
        {
            new(userId, "A", totalAssets: 500_000m, interestRate: 3.0m, interestCap: 300_000m),
            new(userId, "B", totalAssets: 200_000m, interestRate: 2.5m, interestCap: 500_000m)
        };

        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts);

        result.InvestmentTotal.Should().Be(0m);
        result.BankTotal.Should().Be(700_000m);
        result.GrandTotal.Should().Be(700_000m);
        result.InvestmentPercentage.Should().Be(0m);
        result.BankPercentage.Should().Be(100m);

        // Interest: (min(500,000, 300,000) * 3% / 12) + (min(200,000, 500,000) * 2.5% / 12)
        result.TotalMonthlyInterest.Should().Be(750m + 416.67m);

        // Yearly is monthly * 12, rounded per-account in InterestEstimationService.
        result.TotalYearlyInterest.Should().Be(9_000m + 5_000.04m);
    }

    [Fact]
    public void Calculate_WithInvestmentsAndBankAccounts_CalculatesTotalsAndPercentages()
    {
        var userId = Guid.NewGuid();

        var accounts = new List<BankAccount>
        {
            new(userId, "A", totalAssets: 500_000m, interestRate: 3.0m, interestCap: 300_000m),
            new(userId, "B", totalAssets: 200_000m, interestRate: 2.5m, interestCap: 500_000m)
        };

        var result = _service.Calculate(
            investmentTotal: 446_000m,
            bankAccounts: accounts);

        result.InvestmentTotal.Should().Be(446_000m);
        result.BankTotal.Should().Be(700_000m);
        result.GrandTotal.Should().Be(1_146_000m);

        // Interest: (300,000 * 3% / 12) + (200,000 * 2.5% / 12)
        result.TotalMonthlyInterest.Should().Be(750m + 416.67m);

        // Yearly is monthly * 12, rounded per-account in InterestEstimationService.
        result.TotalYearlyInterest.Should().Be(9_000m + 5_000.04m);

        // Percentages
        result.InvestmentPercentage.Should().BeApproximately(446_000m / 1_146_000m * 100m, 0.0001m);
        result.BankPercentage.Should().BeApproximately(700_000m / 1_146_000m * 100m, 0.0001m);
    }
}
