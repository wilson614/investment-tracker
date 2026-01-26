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
