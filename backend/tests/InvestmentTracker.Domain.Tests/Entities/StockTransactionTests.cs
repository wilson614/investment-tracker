using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Tests.Entities;

/// <summary>
/// Tests for StockTransaction entity validation and computed properties.
/// </summary>
public class StockTransactionTests
{
    private readonly Guid _portfolioId = Guid.NewGuid();

    #region Constructor Validation Tests

    [Fact]
    public void Constructor_ValidParameters_CreatesTransaction()
    {
        // Arrange & Act
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow.Date,
            "VWRA",
            TransactionType.Buy,
            10.5m,
            100m,
            31.5m,
            5m);

        // Assert
        Assert.Equal(_portfolioId, transaction.PortfolioId);
        Assert.Equal("VWRA", transaction.Ticker);
        Assert.Equal(TransactionType.Buy, transaction.TransactionType);
        Assert.Equal(10.5m, transaction.Shares);
        Assert.Equal(100m, transaction.PricePerShare);
        Assert.Equal(31.5m, transaction.ExchangeRate);
        Assert.Equal(5m, transaction.Fees);
    }

    [Fact]
    public void Constructor_EmptyPortfolioId_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                Guid.Empty,
                DateTime.UtcNow,
                "VWRA",
                TransactionType.Buy,
                10m,
                100m,
                31.5m));

        Assert.Contains("Portfolio ID", exception.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Constructor_InvalidTicker_ThrowsArgumentException(string? ticker)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                _portfolioId,
                DateTime.UtcNow,
                ticker!,
                TransactionType.Buy,
                10m,
                100m,
                31.5m));

        Assert.Contains("Ticker", exception.Message);
    }

    [Fact]
    public void Constructor_TickerTooLong_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                _portfolioId,
                DateTime.UtcNow,
                new string('A', 21),
                TransactionType.Buy,
                10m,
                100m,
                31.5m));

        Assert.Contains("20 characters", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_NonPositiveShares_ThrowsArgumentException(decimal shares)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                _portfolioId,
                DateTime.UtcNow,
                "VWRA",
                TransactionType.Buy,
                shares,
                100m,
                31.5m));

        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public void Constructor_NegativePrice_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                _portfolioId,
                DateTime.UtcNow,
                "VWRA",
                TransactionType.Buy,
                10m,
                -100m,
                31.5m));

        Assert.Contains("negative", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveExchangeRate_ThrowsArgumentException(decimal rate)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                _portfolioId,
                DateTime.UtcNow,
                "VWRA",
                TransactionType.Buy,
                10m,
                100m,
                rate));

        Assert.Contains("positive", exception.Message);
    }

    [Fact]
    public void Constructor_NegativeFees_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                _portfolioId,
                DateTime.UtcNow,
                "VWRA",
                TransactionType.Buy,
                10m,
                100m,
                31.5m,
                -5m));

        Assert.Contains("negative", exception.Message);
    }

    [Fact]
    public void Constructor_FutureDate_ThrowsArgumentException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            new StockTransaction(
                _portfolioId,
                DateTime.UtcNow.AddDays(7),
                "VWRA",
                TransactionType.Buy,
                10m,
                100m,
                31.5m));

        Assert.Contains("future", exception.Message);
    }

    #endregion

    #region Computed Properties Tests

    [Fact]
    public void TotalCostSource_CalculatesCorrectly()
    {
        // Arrange
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m,
            15m);

        // Act & Assert
        // (10 * 100) + 15 = 1015
        Assert.Equal(1015m, transaction.TotalCostSource);
    }

    [Fact]
    public void TotalCostHome_CalculatesCorrectly()
    {
        // Arrange
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m,
            15m);

        // Act & Assert
        // TotalCostSource * ExchangeRate = 1015 * 31.5 = 31972.5
        Assert.Equal(31972.5m, transaction.TotalCostHome);
    }

    #endregion

    #region Precision Tests

    [Fact]
    public void Shares_RoundsTo4DecimalPlaces()
    {
        // Arrange & Act
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10.123456789m,
            100m,
            31.5m);

        // Assert
        Assert.Equal(10.1235m, transaction.Shares);
    }

    [Fact]
    public void ExchangeRate_RoundsTo6DecimalPlaces()
    {
        // Arrange & Act
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.1234567890m);

        // Assert
        Assert.Equal(31.123457m, transaction.ExchangeRate);
    }

    [Fact]
    public void Fees_RoundsTo2DecimalPlaces()
    {
        // Arrange & Act
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m,
            15.999m);

        // Assert
        Assert.Equal(16m, transaction.Fees);
    }

    #endregion

    #region Fund Source Tests

    [Fact]
    public void SetFundSource_CurrencyLedgerWithoutId_ThrowsArgumentException()
    {
        // Arrange
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            transaction.SetFundSource(FundSource.CurrencyLedger, null));

        Assert.Contains("Currency ledger ID is required", exception.Message);
    }

    [Fact]
    public void SetFundSource_NoneWithId_ThrowsArgumentException()
    {
        // Arrange
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m);

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            transaction.SetFundSource(FundSource.None, Guid.NewGuid()));

        Assert.Contains("should only be set when fund source is CurrencyLedger", exception.Message);
    }

    [Fact]
    public void SetFundSource_ValidCurrencyLedger_SetsCorrectly()
    {
        // Arrange
        var ledgerId = Guid.NewGuid();
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m);

        // Act
        transaction.SetFundSource(FundSource.CurrencyLedger, ledgerId);

        // Assert
        Assert.Equal(FundSource.CurrencyLedger, transaction.FundSource);
        Assert.Equal(ledgerId, transaction.CurrencyLedgerId);
    }

    #endregion

    #region Soft Delete Tests

    [Fact]
    public void MarkAsDeleted_SetsIsDeletedTrue()
    {
        // Arrange
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m);

        // Act
        transaction.MarkAsDeleted();

        // Assert
        Assert.True(transaction.IsDeleted);
    }

    [Fact]
    public void Restore_SetsIsDeletedFalse()
    {
        // Arrange
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VWRA",
            TransactionType.Buy,
            10m,
            100m,
            31.5m);
        transaction.MarkAsDeleted();

        // Act
        transaction.Restore();

        // Assert
        Assert.False(transaction.IsDeleted);
    }

    #endregion

    #region Ticker Normalization Tests

    [Fact]
    public void Ticker_NormalizesToUpperCase()
    {
        // Arrange & Act
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "vwra",
            TransactionType.Buy,
            10m,
            100m,
            31.5m);

        // Assert
        Assert.Equal("VWRA", transaction.Ticker);
    }

    [Fact]
    public void Ticker_TrimsWhitespace()
    {
        // Arrange & Act
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "  VWRA  ",
            TransactionType.Buy,
            10m,
            100m,
            31.5m);

        // Assert
        Assert.Equal("VWRA", transaction.Ticker);
    }

    #endregion

    #region Taiwan Stock Floor Tests

    [Theory]
    [InlineData("0050", true)]
    [InlineData("2330", true)]
    [InlineData("6547R", true)]
    [InlineData("AAPL", false)]
    [InlineData("VOO", false)]
    [InlineData("VWRA", false)]
    [InlineData("HSBA.L", false)]
    public void IsTaiwanStock_CorrectlyIdentifiesMarket(string ticker, bool expected)
    {
        // Arrange & Act
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            ticker,
            TransactionType.Buy,
            1m,
            100m,
            1m);

        // Assert
        Assert.Equal(expected, transaction.IsTaiwanStock);
    }

    [Fact]
    public void TotalCostSource_TaiwanStock_UsesFloorForSubtotal()
    {
        // Arrange: 3 shares × 27.25 = 81.75, should floor to 81, plus 0 fees = 81
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "0050",
            TransactionType.Buy,
            3m,
            27.25m,
            1m,  // ExchangeRate = 1 for TWD
            0m); // No fees

        // Act & Assert
        Assert.Equal(81m, transaction.TotalCostSource);
    }

    [Fact]
    public void TotalCostSource_TaiwanStock_FloorThenAddFees()
    {
        // Arrange: 3 shares × 27.25 = 81.75 -> floor to 81, plus 20 fees = 101
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "2330",
            TransactionType.Buy,
            3m,
            27.25m,
            1m,
            20m);

        // Act & Assert
        Assert.Equal(101m, transaction.TotalCostSource);
    }

    [Fact]
    public void TotalCostSource_USStock_DoesNotUseFloor()
    {
        // Arrange: 3 shares × 27.25 = 81.75 (no floor for US stocks)
        var transaction = new StockTransaction(
            _portfolioId,
            DateTime.UtcNow,
            "VOO",
            TransactionType.Buy,
            3m,
            27.25m,
            30m,
            0m);

        // Act & Assert
        Assert.Equal(81.75m, transaction.TotalCostSource);
    }

    #endregion
}
