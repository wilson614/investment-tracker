using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

/// <summary>
/// Tests for StockSplitAdjustmentService including cumulative split calculations.
/// </summary>
public class StockSplitAdjustmentServiceTests
{
    private readonly StockSplitAdjustmentService _service = new();

    #region GetCumulativeSplitRatio Tests

    [Fact]
    public void GetCumulativeSplitRatio_NoSplits_ReturnsOne()
    {
        // Arrange
        var splits = new List<StockSplit>();

        // Act
        var ratio = _service.GetCumulativeSplitRatio("0050", StockMarket.TW, new DateTime(2024, 1, 1), splits);

        // Assert
        Assert.Equal(1.0m, ratio);
    }

    [Fact]
    public void GetCumulativeSplitRatio_SingleSplitAfterTransaction_ReturnsSplitRatio()
    {
        // Arrange: 0050 had a 1:4 split on 2025-06-18
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };

        // Transaction before split date
        var transactionDate = new DateTime(2024, 1, 15);

        // Act
        var ratio = _service.GetCumulativeSplitRatio("0050", StockMarket.TW, transactionDate, splits);

        // Assert
        Assert.Equal(4.0m, ratio);
    }

    [Fact]
    public void GetCumulativeSplitRatio_TransactionAfterSplit_ReturnsOne()
    {
        // Arrange
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };

        // Transaction after split date
        var transactionDate = new DateTime(2025, 7, 1);

        // Act
        var ratio = _service.GetCumulativeSplitRatio("0050", StockMarket.TW, transactionDate, splits);

        // Assert
        Assert.Equal(1.0m, ratio);
    }

    [Fact]
    public void GetCumulativeSplitRatio_MultipleSplits_ReturnsCumulativeRatio()
    {
        // Arrange: Hypothetical stock with multiple splits
        var splits = new List<StockSplit>
        {
            new StockSplit("TEST", StockMarket.US, new DateTime(2023, 6, 1), 2.0m, "1拆2"),
            new StockSplit("TEST", StockMarket.US, new DateTime(2024, 6, 1), 3.0m, "1拆3")
        };

        // Transaction before both splits
        var transactionDate = new DateTime(2022, 1, 1);

        // Act
        var ratio = _service.GetCumulativeSplitRatio("TEST", StockMarket.US, transactionDate, splits);

        // Assert: 2.0 * 3.0 = 6.0
        Assert.Equal(6.0m, ratio);
    }

    [Fact]
    public void GetCumulativeSplitRatio_TransactionBetweenSplits_ReturnsPartialCumulativeRatio()
    {
        // Arrange
        var splits = new List<StockSplit>
        {
            new StockSplit("TEST", StockMarket.US, new DateTime(2023, 6, 1), 2.0m, "1拆2"),
            new StockSplit("TEST", StockMarket.US, new DateTime(2024, 6, 1), 3.0m, "1拆3")
        };

        // Transaction between the two splits
        var transactionDate = new DateTime(2023, 12, 1);

        // Act
        var ratio = _service.GetCumulativeSplitRatio("TEST", StockMarket.US, transactionDate, splits);

        // Assert: Only the second split applies (3.0)
        Assert.Equal(3.0m, ratio);
    }

    [Fact]
    public void GetCumulativeSplitRatio_DifferentSymbol_ReturnsOne()
    {
        // Arrange
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };

        // Different symbol
        var transactionDate = new DateTime(2024, 1, 1);

        // Act
        var ratio = _service.GetCumulativeSplitRatio("0056", StockMarket.TW, transactionDate, splits);

        // Assert
        Assert.Equal(1.0m, ratio);
    }

    [Fact]
    public void GetCumulativeSplitRatio_DifferentMarket_ReturnsOne()
    {
        // Arrange
        var splits = new List<StockSplit>
        {
            new StockSplit("AAPL", StockMarket.US, new DateTime(2020, 8, 31), 4.0m, "1拆4")
        };

        // Same symbol but different market (hypothetical)
        var transactionDate = new DateTime(2020, 1, 1);

        // Act
        var ratio = _service.GetCumulativeSplitRatio("AAPL", StockMarket.UK, transactionDate, splits);

        // Assert
        Assert.Equal(1.0m, ratio);
    }

    #endregion

    #region GetAdjustedShares Tests

    [Fact]
    public void GetAdjustedShares_SplitApplies_ReturnsAdjustedShares()
    {
        // Arrange: Original 10 shares of 0050, split 1:4
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };
        var originalShares = 10m;
        var transactionDate = new DateTime(2024, 1, 15);

        // Act
        var adjustedShares = _service.GetAdjustedShares(originalShares, "0050", StockMarket.TW, transactionDate, splits);

        // Assert: 10 * 4 = 40
        Assert.Equal(40m, adjustedShares);
    }

    [Fact]
    public void GetAdjustedShares_NoSplitApplies_ReturnsOriginalShares()
    {
        // Arrange
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };
        var originalShares = 10m;
        var transactionDate = new DateTime(2025, 7, 1);

        // Act
        var adjustedShares = _service.GetAdjustedShares(originalShares, "0050", StockMarket.TW, transactionDate, splits);

        // Assert
        Assert.Equal(10m, adjustedShares);
    }

    #endregion

    #region GetAdjustedPrice Tests

    [Fact]
    public void GetAdjustedPrice_SplitApplies_ReturnsAdjustedPrice()
    {
        // Arrange: Original price 160, split 1:4
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };
        var originalPrice = 160m;
        var transactionDate = new DateTime(2024, 1, 15);

        // Act
        var adjustedPrice = _service.GetAdjustedPrice(originalPrice, "0050", StockMarket.TW, transactionDate, splits);

        // Assert: 160 / 4 = 40
        Assert.Equal(40m, adjustedPrice);
    }

    [Fact]
    public void GetAdjustedPrice_NoSplitApplies_ReturnsOriginalPrice()
    {
        // Arrange
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };
        var originalPrice = 40m;
        var transactionDate = new DateTime(2025, 7, 1);

        // Act
        var adjustedPrice = _service.GetAdjustedPrice(originalPrice, "0050", StockMarket.TW, transactionDate, splits);

        // Assert
        Assert.Equal(40m, adjustedPrice);
    }

    [Fact]
    public void GetAdjustedPrice_NonExactDivision_PreservesDecimalPrecision()
    {
        // Arrange: Original price 163, split 1:4 = 40.75
        // Split adjustment preserves total cost: shares * price = adjusted_shares * adjusted_price
        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };
        var originalPrice = 163m;
        var transactionDate = new DateTime(2024, 1, 15);

        // Act
        var adjustedPrice = _service.GetAdjustedPrice(originalPrice, "0050", StockMarket.TW, transactionDate, splits);

        // Assert: 163 / 4 = 40.75 (no floor - total cost must be preserved)
        Assert.Equal(40.75m, adjustedPrice);
    }

    [Fact]
    public void GetAdjustedPrice_ExactDivision_ReturnsExactValue()
    {
        // Arrange: Exact division case
        var splits = new List<StockSplit>
        {
            new StockSplit("AAPL", StockMarket.US, new DateTime(2020, 8, 31), 4.0m, "1:4 split")
        };
        var originalPrice = 500m;
        var transactionDate = new DateTime(2020, 1, 15);

        // Act
        var adjustedPrice = _service.GetAdjustedPrice(originalPrice, "AAPL", StockMarket.US, transactionDate, splits);

        // Assert: 500 / 4 = 125
        Assert.Equal(125m, adjustedPrice);
    }

    #endregion

    #region DetectMarket Tests

    [Theory]
    [InlineData("0050", StockMarket.TW)]
    [InlineData("2330", StockMarket.TW)]
    [InlineData("6547R", StockMarket.TW)]
    [InlineData("AAPL", StockMarket.US)]
    [InlineData("VOO", StockMarket.US)]
    [InlineData("VWRA", StockMarket.US)]
    [InlineData("HSBA.L", StockMarket.UK)]
    public void DetectMarket_VariousSymbols_ReturnsExpectedMarket(string symbol, StockMarket expected)
    {
        // Act
        var market = _service.DetectMarket(symbol);

        // Assert
        Assert.Equal(expected, market);
    }

    #endregion

    #region TotalCost Preservation Tests

    [Fact]
    public void TotalCost_PreservedAfterSplitAdjustment()
    {
        // Arrange: Original 10 shares @ 160 = 1600 total
        var originalShares = 10m;
        var originalPrice = 160m;
        var originalTotalCost = originalShares * originalPrice; // 1600

        var splits = new List<StockSplit>
        {
            new StockSplit("0050", StockMarket.TW, new DateTime(2025, 6, 18), 4.0m, "1拆4")
        };
        var transactionDate = new DateTime(2024, 1, 15);

        // Act
        var adjustedShares = _service.GetAdjustedShares(originalShares, "0050", StockMarket.TW, transactionDate, splits);
        var adjustedPrice = _service.GetAdjustedPrice(originalPrice, "0050", StockMarket.TW, transactionDate, splits);
        var adjustedTotalCost = adjustedShares * adjustedPrice;

        // Assert: Total cost should be preserved
        // Original: 10 * 160 = 1600
        // Adjusted: 40 * 40 = 1600
        Assert.Equal(originalTotalCost, adjustedTotalCost);
    }

    #endregion
}
