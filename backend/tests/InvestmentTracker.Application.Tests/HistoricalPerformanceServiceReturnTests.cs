using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.Services;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Application.Tests;

public class HistoricalPerformanceServiceReturnTests
{
    private readonly Mock<IPortfolioRepository> _portfolioRepoMock = new();
    private readonly Mock<IStockTransactionRepository> _transactionRepoMock = new();
    private readonly Mock<ICurrentUserService> _currentUserServiceMock = new();
    private readonly Mock<IHistoricalYearEndDataService> _historicalYearEndDataServiceMock = new();
    private readonly Mock<ITransactionPortfolioSnapshotService> _txSnapshotServiceMock = new();
    private readonly Mock<ITransactionDateExchangeRateService> _txDateFxServiceMock = new();
    private readonly Mock<ICurrencyLedgerRepository> _currencyLedgerRepoMock = new();
    private readonly ReturnCashFlowStrategyProvider _cashFlowStrategyProvider = new(
        new StockTransactionCashFlowStrategy(),
        new CurrencyLedgerCashFlowStrategy());
    private readonly Mock<ILogger<HistoricalPerformanceService>> _loggerMock = new();

    private readonly PortfolioCalculator _portfolioCalculator = new();
    private readonly IReturnCalculator _returnCalculator = new ReturnCalculator();

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _portfolioId = Guid.NewGuid();

    private readonly HistoricalPerformanceService _service;

