using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

/// <summary>
/// Tests for PortfolioCalculator including position recalculation and moving average cost.
/// </summary>
public class PortfolioCalculatorTests
{
    private readonly PortfolioCalculator _calculator;
    private readonly Guid _portfolioId;

    public PortfolioCalculatorTests()
    {
        _calculator = new PortfolioCalculator();
        _portfolioId = Guid.NewGuid();
    }

    #region CalculatePosition Tests

    [Fact]
    public void CalculatePosition_SingleBuyTransaction_ReturnsCorrectPosition()
    {
        // Arrange
        var transactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10.5m, 100m, 31.5m, 5m)
        };

        // Act
        var position = _calculator.CalculatePosition("VWRA", transactions);

        // Assert
        Assert.Equal("VWRA", position.Ticker);
        Assert.Equal(10.5m, position.TotalShares);
        // Total cost in source = (10.5 * 100) + 5 = 1055
        // Total cost in home = 1055 * 31.5 = 33232.5
        Assert.Equal(33232.5m, position.TotalCostHome);
        // Average cost = 33232.5 / 10.5 = 3165
        Assert.Equal(3165m, position.AverageCostPerShare, 2);
    }

    [Fact]
    public void CalculatePosition_MultipleBuyTransactions_CalculatesMovingAverageCost()
    {
        // Arrange: Simulate VWRA purchases as per spec
        var transactions = new List<StockTransaction>
        {
            // First buy: 10 shares at $100, rate 31.5
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 0m),
            // Second buy: 5 shares at $110, rate 32.0
            CreateBuyTransaction("VWRA", 5m, 110m, 32.0m, 0m)
        };

        // Act
        var position = _calculator.CalculatePosition("VWRA", transactions);

        // Assert
        Assert.Equal(15m, position.TotalShares);
        // First: 10 * 100 * 31.5 = 31500 TWD
        // Second: 5 * 110 * 32.0 = 17600 TWD
        // Total: 49100 TWD for 15 shares
        // Average: 49100 / 15 = 3273.33
        Assert.Equal(49100m, position.TotalCostHome);
        Assert.Equal(3273.33m, position.AverageCostPerShare, 2);
    }

    [Fact]
    public void CalculatePosition_BuyAndSellTransactions_UpdatesPositionCorrectly()
    {
        // Arrange
        var transactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 20m, 100m, 31.5m, 0m),
            CreateSellTransaction("VWRA", 5m, 120m, 32.0m, 0m)
        };

        // Act
        var position = _calculator.CalculatePosition("VWRA", transactions);

        // Assert
        Assert.Equal(15m, position.TotalShares);
        // Buy: 20 * 100 * 31.5 = 63000 TWD, avg = 3150 per share
        // Sell removes 5 shares, cost basis = 5 * 3150 = 15750
        // Remaining: 63000 - 15750 = 47250 TWD for 15 shares
        // Average remains 3150 (unchanged for remaining shares)
        Assert.Equal(47250m, position.TotalCostHome, 2);
        Assert.Equal(3150m, position.AverageCostPerShare, 2);
    }

    [Fact]
    public void CalculatePosition_WithFees_IncludesFeesInCost()
    {
        // Arrange
        var transactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 10m)
        };

        // Act
        var position = _calculator.CalculatePosition("VWRA", transactions);

        // Assert
        // Total source = (10 * 100) + 10 = 1010
        // Total home = 1010 * 31.5 = 31815
        Assert.Equal(31815m, position.TotalCostHome);
        Assert.Equal(3181.5m, position.AverageCostPerShare, 2);
    }

    [Fact]
    public void CalculatePosition_EmptyTransactions_ReturnsZeroPosition()
    {
        // Arrange
        var transactions = new List<StockTransaction>();

        // Act
        var position = _calculator.CalculatePosition("VWRA", transactions);

        // Assert
        Assert.Equal("VWRA", position.Ticker);
        Assert.Equal(0m, position.TotalShares);
        Assert.Equal(0m, position.TotalCostHome);
        Assert.Equal(0m, position.AverageCostPerShare);
    }

    [Fact]
    public void CalculatePosition_FractionalShares_Handles4DecimalPlaces()
    {
        // Arrange
        var transactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10.1234m, 100m, 31.5m, 0m)
        };

        // Act
        var position = _calculator.CalculatePosition("VWRA", transactions);

        // Assert
        Assert.Equal(10.1234m, position.TotalShares);
    }

    #endregion

    #region CalculateUnrealizedPnl Tests

    [Fact]
    public void CalculateUnrealizedPnl_PriceIncreased_ReturnsPositivePnl()
    {
        // Arrange
        var transactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 0m)
        };
        var position = _calculator.CalculatePosition("VWRA", transactions);
        decimal currentPrice = 120m;
        decimal currentExchangeRate = 32m;

        // Act
        var pnl = _calculator.CalculateUnrealizedPnl(position, currentPrice, currentExchangeRate);

        // Assert
        // Current value = 10 * 120 * 32 = 38400
        // Cost = 10 * 100 * 31.5 = 31500
        // PnL = 38400 - 31500 = 6900
        Assert.Equal(6900m, pnl.UnrealizedPnlHome);
        Assert.True(pnl.UnrealizedPnlPercentage > 0);
    }

    [Fact]
    public void CalculateUnrealizedPnl_PriceDecreased_ReturnsNegativePnl()
    {
        // Arrange
        var transactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 0m)
        };
        var position = _calculator.CalculatePosition("VWRA", transactions);
        decimal currentPrice = 80m;
        decimal currentExchangeRate = 30m;

        // Act
        var pnl = _calculator.CalculateUnrealizedPnl(position, currentPrice, currentExchangeRate);

        // Assert
        // Current value = 10 * 80 * 30 = 24000
        // Cost = 31500
        // PnL = 24000 - 31500 = -7500
        Assert.Equal(-7500m, pnl.UnrealizedPnlHome);
        Assert.True(pnl.UnrealizedPnlPercentage < 0);
    }

    #endregion

    #region CalculateRealizedPnl Tests

    [Fact]
    public void CalculateRealizedPnl_SellAtProfit_ReturnsPositivePnl()
    {
        // Arrange: Buy at 100, sell at 120
        var buyTransactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 0m)
        };
        var sellTransaction = CreateSellTransaction("VWRA", 5m, 120m, 32m, 0m);
        var position = _calculator.CalculatePosition("VWRA", buyTransactions);

        // Act
        var realizedPnl = _calculator.CalculateRealizedPnl(position, sellTransaction);

        // Assert
        // Avg cost per share = 31500 / 10 = 3150 TWD
        // Cost basis for 5 shares = 5 * 3150 = 15750 TWD
        // Sale proceeds = 5 * 120 * 32 = 19200 TWD
        // Realized PnL = 19200 - 15750 = 3450 TWD
        Assert.Equal(3450m, realizedPnl);
    }

    [Fact]
    public void CalculateRealizedPnl_SellAtLoss_ReturnsNegativePnl()
    {
        // Arrange: Buy at 100, sell at 80
        var buyTransactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 0m)
        };
        var sellTransaction = CreateSellTransaction("VWRA", 5m, 80m, 30m, 0m);
        var position = _calculator.CalculatePosition("VWRA", buyTransactions);

        // Act
        var realizedPnl = _calculator.CalculateRealizedPnl(position, sellTransaction);

        // Assert
        // Cost basis = 5 * 3150 = 15750 TWD
        // Sale proceeds = 5 * 80 * 30 = 12000 TWD
        // Realized PnL = 12000 - 15750 = -3750 TWD
        Assert.Equal(-3750m, realizedPnl);
    }

    #endregion

    #region Helper Methods

    private StockTransaction CreateBuyTransaction(
        string ticker, decimal shares, decimal price, decimal exchangeRate, decimal fees)
    {
        return new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            ticker,
            TransactionType.Buy,
            shares,
            price,
            exchangeRate,
            fees);
    }

    private StockTransaction CreateSellTransaction(
        string ticker, decimal shares, decimal price, decimal exchangeRate, decimal fees)
    {
        return new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            ticker,
            TransactionType.Sell,
            shares,
            price,
            exchangeRate,
            fees);
    }

    #endregion
}
