using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.Tests.UseCases.Assets;

public class TotalAssetsServiceMultiCurrencyTests
{
    private readonly TotalAssetsService _service = new(new InterestEstimationService());

    [Fact]
    public void Calculate_TwdOnlyBankAccounts_DoesNotRequireConversion()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "TW-1", totalAssets: 100_000m, currency: "TWD"),
            new(userId, "TW-2", totalAssets: 50_000m, currency: "TWD")
        };

        // Act
        var result = _service.Calculate(
            investmentTotal: 10_000m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 31.5m,
                ["EUR"] = 34m
            });

        // Assert
        result.BankTotal.Should().Be(150_000m);
        result.GrandTotal.Should().Be(160_000m);
    }

    [Fact]
    public void Calculate_UsdBankAccount_ConvertsToTwdUsingExchangeRate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "US-1", totalAssets: 2_000m, currency: "USD")
        };

        // Act
        var result = _service.Calculate(
            investmentTotal: 0m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 32m
            });

        // Assert
        result.BankTotal.Should().Be(64_000m);
        result.GrandTotal.Should().Be(64_000m);
    }

    [Fact]
    public void Calculate_MixedCurrencies_AggregatesConvertedBankTotalInTwd()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var accounts = new List<BankAccount>
        {
            new(userId, "TW-1", totalAssets: 80_000m, currency: "TWD"),
            new(userId, "US-1", totalAssets: 1_000m, currency: "USD"),
            new(userId, "EU-1", totalAssets: 500m, currency: "EUR")
        };

        // Act
        var result = _service.Calculate(
            investmentTotal: 20_000m,
            bankAccounts: accounts,
            exchangeRatesToTwd: new Dictionary<string, decimal>
            {
                ["USD"] = 31m,
                ["EUR"] = 35m
            });

        // Assert
        result.BankTotal.Should().Be(128_500m); // 80,000 + (1,000*31) + (500*35)
        result.GrandTotal.Should().Be(148_500m);
    }
}
