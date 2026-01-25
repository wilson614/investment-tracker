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
            _portfolioCalculator,
            _currentUserServiceMock.Object,
            _historicalYearEndDataServiceMock.Object,
            _txSnapshotServiceMock.Object,
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
