using FluentAssertions;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class TransactionPortfolioSnapshotServiceTests
{
    [Fact]
    public async Task UpsertSnapshotAsync_SameDayMultipleStockTransactions_SecondSnapshotIsChainedToDayEnd()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepository = new Mock<IPortfolioRepository>();
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var date = new DateTime(2021, 1, 3, 0, 0, 0, DateTimeKind.Utc);

        var tx1 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.UK);
        tx1.CreatedAt = new DateTime(2021, 1, 3, 1, 0, 0, DateTimeKind.Utc);

        var tx2 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "0050",
            transactionType: TransactionType.Sell,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW);
        tx2.CreatedAt = new DateTime(2021, 1, 3, 2, 0, 0, DateTimeKind.Utc);

        var transactionRepository = new Mock<IStockTransactionRepository>();
        transactionRepository
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => id == tx1.Id ? tx1 : id == tx2.Id ? tx2 : null);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx1, tx2]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = new TransactionPortfolioSnapshotService(
            dbContext,
            portfolioRepository.Object,
            transactionRepository.Object,
            currencyLedgerRepository.Object,
            new CurrencyLedgerService(),
            new PortfolioCalculator(),
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Seed buggy snapshots (both have the same before/after -> not chained)
        var snapshotDate = new DateTime(2021, 1, 3, 0, 0, 0, DateTimeKind.Utc);
        var dayStartHome = 1000m;
        var dayEndHome = 2000m;
        var dayStartSource = 100m;
        var dayEndSource = 200m;

        var s1 = new TransactionPortfolioSnapshot(
            portfolioId: portfolio.Id,
            transactionId: tx1.Id,
            snapshotDate: snapshotDate,
            portfolioValueBeforeHome: dayStartHome,
            portfolioValueAfterHome: dayEndHome,
            portfolioValueBeforeSource: dayStartSource,
            portfolioValueAfterSource: dayEndSource);

        var s2 = new TransactionPortfolioSnapshot(
            portfolioId: portfolio.Id,
            transactionId: tx2.Id,
            snapshotDate: snapshotDate,
            portfolioValueBeforeHome: dayStartHome,
            portfolioValueAfterHome: dayEndHome,
            portfolioValueBeforeSource: dayStartSource,
            portfolioValueAfterSource: dayEndSource);

        dbContext.TransactionPortfolioSnapshots.AddRange(s1, s2);
        await dbContext.SaveChangesAsync();

        // Act
        await sut.UpsertSnapshotAsync(portfolio.Id, tx2.Id, tx2.TransactionDate, CancellationToken.None);

        // Assert
        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().HaveCount(2);

        var updated1 = snapshots.Single(s => s.TransactionId == tx1.Id);
        var updated2 = snapshots.Single(s => s.TransactionId == tx2.Id);

        updated1.PortfolioValueBeforeHome.Should().Be(dayStartHome);
        updated1.PortfolioValueAfterHome.Should().Be(dayEndHome);
        updated1.PortfolioValueBeforeSource.Should().Be(dayStartSource);
        updated1.PortfolioValueAfterSource.Should().Be(dayEndSource);

        // Second snapshot should be neutralized (before==after==dayEnd) to avoid duplicate TWR factor
        updated2.PortfolioValueBeforeHome.Should().Be(dayEndHome);
        updated2.PortfolioValueAfterHome.Should().Be(dayEndHome);
        updated2.PortfolioValueBeforeSource.Should().Be(dayEndSource);
        updated2.PortfolioValueAfterSource.Should().Be(dayEndSource);

        yahoo.VerifyNoOtherCalls();
        stooq.VerifyNoOtherCalls();
        twse.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertSnapshotAsync_WithNegativeLedgerBalance_DoesNotFloorSnapshotValues()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, ledgerId, baseCurrency: "USD", homeCurrency: "USD", displayName: "Negative Ledger Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var eventDate = new DateTime(2025, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var txId = Guid.NewGuid();

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(txId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTransaction?)null);

        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var ledger = new CurrencyLedger(userId, "USD", "USD Ledger", "USD");
        typeof(CurrencyLedger)
            .BaseType!
            .GetProperty(nameof(CurrencyLedger.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(ledger, [ledgerId]);

        var initialBalance = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            transactionType: CurrencyTransactionType.InitialBalance,
            foreignAmount: 100m,
            homeAmount: 100m,
            exchangeRate: 1m);

        var spend = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: eventDate,
            transactionType: CurrencyTransactionType.Spend,
            foreignAmount: 150m,
            homeAmount: 150m,
            exchangeRate: 1m);

        ledger.AddTransaction(initialBalance);
        ledger.AddTransaction(spend);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(ledgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = new TransactionPortfolioSnapshotService(
            dbContext,
            portfolioRepository.Object,
            transactionRepository.Object,
            currencyLedgerRepository.Object,
            new CurrencyLedgerService(),
            new PortfolioCalculator(),
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        await sut.UpsertSnapshotAsync(portfolio.Id, txId, eventDate, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == txId);

        snapshot.PortfolioValueBeforeSource.Should().Be(100m);
        snapshot.PortfolioValueAfterSource.Should().Be(-50m);
        snapshot.PortfolioValueBeforeHome.Should().Be(100m);
        snapshot.PortfolioValueAfterHome.Should().Be(-50m);
    }

    private static AppDbContext CreateInMemoryDbContext(ICurrentUserService currentUserService)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, currentUserService);
    }
}
