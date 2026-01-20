namespace InvestmentTracker.Domain.Tests.Services;

/// <summary>
/// Tests for YTD dividend adjustment calculations
/// Verifies that dividend-adjusted total return is calculated correctly
/// </summary>
public class MarketYtdDividendTests
{
    /// <summary>
    /// Test: Total return includes dividend correctly
    ///
    /// Example based on 0050 in 2026:
    /// - Year-end price (2025/12): 65.60
    /// - Current price: 69.85
    /// - Dividend paid (2026/01/22): 2.70
    ///
    /// Expected:
    /// - Price return = (69.85 - 65.60) / 65.60 * 100 = 6.48%
    /// - Total return = (69.85 + 2.70 - 65.60) / 65.60 * 100 = 10.59%
    /// </summary>
    [Fact]
    public void CalculateTotalReturn_WithDividend_ReturnsCorrectPercentage()
    {
        // Arrange
        var yearEndPrice = 65.60m;
        var currentPrice = 69.85m;
        var dividendPaid = 2.70m;

        // Act
        var priceReturn = CalculatePriceReturn(yearEndPrice, currentPrice);
        var totalReturn = CalculateTotalReturn(yearEndPrice, currentPrice, dividendPaid);

        // Assert
        Assert.Equal(6.479m, Math.Round(priceReturn, 3));
        Assert.Equal(10.595m, Math.Round(totalReturn, 3));
    }

    [Fact]
    public void CalculateTotalReturn_NoDividend_SameAsPriceReturn()
    {
        // Arrange
        var yearEndPrice = 100m;
        var currentPrice = 110m;
        var dividendPaid = 0m;

        // Act
        var priceReturn = CalculatePriceReturn(yearEndPrice, currentPrice);
        var totalReturn = CalculateTotalReturn(yearEndPrice, currentPrice, dividendPaid);

        // Assert
        Assert.Equal(priceReturn, totalReturn);
        Assert.Equal(10m, priceReturn);
    }

    [Fact]
    public void CalculateTotalReturn_MultipleDividends_SumsCorrectly()
    {
        // Arrange: 0050 typically pays twice a year (January and July)
        var yearEndPrice = 65.60m;
        var currentPrice = 68.00m;
        var dividend1 = 2.70m;  // January dividend
        var dividend2 = 0.36m;  // July dividend (hypothetical already paid)
        var totalDividends = dividend1 + dividend2;

        // Act
        var priceReturn = CalculatePriceReturn(yearEndPrice, currentPrice);
        var totalReturn = CalculateTotalReturn(yearEndPrice, currentPrice, totalDividends);

        // Assert
        // Price return = (68.00 - 65.60) / 65.60 * 100 = 3.659%
        Assert.Equal(3.659m, Math.Round(priceReturn, 3));
        // Total return = (68.00 + 3.06 - 65.60) / 65.60 * 100 = 8.323%
        Assert.Equal(8.323m, Math.Round(totalReturn, 3));
    }

    [Fact]
    public void CalculateTotalReturn_PriceDrop_DividendOffsetsLoss()
    {
        // Arrange: Price dropped but dividend compensates
        var yearEndPrice = 100m;
        var currentPrice = 95m;  // -5% price drop
        var dividendPaid = 8m;   // 8% dividend yield

        // Act
        var priceReturn = CalculatePriceReturn(yearEndPrice, currentPrice);
        var totalReturn = CalculateTotalReturn(yearEndPrice, currentPrice, dividendPaid);

        // Assert
        Assert.Equal(-5m, priceReturn);  // Price return is negative
        Assert.Equal(3m, totalReturn);   // Total return is positive due to dividend
    }

    [Theory]
    [InlineData(100, 110, 0, 10)]      // 10% gain, no dividend
    [InlineData(100, 110, 5, 15)]      // 10% gain + 5% dividend = 15%
    [InlineData(100, 90, 0, -10)]      // 10% loss, no dividend
    [InlineData(100, 90, 15, 5)]       // 10% loss + 15% dividend = 5% total
    [InlineData(50, 55, 2.5, 15)]      // (55 + 2.5 - 50) / 50 * 100 = 15%
    public void CalculateTotalReturn_VariousScenarios(
        decimal yearEndPrice,
        decimal currentPrice,
        decimal dividend,
        decimal expectedTotalReturn)
    {
        // Act
        var totalReturn = CalculateTotalReturn(yearEndPrice, currentPrice, dividend);

        // Assert
        Assert.Equal(expectedTotalReturn, totalReturn);
    }

    /// <summary>
    /// Helper: Calculate price-only return percentage
    /// </summary>
    private static decimal CalculatePriceReturn(decimal yearEndPrice, decimal currentPrice)
    {
        return (currentPrice - yearEndPrice) / yearEndPrice * 100;
    }

    /// <summary>
    /// Helper: Calculate total return (including dividends) percentage
    /// This mirrors the logic in MarketYtdService.GetBenchmarkYtdAsync
    /// </summary>
    private static decimal CalculateTotalReturn(decimal yearEndPrice, decimal currentPrice, decimal dividendsPaid)
    {
        return (currentPrice + dividendsPaid - yearEndPrice) / yearEndPrice * 100;
    }
}