    public HistoricalPerformanceServiceReturnTests()
    {
        _currentUserServiceMock
            .Setup(x => x.UserId)
            .Returns(_userId);

        _service = new HistoricalPerformanceService(
            _portfolioRepoMock.Object,
            _transactionRepoMock.Object,
            _currencyLedgerRepoMock.Object,
            _portfolioCalculator,
            _currentUserServiceMock.Object,
            _historicalYearEndDataServiceMock.Object,
            _txDateFxServiceMock.Object,
            _txSnapshotServiceMock.Object,
            _cashFlowStrategyProvider,
            _returnCalculator,
            _loggerMock.Object);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_SingleBuyOnYearStart_ModifiedDietzEqualsTwr()
    {
        // Arrange
        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "TWD",
            displayName: "Test Portfolio");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buy = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: yearStart,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.UK);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buy]);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: buy.Id,
                snapshotDate: yearStart,
                beforeSource: 0m,
                afterSource: 1000m,
                fxRate: 30m,
                createdAt: new DateTime(year, 1, 1, 1, 0, 0, DateTimeKind.Utc))
        };

        _txSnapshotServiceMock
            .Setup(x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock
            .Setup(x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["VWRA"] = new() { Price = 110m, ExchangeRate = 30m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        result.ModifiedDietzPercentageSource.Should().BeApproximately(10d, 0.0001d);
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(10d, 0.0001d);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_TwoBuys_ModifiedDietzDiffersFromTwr()
    {
        // Arrange
        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var midYear = new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "TWD",
            displayName: "Test Portfolio");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buy1 = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: yearStart,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.UK);

        var buy2 = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: midYear,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.UK);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buy1, buy2]);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: buy1.Id,
                snapshotDate: yearStart,
                beforeSource: 0m,
                afterSource: 1000m,
                fxRate: 30m,
                createdAt: new DateTime(year, 1, 1, 1, 0, 0, DateTimeKind.Utc)),
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: buy2.Id,
                snapshotDate: midYear,
                beforeSource: 1000m,
                afterSource: 2000m,
                fxRate: 30m,
                createdAt: new DateTime(year, 7, 1, 1, 0, 0, DateTimeKind.Utc))
        };

        _txSnapshotServiceMock
            .Setup(x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock
            .Setup(x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["VWRA"] = new() { Price = 110m, ExchangeRate = 30m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        // TWR should only reflect market movement between cash flow events.
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(10d, 0.0001d);

        // Modified Dietz should reflect cash flow timing (same end value, different weights).
        var periodStart = new DateTime(year, 1, 1);
        var periodEnd = new DateTime(year, 12, 31);
        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (midYear.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        var expectedDietz = 200m / (1000m + 1000m * weight);
        var expectedDietzPct = (double)(expectedDietz * 100m);

        result.ModifiedDietzPercentageSource.Should().BeApproximately(expectedDietzPct, 0.0001d);
        (result.ModifiedDietzPercentageSource!.Value - result.TimeWeightedReturnPercentageSource!.Value)
            .Should().BeGreaterThan(1d);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_TwoBuysSameDay_SecondSnapshotNeutralized_TwrNotDoubleCounted()
    {
        // Arrange
        const int year = 2025;
        var eventDate = new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "TWD",
            displayName: "Test Portfolio");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buy1 = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: eventDate,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.UK);
        buy1.CreatedAt = new DateTime(year, 7, 1, 1, 0, 0, DateTimeKind.Utc);

        var buy2 = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: eventDate,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.UK);
        buy2.CreatedAt = new DateTime(year, 7, 1, 2, 0, 0, DateTimeKind.Utc);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buy1, buy2]);

        // Snapshots mimic the production behavior for same-day multiple transactions:
        // - First snapshot captures the day movement once (before=dayStart, after=dayEnd)
        // - Subsequent snapshots are neutralized (before==after==dayEnd) to avoid duplicate TWR factors
        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: buy1.Id,
                snapshotDate: eventDate,
                beforeSource: 0m,
                afterSource: 2000m,
                fxRate: 30m,
                createdAt: new DateTime(year, 7, 1, 3, 0, 0, DateTimeKind.Utc)),
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: buy2.Id,
                snapshotDate: eventDate,
                beforeSource: 2000m,
                afterSource: 2000m,
                fxRate: 30m,
                createdAt: new DateTime(year, 7, 1, 4, 0, 0, DateTimeKind.Utc))
        };

        _txSnapshotServiceMock
            .Setup(x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock
            .Setup(x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["VWRA"] = new() { Price = 110m, ExchangeRate = 30m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        result.StartValueSource.Should().BeNull();
        result.EndValueSource.Should().Be(2200m);
        result.StartValueHome.Should().BeNull();
        result.EndValueHome.Should().Be(66000m);

        var expectedTwr = (2200m / 2000m) - 1m;
        var expectedTwrPct = (double)(expectedTwr * 100m);
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(expectedTwrPct, 0.0001d);
        result.TimeWeightedReturnPercentage.Should().BeApproximately(expectedTwrPct, 0.0001d);

        var periodStart = new DateTime(year, 1, 1);
        var periodEnd = new DateTime(year, 12, 31);
        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (eventDate.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        var expectedDietz = 200m / (2000m * weight);
        var expectedDietzPct = (double)(expectedDietz * 100m);

        result.ModifiedDietzPercentageSource.Should().BeApproximately(expectedDietzPct, 0.0001d);
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedDietzPct, 0.0001d);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_SellWithFees_UsesNegativeCashFlowAndComputesExpectedRates()
    {
        // Arrange
        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var priceReferenceDate = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var sellDate = new DateTime(year, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "TWD",
            displayName: "Test Portfolio");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buyBeforeYear = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: priceReferenceDate,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.UK);
        buyBeforeYear.CreatedAt = new DateTime(year - 1, 12, 31, 1, 0, 0, DateTimeKind.Utc);

        var sell = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: sellDate,
            ticker: "VWRA",
            transactionType: TransactionType.Sell,
            shares: 2m,
            pricePerShare: 120m,
            exchangeRate: 30m,
            fees: 1m,
            market: StockMarket.UK);
        sell.CreatedAt = new DateTime(year, 6, 30, 1, 0, 0, DateTimeKind.Utc);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buyBeforeYear, sell]);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: sell.Id,
                snapshotDate: sellDate,
                beforeSource: 1200m,
                afterSource: 960m,
                fxRate: 30m,
                createdAt: new DateTime(year, 6, 30, 2, 0, 0, DateTimeKind.Utc))
        };

        _txSnapshotServiceMock
            .Setup(x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock
            .Setup(x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearStartPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["VWRA"] = new() { Price = 100m, ExchangeRate = 30m }
            },
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["VWRA"] = new() { Price = 110m, ExchangeRate = 30m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        result.StartValueSource.Should().Be(1000m);
        result.EndValueSource.Should().Be(880m);
        result.StartValueHome.Should().Be(30000m);
        result.EndValueHome.Should().Be(26400m);

        var expectedTwr = (1200m / 1000m) * (880m / 960m) - 1m;
        var expectedTwrPct = (double)(expectedTwr * 100m);
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(expectedTwrPct, 0.0001d);
        result.TimeWeightedReturnPercentage.Should().BeApproximately(expectedTwrPct, 0.0001d);

        var proceeds = 2m * 120m - 1m;
        var sellCashFlow = -proceeds;

        var periodStart = yearStart;
        var periodEnd = new DateTime(year, 12, 31);
        var totalDays = (periodEnd.Date - periodStart.Date).Days;
        var daysSinceStart = (sellDate.Date - periodStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        var expectedDietz = (880m - 1000m - sellCashFlow) / (1000m + sellCashFlow * weight);
        var expectedDietzPct = (double)(expectedDietz * 100m);

        result.ModifiedDietzPercentageSource.Should().BeApproximately(expectedDietzPct, 0.0001d);
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedDietzPct, 0.0001d);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_WithBoundLedgerExternalDeposit_UsesUnifiedClosedLoopBaselineForMdAndTwr()
    {
        // Arrange
        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var priceReferenceDate = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var depositDate = new DateTime(year, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "USD",
            displayName: "Closed-loop baseline test portfolio");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buyBeforeYear = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: priceReferenceDate,
            ticker: "AAPL",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buyBeforeYear]);

        var boundLedger = new CurrencyLedger(_userId, "USD", "USD Ledger", "USD");
        typeof(CurrencyLedger)
            .BaseType!
            .GetProperty(nameof(CurrencyLedger.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(boundLedger, [portfolio.BoundCurrencyLedgerId]);

        var initialBalance = new CurrencyTransaction(
            boundLedger.Id,
            priceReferenceDate,
            CurrencyTransactionType.InitialBalance,
            500m,
            homeAmount: 500m,
            exchangeRate: 1m);

        var externalDeposit = new CurrencyTransaction(
            boundLedger.Id,
            depositDate,
            CurrencyTransactionType.Deposit,
            100m,
            homeAmount: 100m,
            exchangeRate: 1m);

        boundLedger.AddTransaction(initialBalance);
        boundLedger.AddTransaction(externalDeposit);

        _currencyLedgerRepoMock
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boundLedger);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: externalDeposit.Id,
                snapshotDate: depositDate,
                beforeSource: 1500m,
                afterSource: 1600m,
                fxRate: 1m,
                createdAt: new DateTime(year, 6, 30, 1, 0, 0, DateTimeKind.Utc))
        };

        _txSnapshotServiceMock
            .Setup(x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock
            .Setup(x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearStartPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 100m, ExchangeRate = 1m }
            },
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["AAPL"] = new() { Price = 110m, ExchangeRate = 1m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        result.StartValueSource.Should().Be(1500m);
        result.EndValueSource.Should().Be(1700m);
        result.StartValueHome.Should().Be(1500m);
        result.EndValueHome.Should().Be(1700m);

        var expectedTwr = (1500m / 1500m) * (1700m / 1600m) - 1m;
        var expectedTwrPct = (double)(expectedTwr * 100m);
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(expectedTwrPct, 0.0001d);
        result.TimeWeightedReturnPercentage.Should().BeApproximately(expectedTwrPct, 0.0001d);

        var periodEnd = new DateTime(year, 12, 31);
        var totalDays = (periodEnd.Date - yearStart.Date).Days;
        var daysSinceStart = (depositDate.Date - yearStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        var expectedDietz = (1700m - 1500m - 100m) / (1500m + 100m * weight);
        var expectedDietzPct = (double)(expectedDietz * 100m);

        result.ModifiedDietzPercentageSource.Should().BeApproximately(expectedDietzPct, 0.0001d);
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedDietzPct, 0.0001d);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_WithInternalFxTransferEffects_ExcludesThemFromMdAndTwrExternalCashFlowInputs()
    {
        // Arrange
        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var priceReferenceDate = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var depositDate = new DateTime(year, 6, 30, 0, 0, 0, DateTimeKind.Utc);
        var relatedStockId = Guid.NewGuid();

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "USD",
            displayName: "Internal FX exclusion test portfolio");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var boundLedger = new CurrencyLedger(_userId, "USD", "USD Ledger", "USD");
        typeof(CurrencyLedger)
            .BaseType!
            .GetProperty(nameof(CurrencyLedger.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(boundLedger, [portfolio.BoundCurrencyLedgerId]);

        var initialBalance = new CurrencyTransaction(
            boundLedger.Id,
            priceReferenceDate,
            CurrencyTransactionType.InitialBalance,
            1000m,
            homeAmount: 1000m,
            exchangeRate: 1m);

        var externalDeposit = new CurrencyTransaction(
            boundLedger.Id,
            depositDate,
            CurrencyTransactionType.Deposit,
            200m,
            homeAmount: 200m,
            exchangeRate: 1m);

        var internalExchangeBuy = new CurrencyTransaction(
            boundLedger.Id,
            new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            CurrencyTransactionType.ExchangeBuy,
            50m,
            homeAmount: 50m,
            exchangeRate: 1m,
            relatedStockTransactionId: relatedStockId,
            notes: "internal fx transfer effect");

        var internalExchangeSell = new CurrencyTransaction(
            boundLedger.Id,
            new DateTime(year, 7, 2, 0, 0, 0, DateTimeKind.Utc),
            CurrencyTransactionType.ExchangeSell,
            30m,
            homeAmount: 30m,
            exchangeRate: 1m,
            relatedStockTransactionId: relatedStockId,
            notes: "internal fx transfer effect");

        var internalOtherIncome = new CurrencyTransaction(
            boundLedger.Id,
            new DateTime(year, 7, 3, 0, 0, 0, DateTimeKind.Utc),
            CurrencyTransactionType.OtherIncome,
            40m,
            homeAmount: 40m,
            exchangeRate: 1m,
            relatedStockTransactionId: relatedStockId,
            notes: "sell-linked fallback");

        boundLedger.AddTransaction(initialBalance);
        boundLedger.AddTransaction(externalDeposit);
        boundLedger.AddTransaction(internalExchangeBuy);
        boundLedger.AddTransaction(internalExchangeSell);
        boundLedger.AddTransaction(internalOtherIncome);

        _currencyLedgerRepoMock
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boundLedger);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            // External CF event snapshot (should be used)
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: externalDeposit.Id,
                snapshotDate: depositDate,
                beforeSource: 1000m,
                afterSource: 1200m,
                fxRate: 1m,
                createdAt: new DateTime(year, 6, 30, 1, 0, 0, DateTimeKind.Utc)),

            // Internal FX-effect snapshots (should be ignored as external CF inputs)
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: internalExchangeBuy.Id,
                snapshotDate: new DateTime(year, 7, 1, 0, 0, 0, DateTimeKind.Utc),
                beforeSource: 1200m,
                afterSource: 1250m,
                fxRate: 1m,
                createdAt: new DateTime(year, 7, 1, 1, 0, 0, DateTimeKind.Utc)),
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: internalExchangeSell.Id,
                snapshotDate: new DateTime(year, 7, 2, 0, 0, 0, DateTimeKind.Utc),
                beforeSource: 1250m,
                afterSource: 1220m,
                fxRate: 1m,
                createdAt: new DateTime(year, 7, 2, 1, 0, 0, DateTimeKind.Utc)),
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: internalOtherIncome.Id,
                snapshotDate: new DateTime(year, 7, 3, 0, 0, 0, DateTimeKind.Utc),
                beforeSource: 1220m,
                afterSource: 1260m,
                fxRate: 1m,
                createdAt: new DateTime(year, 7, 3, 1, 0, 0, DateTimeKind.Utc))
        };

        _txSnapshotServiceMock
            .Setup(x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock
            .Setup(x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshots);

        var request = new CalculateYearPerformanceRequest
        {
            Year = year,
            YearStartPrices = new Dictionary<string, YearEndPriceInfo>(),
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>()
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        // Closed-loop valuation includes all ledger effects; no flooring/clamping.
        result.StartValueSource.Should().Be(1000m);
        result.EndValueSource.Should().Be(1260m);
        result.StartValueHome.Should().Be(1000m);
        result.EndValueHome.Should().Be(1260m);

        // Only external deposit contributes to MD/TWR cash-flow inputs.
        var expectedTwr = (1000m / 1000m) * (1260m / 1200m) - 1m;
        var expectedTwrPct = (double)(expectedTwr * 100m);
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(expectedTwrPct, 0.0001d);
        result.TimeWeightedReturnPercentage.Should().BeApproximately(expectedTwrPct, 0.0001d);

        var periodEnd = new DateTime(year, 12, 31);
        var totalDays = (periodEnd.Date - yearStart.Date).Days;
        var daysSinceStart = (depositDate.Date - yearStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        var expectedDietz = (1260m - 1000m - 200m) / (1000m + 200m * weight);
        var expectedDietzPct = (double)(expectedDietz * 100m);
        result.ModifiedDietzPercentageSource.Should().BeApproximately(expectedDietzPct, 0.0001d);
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedDietzPct, 0.0001d);

        // Regression guard: if internal FX effects were wrongly included as external CF,
        // both MD and TWR in this fixture would collapse near 0%.
        result.TimeWeightedReturnPercentageSource.Should().NotBeApproximately(0d, 0.0001d);
        result.ModifiedDietzPercentageSource.Should().NotBeApproximately(0d, 0.0001d);
    }

    private static TransactionPortfolioSnapshot CreateSnapshot(
        Guid portfolioId,
        Guid transactionId,
        DateTime snapshotDate,
        decimal beforeSource,
        decimal afterSource,
        decimal fxRate,
        DateTime createdAt)
    {
        var snapshot = new TransactionPortfolioSnapshot(
            portfolioId,
            transactionId,
            snapshotDate,
            portfolioValueBeforeHome: beforeSource * fxRate,
            portfolioValueAfterHome: afterSource * fxRate,
            portfolioValueBeforeSource: beforeSource,
            portfolioValueAfterSource: afterSource);

        snapshot.CreatedAt = createdAt;
        snapshot.UpdatedAt = createdAt;

        return snapshot;
    }
}
