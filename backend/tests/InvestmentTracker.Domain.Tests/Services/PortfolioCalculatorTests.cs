using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Domain.Tests.Services;

/// <summary>
/// Tests for PortfolioCalculator including position recalculation and moving average cost.
/// </summary>
public class PortfolioCalculatorTests
{
    private readonly PortfolioCalculator _calculator = new();
    private readonly Guid _portfolioId = Guid.NewGuid();

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
        Assert.Equal(3165m, position.AverageCostPerShareHome, 2);
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
        Assert.Equal(3273.33m, position.AverageCostPerShareHome, 2);
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
        Assert.Equal(3150m, position.AverageCostPerShareHome, 2);
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
        Assert.Equal(3181.5m, position.AverageCostPerShareHome, 2);
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
        Assert.Equal(0m, position.AverageCostPerShareHome);
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
        var currentPrice = 120m;
        var currentExchangeRate = 32m;

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
        var currentPrice = 80m;
        var currentExchangeRate = 30m;

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

    [Fact]
    public void CalculateRealizedPnl_TaiwanStock_UsesFloorForProceeds()
    {
        // Arrange: Taiwan stock with non-integer subtotal
        // Buy 10 shares at 100 TWD = 1000 cost basis
        var buyTransactions = new List<StockTransaction>
        {
            CreateBuyTransaction("0050", 10m, 100m, 1m, 0m)
        };
        // Sell 3 shares at 27.25 TWD
        // Subtotal = 3 × 27.25 = 81.75, floor to 81
        // Proceeds = 81 - 0 fees = 81
        var sellTransaction = CreateSellTransaction("0050", 3m, 27.25m, 1m, 0m);
        var position = _calculator.CalculatePosition("0050", buyTransactions);

        // Act
        var realizedPnl = _calculator.CalculateRealizedPnl(position, sellTransaction);

        // Assert
        // Avg cost per share = 1000 / 10 = 100 TWD
        // Cost basis for 3 shares = 3 * 100 = 300 TWD
        // Sale proceeds = Floor(3 × 27.25) = 81 TWD
        // Realized PnL = 81 - 300 = -219 TWD
        Assert.Equal(-219m, realizedPnl);
    }

    [Fact]
    public void CalculateRealizedPnl_USStock_DoesNotUseFloor()
    {
        // Arrange: US stock keeps decimal precision
        var buyTransactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VOO", 10m, 100m, 30m, 0m)
        };
        // Sell 3 shares at 27.25 USD
        // Subtotal = 3 × 27.25 = 81.75 (no floor)
        var sellTransaction = CreateSellTransaction("VOO", 3m, 27.25m, 30m, 0m);
        var position = _calculator.CalculatePosition("VOO", buyTransactions);

        // Act
        var realizedPnl = _calculator.CalculateRealizedPnl(position, sellTransaction);

        // Assert
        // Avg cost per share home = (10 * 100 * 30) / 10 = 3000 TWD
        // Cost basis for 3 shares = 3 * 3000 = 9000 TWD
        // Sale proceeds = 81.75 * 30 = 2452.5 TWD
        // Realized PnL = 2452.5 - 9000 = -6547.5 TWD
        Assert.Equal(-6547.5m, realizedPnl);
    }

    [Fact]
    public void CalculateRealizedPnl_WithSellFees_ReducesPnl()
    {
        // Arrange: Same as SellAtProfit but WITH fees
        var buyTransactions = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 0m)
        };
        // Sell with 100 USD fees
        var sellTransactionWithFees = CreateSellTransaction("VWRA", 5m, 120m, 32m, 100m);
        var sellTransactionNoFees = CreateSellTransaction("VWRA", 5m, 120m, 32m, 0m);
        var position = _calculator.CalculatePosition("VWRA", buyTransactions);

        // Act
        var pnlWithFees = _calculator.CalculateRealizedPnl(position, sellTransactionWithFees);
        var pnlNoFees = _calculator.CalculateRealizedPnl(position, sellTransactionNoFees);

        // Assert
        // Without fees: proceeds = 5 * 120 * 32 = 19200, cost = 15750, PnL = 3450
        // With 100 fees: proceeds = (600 - 100) * 32 = 16000, cost = 15750, PnL = 250
        // Difference should be 100 * 32 = 3200
        Assert.Equal(3450m, pnlNoFees);
        Assert.Equal(250m, pnlWithFees);
        Assert.Equal(3200m, pnlNoFees - pnlWithFees);  // Fee impact = 100 * 32
    }

    [Fact]
    public void CalculateRealizedPnl_WithBuyFees_ReducesPnl()
    {
        // Arrange: Buy WITH fees vs WITHOUT fees
        var buyWithFees = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 100m)  // 100 fees
        };
        var buyNoFees = new List<StockTransaction>
        {
            CreateBuyTransaction("VWRA", 10m, 100m, 31.5m, 0m)
        };
        var sellTransaction = CreateSellTransaction("VWRA", 5m, 120m, 32m, 0m);

        var positionWithFees = _calculator.CalculatePosition("VWRA", buyWithFees);
        var positionNoFees = _calculator.CalculatePosition("VWRA", buyNoFees);

        // Act
        var pnlWithBuyFees = _calculator.CalculateRealizedPnl(positionWithFees, sellTransaction);
        var pnlNoBuyFees = _calculator.CalculateRealizedPnl(positionNoFees, sellTransaction);

        // Assert
        // Without buy fees: avg cost = 31500/10 = 3150, cost basis = 15750
        // With 100 buy fees: total cost = (1000+100)*31.5 = 34650, avg = 3465, cost basis = 17325
        // Sale proceeds = 19200 for both
        // PnL without fees = 19200 - 15750 = 3450
        // PnL with fees = 19200 - 17325 = 1875
        Assert.Equal(3450m, pnlNoBuyFees);
        Assert.Equal(1875m, pnlWithBuyFees);
        Assert.True(pnlWithBuyFees < pnlNoBuyFees);  // Buy fees reduce PnL
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
