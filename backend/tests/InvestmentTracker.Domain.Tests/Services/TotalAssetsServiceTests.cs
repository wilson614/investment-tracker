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

    [Fact]
    public void Calculate_TwdAccount_UsesBalanceAsIs()
    {
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "TW", totalAssets: 100_000m, currency: "TWD")
        };

        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 31.5m
            });

        result.BankTotal.Should().Be(100_000m);
        result.GrandTotal.Should().Be(100_000m);
    }

    [Fact]
    public void Calculate_UsdAccount_ConvertsUsingExchangeRate()
    {
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "US", totalAssets: 1_000m, currency: "USD")
        };

        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 31.25m
            });

        result.BankTotal.Should().Be(31_250m);
        result.GrandTotal.Should().Be(31_250m);
    }

    [Fact]
    public void Calculate_UsdAccountInterest_ConvertsUsingExchangeRate()
    {
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "US", totalAssets: 1_000m, interestRate: 12m, currency: "USD")
        };

        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 30m
            });

        result.TotalMonthlyInterest.Should().Be(300m);
        result.TotalYearlyInterest.Should().Be(3_600m);
    }

    [Fact]
    public void Calculate_MixedCurrencyInterests_AggregatesInTwd()
    {
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "TW", totalAssets: 120_000m, interestRate: 1.2m, currency: "TWD"),
            new(userId, "US", totalAssets: 1_000m, interestRate: 12m, currency: "USD"),
            new(userId, "EU", totalAssets: 2_000m, interestRate: 6m, currency: "EUR")
        };

        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 31m,
                ["EUR"] = 34m
            });

        result.TotalMonthlyInterest.Should().Be(770m);
        result.TotalYearlyInterest.Should().Be(9_240m);
    }

    [Fact]
    public void Calculate_ForeignCurrencyInterestWithoutExchangeRate_ExcludesUnconvertedInterest()
    {
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "US", totalAssets: 1_000m, interestRate: 12m, currency: "USD")
        };

        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>());

        result.TotalMonthlyInterest.Should().Be(0m);
        result.TotalYearlyInterest.Should().Be(0m);
    }

    [Fact]
    public void Calculate_MixedCurrencies_AggregatesInTwd()
    {
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "TW", totalAssets: 50_000m, currency: "TWD"),
            new(userId, "US", totalAssets: 1_000m, currency: "USD"),
            new(userId, "EU", totalAssets: 200m, currency: "EUR")
        };

        var result = _service.Calculate(
            investmentTotal: 10_000m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 31m,
                ["EUR"] = 34.5m
            });

        result.BankTotal.Should().Be(87_900m);
        result.GrandTotal.Should().Be(97_900m);
    }

    [Fact]
    public void Calculate_ForeignCurrencyWithoutExchangeRate_ExcludesUnconvertedBalance()
    {
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "US", totalAssets: 1_000m, currency: "USD")
        };

        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>());

        result.BankTotal.Should().Be(0m);
        result.GrandTotal.Should().Be(0m);
    }
}
