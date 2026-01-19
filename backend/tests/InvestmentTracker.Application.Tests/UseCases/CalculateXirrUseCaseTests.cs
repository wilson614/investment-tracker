using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.Portfolio;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
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
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<ILogger<CalculateXirrUseCase>> _loggerMock;
    private readonly PortfolioCalculator _portfolioCalculator;
    private readonly StockSplitAdjustmentService _splitAdjustmentService;
    private readonly CalculateXirrUseCase _useCase;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _portfolioId = Guid.NewGuid();

    public CalculateXirrUseCaseTests()
    {
        _portfolioRepoMock = new Mock<IPortfolioRepository>();
        _transactionRepoMock = new Mock<IStockTransactionRepository>();
        _stockSplitRepoMock = new Mock<IStockSplitRepository>();
        _txDateFxServiceMock = new Mock<ITransactionDateExchangeRateService>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _loggerMock = new Mock<ILogger<CalculateXirrUseCase>>();
        _portfolioCalculator = new PortfolioCalculator();
        _splitAdjustmentService = new StockSplitAdjustmentService();

        _currentUserServiceMock.Setup(x => x.UserId).Returns(_userId);

        _useCase = new CalculateXirrUseCase(
            _portfolioRepoMock.Object,
            _transactionRepoMock.Object,
            _stockSplitRepoMock.Object,
            _portfolioCalculator,
            _splitAdjustmentService,
            _txDateFxServiceMock.Object,
            _currentUserServiceMock.Object,
            _loggerMock.Object);
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

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VT"] = new CurrentPriceInfo { Price = 110m, ExchangeRate = 32m }
            },
            AsOfDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CashFlowCount.Should().Be(2); // 1 buy + 1 final value
        result.MissingExchangeRates.Should().BeNull();
        
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
                ["2330"] = new CurrentPriceInfo { Price = 550m, ExchangeRate = 1m }
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

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VT"] = new CurrentPriceInfo { Price = 110m, ExchangeRate = 32m }
            },
            AsOfDate = new DateTime(2024, 12, 31)
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

        var request = new CalculateXirrRequest
        {
            CurrentPrices = new Dictionary<string, CurrentPriceInfo>
            {
                ["VT"] = new CurrentPriceInfo { Price = 110m, ExchangeRate = 32m }
            },
            AsOfDate = new DateTime(2024, 12, 31)
        };

        // Act
        var result = await _useCase.ExecuteAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.CashFlowCount.Should().Be(1); // Only final value, transaction skipped
        result.MissingExchangeRates.Should().NotBeNull();
        result.MissingExchangeRates.Should().HaveCount(1);
        result.MissingExchangeRates![0].TransactionDate.Should().Be(txDate);
    }

    #region Helper Methods

    private Portfolio CreatePortfolio()
    {
        return new Portfolio(_userId, "USD", "TWD", PortfolioType.Primary, "Test Portfolio");
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
