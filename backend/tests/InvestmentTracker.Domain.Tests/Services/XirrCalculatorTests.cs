using FluentAssertions;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

/// <summary>
/// Unit tests for XIRR (Extended Internal Rate of Return) calculation.
/// </summary>
public class XirrCalculatorTests
{
    private readonly PortfolioCalculator _calculator = new();

    [Fact]
    public void CalculateXirr_SingleInvestmentWithProfit_ReturnsPositiveRate()
    {
        // Arrange - invest $1000, receive $1100 after 1 year (10% return)
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),
            new(1100m, new DateTime(2025, 1, 1))
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert - ~10% annual return
        xirr.Should().BeApproximately(0.10, 0.01);
    }

    [Fact]
    public void CalculateXirr_SingleInvestmentWithLoss_ReturnsNegativeRate()
    {
        // Arrange - invest $1000, receive $900 after 1 year (-10% return)
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),
            new(900m, new DateTime(2025, 1, 1))
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert - ~-10% annual return
        xirr.Should().BeApproximately(-0.10, 0.01);
    }

    [Fact]
    public void CalculateXirr_MultipleInvestments_CalculatesCorrectly()
    {
        // Arrange - DCA scenario
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),  // Initial investment
            new(-500m, new DateTime(2024, 7, 1)),   // Additional investment
            new(1650m, new DateTime(2025, 1, 1))    // Final value
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert - positive return
        xirr.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateXirr_WithDividends_IncludesIntermediateCashFlows()
    {
        // Arrange - investment with dividends
        var cashFlows = new List<CashFlow>
        {
            new(-10000m, new DateTime(2024, 1, 1)),
            new(200m, new DateTime(2024, 6, 1)),    // Dividend
            new(200m, new DateTime(2024, 12, 1)),   // Dividend
            new(10500m, new DateTime(2025, 1, 1))   // Final sale
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert - ~9% annual return
        xirr.Should().BeApproximately(0.09, 0.02);
    }

    [Fact]
    public void CalculateXirr_ShortTermInvestment_AnnualizesReturn()
    {
        // Arrange - 5% gain in 3 months
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),
            new(1050m, new DateTime(2024, 4, 1))  // 3 months
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert - annualized should be ~21.5% (1.05^4 - 1)
        xirr.Should().BeApproximately(0.215, 0.02);
    }

    [Fact]
    public void CalculateXirr_VeryShortTermHighAnnualizedReturn_DoesNotReturnNull()
    {
        // Arrange - 50% gain in 20 days (annualized return is extremely high)
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),
            new(1500m, new DateTime(2024, 1, 21))
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert
        xirr.Should().NotBeNull();
        xirr.Value.Should().BeGreaterThan(10);
    }

    [Fact]
    public void CalculateXirr_EmptyCashFlows_ReturnsNull()
    {
        // Arrange
        var cashFlows = new List<CashFlow>();
        if (cashFlows == null) throw new ArgumentNullException(nameof(cashFlows));

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert
        xirr.Should().BeNull();
    }

    [Fact]
    public void CalculateXirr_SingleCashFlow_ReturnsNull()
    {
        // Arrange - need at least 2 cash flows
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1))
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert
        xirr.Should().BeNull();
    }

    [Fact]
    public void CalculateXirr_AllSameSign_ReturnsNull()
    {
        // Arrange - all outflows (no inflows)
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),
            new(-500m, new DateTime(2024, 7, 1))
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert
        xirr.Should().BeNull();
    }

    [Fact]
    public void CalculateXirr_BreakEven_ReturnsZero()
    {
        // Arrange - invest $1000, get back $1000 after 1 year
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),
            new(1000m, new DateTime(2025, 1, 1))
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert
        xirr.Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void CalculateXirr_HighReturn_CalculatesCorrectly()
    {
        // Arrange - 100% return in 1 year
        var cashFlows = new List<CashFlow>
        {
            new(-1000m, new DateTime(2024, 1, 1)),
            new(2000m, new DateTime(2025, 1, 1))
        };

        // Act
        var xirr = _calculator.CalculateXirr(cashFlows);

        // Assert
        xirr.Should().BeApproximately(1.0, 0.01);
    }
}
