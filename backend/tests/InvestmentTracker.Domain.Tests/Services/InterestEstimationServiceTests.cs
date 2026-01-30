using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

public class InterestEstimationServiceTests
{
    private readonly InterestEstimationService _service = new();

    [Fact]
    public void Calculate_TotalAssetsGreaterThanInterestCap_UsesInterestCapForPrincipal()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var account = new BankAccount(
            userId,
            bankName: "Test",
            totalAssets: 500_000m,
            interestRate: 3.0m,
            interestCap: 300_000m);

        // Act
        var result = _service.Calculate(account);

        // Assert
        result.MonthlyInterest.Should().Be(750m);
        result.YearlyInterest.Should().Be(9_000m);
    }

    [Fact]
    public void Calculate_TotalAssetsLessThanInterestCap_UsesTotalAssetsForPrincipal()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var account = new BankAccount(
            userId,
            bankName: "Test",
            totalAssets: 200_000m,
            interestRate: 2.5m,
            interestCap: 500_000m);

        // Act
        var result = _service.Calculate(account);

        // Assert
        result.MonthlyInterest.Should().Be(416.67m);
        result.YearlyInterest.Should().Be(5_000.04m);
    }

    [Fact]
    public void Calculate_WithZeroInterestRate_ReturnsZeroInterest()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var account = new BankAccount(
            userId,
            bankName: "Test",
            totalAssets: 100_000m,
            interestRate: 0m,
            interestCap: 300_000m);

        // Act
        var result = _service.Calculate(account);

        // Assert
        result.MonthlyInterest.Should().Be(0m);
        result.YearlyInterest.Should().Be(0m);
    }
}
