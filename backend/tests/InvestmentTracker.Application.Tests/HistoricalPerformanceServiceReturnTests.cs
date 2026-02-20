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

        _txSnapshotServiceMock.Verify(
            x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _txSnapshotServiceMock.Verify(
            x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _historicalYearEndDataServiceMock.Verify(
            x => x.GetOrFetchYearEndPriceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<StockMarket?>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _historicalYearEndDataServiceMock.Verify(
            x => x.GetOrFetchYearEndExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

        result.HasOpeningBaseline.Should().BeFalse();
        result.UsesPartialHistoryAssumption.Should().BeTrue();
        result.CoverageStartDate.Should().Be(eventDate.Date);
        result.CoverageDays.Should().BeGreaterThan(0);
        result.XirrReliability.Should().Be("Unavailable");
        result.XirrSource.Should().BeNull();
        result.XirrPercentageSource.Should().BeNull();
        result.Xirr.Should().BeNull();
        result.XirrPercentage.Should().BeNull();

        result.StartValueSource.Should().Be(0m);
        result.EndValueSource.Should().Be(2200m);
        result.StartValueHome.Should().Be(0m);
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
    public async Task CalculateYearPerformanceAsync_NoOpeningBaseline_WithLateTopUpOnly_CanReachExtremeModifiedDietz()
    {
        // Arrange
        const int year = 2025;
        var tradeDate = new DateTime(year, 12, 30, 0, 0, 0, DateTimeKind.Utc);
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var yearEnd = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        const decimal topUpAmount = 100000m;
        const decimal yearEndPrice = 105.68684m;

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "TWD",
            homeCurrency: "TWD",
            displayName: "Late-topup near-zero Dietz denominator test");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buy = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: tradeDate,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1000m,
            pricePerShare: 100m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        buy.CreatedAt = new DateTime(year, 12, 30, 1, 0, 0, DateTimeKind.Utc);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buy]);

        var boundLedger = new CurrencyLedger(_userId, "TWD", "TWD Ledger", "TWD");
        typeof(CurrencyLedger)
            .BaseType!
            .GetProperty(nameof(CurrencyLedger.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(boundLedger, [portfolio.BoundCurrencyLedgerId]);

        var topUpDeposit = new CurrencyTransaction(
            boundLedger.Id,
            tradeDate,
            CurrencyTransactionType.Deposit,
            topUpAmount,
            homeAmount: topUpAmount,
            exchangeRate: 1m,
            relatedStockTransactionId: buy.Id,
            notes: "補足買入 2330 差額");

        var buySpend = new CurrencyTransaction(
            boundLedger.Id,
            tradeDate,
            CurrencyTransactionType.Spend,
            topUpAmount,
            homeAmount: topUpAmount,
            exchangeRate: 1m,
            relatedStockTransactionId: buy.Id,
            notes: "買入 2330 × 1000");

        boundLedger.AddTransaction(topUpDeposit);
        boundLedger.AddTransaction(buySpend);

        _currencyLedgerRepoMock
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boundLedger);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            // Ledger strategy only keeps the top-up deposit as external cash flow input.
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: topUpDeposit.Id,
                snapshotDate: tradeDate,
                beforeSource: 0m,
                afterSource: topUpAmount,
                fxRate: 1m,
                createdAt: new DateTime(year, 12, 30, 2, 0, 0, DateTimeKind.Utc))
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
                ["2330"] = new() { Price = yearEndPrice, ExchangeRate = 1m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        result.HasOpeningBaseline.Should().BeFalse();
        result.UsesPartialHistoryAssumption.Should().BeTrue();
        result.CoverageStartDate.Should().Be(tradeDate.Date);
        result.CoverageDays.Should().Be(2);
        result.XirrReliability.Should().Be("Unavailable");
        result.ShouldDegradeReturnDisplay.Should().BeTrue();
        result.ReturnDisplayDegradeReasonCode.Should().Be("LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE");
        result.ReturnDisplayDegradeReasonMessage.Should().NotBeNullOrWhiteSpace();
        result.Xirr.Should().BeNull();
        result.XirrSource.Should().BeNull();

        result.StartValueSource.Should().Be(0m);
        result.EndValueSource.Should().Be(105686.84m);
        result.NetContributionsSource.Should().Be(topUpAmount);
        result.StartValueHome.Should().Be(0m);
        result.EndValueHome.Should().Be(105686.84m);
        result.NetContributionsHome.Should().Be(topUpAmount);

        var totalDays = (yearEnd.Date - yearStart.Date).Days;
        var daysSinceStart = (tradeDate.Date - yearStart.Date).Days;
        var weight = (totalDays - daysSinceStart) / (decimal)totalDays;

        // Root cause lock: denominator = startValue + weighted external cash flow = 0 + 100000 * (1/364)
        // The weighted denominator is tiny because only a very-late top-up is treated as external cash flow.
        var expectedDenominator = topUpAmount * weight;
        var expectedNumerator = result.EndValueSource!.Value - result.StartValueSource!.Value - result.NetContributionsSource!.Value;
        var expectedDietzPct = (double)((expectedNumerator / expectedDenominator) * 100m);

        totalDays.Should().Be(364);
        daysSinceStart.Should().Be(363);
        weight.Should().BeApproximately(1m / 364m, 0.0000001m);
        expectedDenominator.Should().BeApproximately(274.7252747m, 0.0001m);
        expectedDenominator.Should().BeLessThan(300m);
        expectedNumerator.Should().Be(5686.84m);

        result.ModifiedDietzPercentageSource.Should().BeApproximately(expectedDietzPct, 0.0001d);
        result.ModifiedDietzPercentage.Should().BeApproximately(expectedDietzPct, 0.0001d);
        result.ModifiedDietzPercentageSource.Should().BeApproximately(2070.01d, 0.1d);
        result.ModifiedDietzPercentageSource.Should().BeGreaterThan(1000d);

        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(5.68684d, 0.0001d);
        result.TimeWeightedReturnPercentage.Should().BeApproximately(5.68684d, 0.0001d);
        (result.ModifiedDietzPercentageSource!.Value - result.TimeWeightedReturnPercentageSource!.Value)
            .Should().BeGreaterThan(2000d);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_NoOpeningBaselineWithSufficientCoverage_DegradesWithNoOpeningBaselineReason()
    {
        // Arrange
        const int year = 2025;
        var tradeDate = new DateTime(year, 10, 3, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "TWD",
            homeCurrency: "TWD",
            displayName: "No baseline only degrade reason test");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buy = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: tradeDate,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1000m,
            pricePerShare: 100m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        buy.CreatedAt = new DateTime(year, 10, 3, 1, 0, 0, DateTimeKind.Utc);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buy]);

        _currencyLedgerRepoMock
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: buy.Id,
                snapshotDate: tradeDate,
                beforeSource: 0m,
                afterSource: 100000m,
                fxRate: 1m,
                createdAt: new DateTime(year, 10, 3, 2, 0, 0, DateTimeKind.Utc))
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
                ["2330"] = new() { Price = 105.68684m, ExchangeRate = 1m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        result.HasOpeningBaseline.Should().BeFalse();
        result.CoverageStartDate.Should().Be(tradeDate.Date);
        result.CoverageDays.Should().Be(90);

        result.ShouldDegradeReturnDisplay.Should().BeTrue();
        result.ReturnDisplayDegradeReasonCode.Should().Be("LOW_CONFIDENCE_NO_OPENING_BASELINE");
        result.ReturnDisplayDegradeReasonCode.Should().NotBe("LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE");
        result.ReturnDisplayDegradeReasonMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResolveReturnDisplayDegradeSignal_WithOpeningBaselineAndLowCoverage_ReturnsLowCoverageReason()
    {
        // Arrange
        var method = typeof(HistoricalPerformanceService).GetMethod(
            "ResolveReturnDisplayDegradeSignal",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        // Act
        var signal = method!.Invoke(null, ["Unavailable", true, 17]);
        var signalType = signal!.GetType();
        var shouldDegrade = (bool)signalType.GetProperty("ShouldDegrade")!.GetValue(signal)!;
        var reasonCode = (string?)signalType.GetProperty("ReasonCode")!.GetValue(signal);

        // Assert
        shouldDegrade.Should().BeTrue();
        reasonCode.Should().Be("LOW_CONFIDENCE_LOW_COVERAGE");
        reasonCode.Should().NotBe("LOW_CONFIDENCE_NO_OPENING_BASELINE_AND_LOW_COVERAGE");
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

        result.HasOpeningBaseline.Should().BeTrue();
        result.UsesPartialHistoryAssumption.Should().BeFalse();
        result.CoverageStartDate.Should().Be(yearStart.Date);
        result.CoverageDays.Should().Be((new DateTime(year, 12, 31) - yearStart.Date).Days + 1);
        result.XirrReliability.Should().Be("High");

        result.StartValueSource.Should().Be(1000m);
        result.EndValueSource.Should().Be(880m);
        result.StartValueHome.Should().Be(30000m);
        result.EndValueHome.Should().Be(26400m);

        result.NetContributionsSource.Should().Be(-239m);
        result.NetContributionsHome.Should().Be(-7170m);
        result.TotalReturnPercentageSource.Should().BeApproximately(11.9d, 0.0001d);
        result.TotalReturnPercentage.Should().BeApproximately(11.9d, 0.0001d);

        var expectedTwr = (1200m / 1000m) * (880m / 960m) - 1m;
        var expectedTwrPct = (double)(expectedTwr * 100m);
        result.TimeWeightedReturnPercentageSource.Should().BeApproximately(expectedTwrPct, 0.0001d);
        result.TimeWeightedReturnPercentage.Should().BeApproximately(expectedTwrPct, 0.0001d);

        var proceeds = sell.NetProceedsSource;
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
    public async Task CalculateYearPerformanceAsync_TaiwanSellWithFloorAndFees_ReflectsNetProceedsInHomeAndSource()
    {
        // Arrange
        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var priceReferenceDate = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        var sellDate = new DateTime(year, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "TWD",
            homeCurrency: "TWD",
            displayName: "TW floor/fees sell cashflow test");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buyBeforeYear = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: priceReferenceDate,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        buyBeforeYear.CreatedAt = new DateTime(year - 1, 12, 31, 1, 0, 0, DateTimeKind.Utc);

        var sell = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: sellDate,
            ticker: "2330",
            transactionType: TransactionType.Sell,
            shares: 1m,
            pricePerShare: 100.9m,
            exchangeRate: 1m,
            fees: 1m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        sell.CreatedAt = new DateTime(year, 6, 30, 1, 0, 0, DateTimeKind.Utc);

        _portfolioRepoMock
            .Setup(x => x.GetByIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        _transactionRepoMock
            .Setup(x => x.GetByPortfolioIdAsync(_portfolioId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buyBeforeYear, sell]);

        _currencyLedgerRepoMock
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var snapshots = new List<TransactionPortfolioSnapshot>
        {
            CreateSnapshot(
                portfolioId: _portfolioId,
                transactionId: sell.Id,
                snapshotDate: sellDate,
                beforeSource: 101m,
                afterSource: 0m,
                fxRate: 1m,
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
                ["2330"] = new() { Price = 101m, ExchangeRate = 1m }
            },
            YearEndPrices = new Dictionary<string, YearEndPriceInfo>
            {
                ["2330"] = new() { Price = 0m, ExchangeRate = 1m }
            }
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();
        result.StartValueSource.Should().Be(101m);
        result.EndValueSource.Should().Be(0m);
        result.StartValueHome.Should().Be(101m);
        result.EndValueHome.Should().Be(0m);

        // floor(1 * 100.9)=100, then minus fee 1 => 99
        sell.NetProceedsSource.Should().Be(99m);
        result.NetContributionsSource.Should().Be(-99m);
        result.NetContributionsHome.Should().Be(-99m);
        result.TotalReturnPercentageSource.Should().BeApproximately(-1.9801980198d, 0.0001d);
        result.TotalReturnPercentage.Should().BeApproximately(-1.9801980198d, 0.0001d);

        // Without floor this would be -99.9; this assertion guards the floor+fees path in annual sell cashflow.
        result.NetContributionsSource.Should().NotBe(-99.9m);
    }

    [Fact]
    public async Task CalculateYearPerformanceAsync_AutoFetchSameTickerForYearStartAndYearEnd_DeduplicatesPriceAndFxCalls()
    {
        // Arrange
        const int year = 2025;
        var yearStart = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var previousYearEnd = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc);

        var portfolio = new Portfolio(
            userId: _userId,
            boundCurrencyLedgerId: Guid.NewGuid(),
            baseCurrency: "USD",
            homeCurrency: "TWD",
            displayName: "Auto fetch dedup test");

        typeof(Portfolio)
            .BaseType!
            .GetProperty(nameof(Portfolio.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(portfolio, [_portfolioId]);

        var buyBeforeYear = new StockTransaction(
            portfolioId: _portfolioId,
            transactionDate: previousYearEnd,
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
            .ReturnsAsync([buyBeforeYear]);

        _currencyLedgerRepoMock
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        _txSnapshotServiceMock
            .Setup(x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _txSnapshotServiceMock
            .Setup(x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionPortfolioSnapshot>());

        _historicalYearEndDataServiceMock
            .Setup(x => x.GetOrFetchYearEndPriceAsync("VWRA", year, StockMarket.UK, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearEndPriceResult
            {
                Price = 110m,
                Currency = "GBP",
                ActualDate = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                Source = "Yahoo",
                FromCache = false
            });

        _historicalYearEndDataServiceMock
            .Setup(x => x.GetOrFetchYearEndPriceAsync("VWRA", year - 1, StockMarket.UK, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearEndPriceResult
            {
                Price = 100m,
                Currency = "GBP",
                ActualDate = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                Source = "Yahoo",
                FromCache = false
            });

        _historicalYearEndDataServiceMock
            .Setup(x => x.GetOrFetchYearEndExchangeRateAsync("USD", "TWD", year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearEndExchangeRateResult
            {
                Rate = 31m,
                CurrencyPair = "USDTWD",
                ActualDate = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                Source = "Stooq",
                FromCache = false
            });

        _historicalYearEndDataServiceMock
            .Setup(x => x.GetOrFetchYearEndExchangeRateAsync("USD", "TWD", year - 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YearEndExchangeRateResult
            {
                Rate = 30m,
                CurrencyPair = "USDTWD",
                ActualDate = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                Source = "Stooq",
                FromCache = false
            });

        var request = new CalculateYearPerformanceRequest
        {
            Year = year
            // No YearStartPrices / YearEndPrices => force auto-fetch
        };

        // Act
        var result = await _service.CalculateYearPerformanceAsync(_portfolioId, request, CancellationToken.None);

        // Assert
        result.MissingPrices.Should().BeEmpty();
        result.IsComplete.Should().BeTrue();

        _historicalYearEndDataServiceMock.Verify(
            x => x.GetOrFetchYearEndPriceAsync("VWRA", year, StockMarket.UK, It.IsAny<CancellationToken>()),
            Times.Once);
        _historicalYearEndDataServiceMock.Verify(
            x => x.GetOrFetchYearEndPriceAsync("VWRA", year - 1, StockMarket.UK, It.IsAny<CancellationToken>()),
            Times.Once);

        _historicalYearEndDataServiceMock.Verify(
            x => x.GetOrFetchYearEndExchangeRateAsync("USD", "TWD", year, It.IsAny<CancellationToken>()),
            Times.Once);
        _historicalYearEndDataServiceMock.Verify(
            x => x.GetOrFetchYearEndExchangeRateAsync("USD", "TWD", year - 1, It.IsAny<CancellationToken>()),
            Times.Once);

        _txSnapshotServiceMock.Verify(
            x => x.BackfillSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _txSnapshotServiceMock.Verify(
            x => x.GetSnapshotsAsync(_portfolioId, It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
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
        result.NetContributionsSource.Should().Be(100m);
        result.NetContributionsHome.Should().Be(100m);
        result.TotalReturnPercentageSource.Should().BeApproximately(6.6666666667d, 0.0001d);
        result.TotalReturnPercentage.Should().BeApproximately(6.6666666667d, 0.0001d);

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
