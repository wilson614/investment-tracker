using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.Portfolio;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Application.Tests.UseCases;

/// <summary>
/// Unit tests for CalculateXirrUseCase focusing on FX auto-fill behavior (T083, T124).
/// Tests the TWD XIRR calculation with various exchange rate scenarios.
/// </summary>
public class CalculateXirrUseCaseTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepoMock;
    private readonly Mock<IStockTransactionRepository> _transactionRepoMock;
    private readonly Mock<IStockSplitRepository> _stockSplitRepoMock;
    private readonly Mock<ITransactionDateExchangeRateService> _txDateFxServiceMock;
    private readonly CalculateXirrUseCase _useCase;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _portfolioId = Guid.NewGuid();

    public CalculateXirrUseCaseTests()
    {
        _portfolioRepoMock = new Mock<IPortfolioRepository>();
        _transactionRepoMock = new Mock<IStockTransactionRepository>();
        _stockSplitRepoMock = new Mock<IStockSplitRepository>();
        _txDateFxServiceMock = new Mock<ITransactionDateExchangeRateService>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();
        var loggerMock = new Mock<ILogger<CalculateXirrUseCase>>();
        var portfolioCalculator = new PortfolioCalculator();
        var splitAdjustmentService = new StockSplitAdjustmentService();

        currentUserServiceMock.Setup(x => x.UserId).Returns(_userId);

        _useCase = new CalculateXirrUseCase(
            _portfolioRepoMock.Object,
            _transactionRepoMock.Object,
            _stockSplitRepoMock.Object,
            portfolioCalculator,
            splitAdjustmentService,
            _txDateFxServiceMock.Object,
            currentUserServiceMock.Object,
            loggerMock.Object);
    }

    [Fact]
    public async Task ExecuteAsync_PortfolioNotFound_ThrowsEntityNotFoundException()
    {
        // Arrange
        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Portfolio?)null);

        var request = new CalculateXirrRequest();

        // Act
        Func<Task> act = async () => await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task ExecuteAsync_PortfolioBelongsToAnotherUser_ThrowsAccessDeniedException()
    {
        // Arrange
        var otherUserPortfolio = new Portfolio(Guid.NewGuid(), Guid.NewGuid(), "USD", "TWD", "Other User Portfolio");

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherUserPortfolio);

        var request = new CalculateXirrRequest();

        // Act
        Func<Task> act = async () => await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task ExecuteForPositionAsync_PortfolioNotFound_ThrowsEntityNotFoundException()
    {
        // Arrange
        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Portfolio?)null);

        var request = new CalculatePositionXirrRequest();

        // Act
        Func<Task> act = async () => await _useCase.ExecuteForPositionAsync(_portfolioId, "VT", request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<EntityNotFoundException>();
    }

    [Fact]
    public async Task ExecuteForPositionAsync_PortfolioBelongsToAnotherUser_ThrowsAccessDeniedException()
    {
        // Arrange
        var otherUserPortfolio = new Portfolio(Guid.NewGuid(), Guid.NewGuid(), "USD", "TWD", "Other User Portfolio");

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(otherUserPortfolio);

        var request = new CalculatePositionXirrRequest();

        // Act
        Func<Task> act = async () => await _useCase.ExecuteForPositionAsync(_portfolioId, "VT", request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<AccessDeniedException>();
    }

    [Fact]
    public async Task ExecuteAsync_TransactionWithExchangeRate_UsesStoredRate()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var transactions = new List<StockTransaction>
        {
            CreateTransaction("VT", TransactionType.Buy, 100, 100m, 30m, new DateTime(2024, 1, 15))
        };

        SetupMocks(portfolio, transactions);

        var asOfDate = new DateTime(2024, 12, 31);
        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VT"] = new() { Price = 110m, ExchangeRate = 32m }
            },
            AsOfDate = asOfDate
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CashFlowCount.Should().Be(2); // 1 buy + 1 final value
        result.MissingExchangeRates.Should().BeNull();
        result.AsOfDate.Should().Be(asOfDate);

        // FX service should NOT be called since transaction has stored rate
        _txDateFxServiceMock.Verify(
            x => x.GetOrFetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TaiwanStock_UsesFxRateOfOne()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var transactions = new List<StockTransaction>
        {
            CreateTaiwanTransaction("2330", TransactionType.Buy, 1000, 500m, new DateTime(2024, 1, 15))
        };

        SetupMocks(portfolio, transactions);

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["2330"] = new() { Price = 550m, ExchangeRate = 1m }
            },
            AsOfDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CashFlowCount.Should().Be(2);
        result.MissingExchangeRates.Should().BeNull();
        
        // FX service should NOT be called for Taiwan stocks (rate = 1.0)
        _txDateFxServiceMock.Verify(
            x => x.GetOrFetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_MissingExchangeRate_AutoFillsFromFxService()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var txDate = new DateTime(2024, 1, 15);
        var transactions = new List<StockTransaction>
        {
            CreateTransactionWithoutFx("VT", TransactionType.Buy, 100, 100m, txDate)
        };

        SetupMocks(portfolio, transactions);

        // Setup FX service to return a rate
        _txDateFxServiceMock
            .Setup(x => x.GetOrFetchAsync("USD", "TWD", txDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionDateExchangeRateResult
            {
                Rate = 31.5m,
                CurrencyPair = "USDTWD",
                RequestedDate = txDate,
                ActualDate = txDate,
                Source = "Stooq",
                FromCache = false
            });

        var asOfDate = new DateTime(2024, 12, 31);
        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VT"] = new() { Price = 110m, ExchangeRate = 32m }
            },
            AsOfDate = asOfDate
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CashFlowCount.Should().Be(2); // Transaction included via auto-fill
        result.MissingExchangeRates.Should().BeNull();
        
        // FX service should be called once for the missing rate
        _txDateFxServiceMock.Verify(
            x => x.GetOrFetchAsync("USD", "TWD", txDate, It.IsAny<CancellationToken>()),
            Times.Once);
        result.AsOfDate.Should().Be(asOfDate);
    }

    [Fact]
    public async Task ExecuteAsync_FxServiceReturnsNull_ReportsMissingRate()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var txDate = new DateTime(2024, 1, 15);
        var transactions = new List<StockTransaction>
        {
            CreateTransactionWithoutFx("VT", TransactionType.Buy, 100, 100m, txDate)
        };

        SetupMocks(portfolio, transactions);

        // Setup FX service to return null (rate not available)
        _txDateFxServiceMock
            .Setup(x => x.GetOrFetchAsync("USD", "TWD", txDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionDateExchangeRateResult?)null);

        var asOfDate = new DateTime(2024, 12, 31);
        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VT"] = new() { Price = 110m, ExchangeRate = 32m }
            },
            AsOfDate = asOfDate
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CashFlowCount.Should().Be(1); // Only final value, transaction skipped
        result.MissingExchangeRates.Should().NotBeNull();
        result.MissingExchangeRates.Should().HaveCount(1);
        result.MissingExchangeRates![0].TransactionDate.Should().Be(txDate);
        result.AsOfDate.Should().Be(asOfDate);
    }

    [Fact]
    public async Task ExecuteAsync_ImportInitializationAdjustmentWithHistoricalTotalCost_IncludesNegativeCashFlow()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var adjustmentDate = new DateTime(2025, 12, 31);

        var adjustment = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: adjustmentDate,
            ticker: "2330",
            transactionType: TransactionType.Adjustment,
            shares: 100m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            notes: "import-execute-adjustment",
            market: StockMarket.TW,
            currency: Currency.TWD);
        adjustment.SetImportInitialization(
            marketValueAtImport: 10000m,
            historicalTotalCost: 9800m);

        SetupMocks(portfolio, [adjustment]);

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["2330"] = new() { Price = 102m, ExchangeRate = 1m }
            },
            AsOfDate = new DateTime(2026, 1, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.CashFlowCount.Should().Be(2, "應包含期初調整成本流出與期末市值流入");
        result.Xirr.Should().NotBeNull();
        result.MissingExchangeRates.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ImportInitializationAdjustmentWithMarketValueOnly_IncludesNegativeCashFlow()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var adjustmentDate = new DateTime(2025, 12, 31);

        var adjustment = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: adjustmentDate,
            ticker: "2330",
            transactionType: TransactionType.Adjustment,
            shares: 100m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            notes: "import-execute-opening-baseline",
            market: StockMarket.TW,
            currency: Currency.TWD);
        adjustment.SetImportInitialization(
            marketValueAtImport: 10000m,
            historicalTotalCost: null);

        SetupMocks(portfolio, [adjustment]);

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["2330"] = new() { Price = 101m, ExchangeRate = 1m }
            },
            AsOfDate = new DateTime(2026, 1, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.CashFlowCount.Should().Be(2);
        result.Xirr.Should().NotBeNull();
        result.MissingExchangeRates.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_NonImportAdjustment_ShouldNotAffectCashFlows()
    {
        // Arrange
        var portfolio = CreatePortfolio();

        var adjustment = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: new DateTime(2025, 12, 31),
            ticker: "2330",
            transactionType: TransactionType.Adjustment,
            shares: 100m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            notes: "manual-adjustment",
            market: StockMarket.TW,
            currency: Currency.TWD);

        SetupMocks(portfolio, [adjustment]);

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["2330"] = new() { Price = 105m, ExchangeRate = 1m }
            },
            AsOfDate = new DateTime(2026, 1, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.CashFlowCount.Should().Be(1, "非匯入初始化 Adjustment 不應改變既有 XIRR 現金流語義");
        result.Xirr.Should().BeNull();
        result.MissingExchangeRates.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteForPositionAsync_ImportInitializationAdjustment_IncludesNegativeCashFlow()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var adjustment = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: new DateTime(2025, 12, 31),
            ticker: "2330",
            transactionType: TransactionType.Adjustment,
            shares: 100m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            notes: "import-execute-adjustment",
            market: StockMarket.TW,
            currency: Currency.TWD);
        adjustment.SetImportInitialization(
            marketValueAtImport: 10000m,
            historicalTotalCost: 9800m);

        SetupMocks(portfolio, [adjustment]);

        var request = new CalculatePositionXirrRequest
        {
            CurrentPrice = 102m,
            CurrentExchangeRate = 1m,
            AsOfDate = new DateTime(2026, 1, 31)
        };

        // Act
        var result = await _useCase.ExecuteForPositionAsync(_portfolioId, "2330", request, CancellationToken.None);

        // Assert
        result.CashFlowCount.Should().Be(2);
        result.Xirr.Should().NotBeNull();
        result.MissingExchangeRates.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldIgnoreTransactionsAfterAsOfDate_WhenBuildingCashFlowsAndHoldings()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var buyDate = new DateTime(2024, 1, 15);
        var afterAsOfSellDate = new DateTime(2025, 1, 5);
        var asOfDate = new DateTime(2024, 12, 31);

        var buy = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: buyDate,
            ticker: "VT",
            transactionType: TransactionType.Buy,
            shares: 100m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        var sellAfterAsOf = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: afterAsOfSellDate,
            ticker: "VT",
            transactionType: TransactionType.Sell,
            shares: 100m,
            pricePerShare: 120m,
            exchangeRate: 31m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        SetupMocks(portfolio, [buy, sellAfterAsOf]);

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VT"] = new() { Price = 110m, ExchangeRate = 32m }
            },
            AsOfDate = asOfDate
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.CashFlowCount.Should().Be(2, "asOfDate 之後的賣出不應參與現金流，也不應把持倉歸零");
        result.EarliestTransactionDate.Should().Be(buyDate);
        result.AsOfDate.Should().Be(asOfDate);
    }

    [Fact]
    public async Task ExecuteAsync_MissingExchangeRate_UsesTransactionCurrencyToHomeCurrency_NotHardcodedUsdTwd()
    {
        // Arrange
        var portfolio = new Portfolio(_userId, Guid.NewGuid(), "GBP", "EUR", "GBP Base / EUR Home Portfolio");
        var txDate = new DateTime(2024, 3, 15);
        var transaction = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: txDate,
            ticker: "VUSA.L",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            market: StockMarket.UK,
            currency: Currency.GBP);

        SetupMocks(portfolio, [transaction]);

        _txDateFxServiceMock
            .Setup(x => x.GetOrFetchAsync("GBP", "EUR", txDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TransactionDateExchangeRateResult
            {
                Rate = 1.17m,
                CurrencyPair = "GBPEUR",
                RequestedDate = txDate,
                ActualDate = txDate,
                Source = "Test",
                FromCache = true
            });

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VUSA.L"] = new() { Price = 102m, ExchangeRate = 1.2m }
            },
            AsOfDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.CashFlowCount.Should().Be(2);
        result.MissingExchangeRates.Should().BeNull();
        _txDateFxServiceMock.Verify(
            x => x.GetOrFetchAsync("GBP", "EUR", txDate, It.IsAny<CancellationToken>()),
            Times.Once);
        _txDateFxServiceMock.Verify(
            x => x.GetOrFetchAsync("USD", "TWD", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_TaiwanSellWithFloorAndFees_UsesNetProceedsSourceInCashFlow()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var buyDate = new DateTime(2024, 1, 15);
        var sellDate = new DateTime(2024, 6, 15);

        var buy = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: buyDate,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);

        var sell = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: sellDate,
            ticker: "2330",
            transactionType: TransactionType.Sell,
            shares: 1m,
            pricePerShare: 100.9m,
            exchangeRate: null,
            fees: 1m,
            market: StockMarket.TW,
            currency: Currency.TWD);

        SetupMocks(portfolio, [buy, sell]);

        var request = new CalculateXirrRequest
        {
            CurrentPrices = null,
            AsOfDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        // buy: -100, sell: floor(100.9)-1 = +99
        // If old formula was used, sell would be 99.9 and IRR would be notably different.
        result.Xirr.Should().NotBeNull();
        result.Xirr!.Value.Should().BeApproximately(-0.023845d, 0.000001d);
        result.Xirr!.Value.Should().NotBeApproximately(-0.002412d, 0.000001d);
        result.CashFlowCount.Should().Be(2);
        result.MissingExchangeRates.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteForPositionAsync_TaiwanSellWithFloorAndFees_UsesNetProceedsSourceInCashFlow()
    {
        // Arrange
        var portfolio = CreatePortfolio();
        var buyDate = new DateTime(2024, 1, 15);
        var sellDate = new DateTime(2024, 6, 15);

        var buy = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: buyDate,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);

        var sell = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: sellDate,
            ticker: "2330",
            transactionType: TransactionType.Sell,
            shares: 1m,
            pricePerShare: 100.9m,
            exchangeRate: null,
            fees: 1m,
            market: StockMarket.TW,
            currency: Currency.TWD);

        SetupMocks(portfolio, [buy, sell]);

        var request = new CalculatePositionXirrRequest
        {
            CurrentPrice = null,
            CurrentExchangeRate = 1m,
            AsOfDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await _useCase.ExecuteForPositionAsync(_portfolioId, "2330", request, CancellationToken.None);

        // Assert
        // buy: -100, sell: floor(100.9)-1 = +99
        // If old formula was used, sell would be 99.9 and IRR would be notably different.
        result.Xirr.Should().NotBeNull();
        result.Xirr!.Value.Should().BeApproximately(-0.023845d, 0.000001d);
        result.Xirr!.Value.Should().NotBeApproximately(-0.002412d, 0.000001d);
        result.CashFlowCount.Should().Be(2);
        result.MissingExchangeRates.Should().BeNull();
    }

    #region Helper Methods

    private Portfolio CreatePortfolio()
    {
        return new Portfolio(_userId, Guid.NewGuid(), "USD", "TWD", "Test Portfolio");
    }

    private StockTransaction CreateTransaction(
        string ticker, TransactionType type, decimal shares, decimal price, decimal fxRate, DateTime date)
    {
        return new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: date,
            ticker: ticker,
            transactionType: type,
            shares: shares,
            pricePerShare: price,
            exchangeRate: fxRate,
            fees: 0);
    }

    private StockTransaction CreateTransactionWithoutFx(
        string ticker, TransactionType type, decimal shares, decimal price, DateTime date)
    {
        return new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: date,
            ticker: ticker,
            transactionType: type,
            shares: shares,
            pricePerShare: price,
            exchangeRate: null, // No FX rate - needs auto-fill
            fees: 0);
    }

    private StockTransaction CreateTaiwanTransaction(
        string ticker, TransactionType type, decimal shares, decimal price, DateTime date)
    {
        // Taiwan stocks are detected by IsTaiwanStock property which checks if ticker is numeric
        return new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: date,
            ticker: ticker,
            transactionType: type,
            shares: shares,
            pricePerShare: price,
            exchangeRate: null, // Taiwan stocks don't need FX
            fees: 0);
    }

    private void SetupMocks(Portfolio portfolio, List<StockTransaction> transactions)
    {
        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(transactions);

        _stockSplitRepoMock
            .Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockSplit>());
    }

    #endregion
}
