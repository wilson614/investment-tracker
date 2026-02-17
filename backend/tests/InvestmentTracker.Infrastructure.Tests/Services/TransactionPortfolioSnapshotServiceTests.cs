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
    public async Task UpsertSnapshotAsync_YahooFirstForTaiwanTicker_AndCachesHistoricalLookupWithinRun()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Yahoo First TW");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2023, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var tx1 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 500m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx1.CreatedAt = new DateTime(2023, 1, 10, 1, 0, 0, DateTimeKind.Utc);

        var tx2 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 510m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx2.CreatedAt = new DateTime(2023, 1, 10, 2, 0, 0, DateTimeKind.Utc);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(tx2.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx2);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx1, tx2]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 620m,
                ActualDate = new DateOnly(2023, 1, 10),
                Currency = "TWD"
            });

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
        await sut.UpsertSnapshotAsync(portfolio.Id, tx2.Id, tx2.TransactionDate, CancellationToken.None);

        // Assert
        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().HaveCount(2);

        // Home/source valuations are calculated separately, so Yahoo lookup occurs twice.
        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        twse.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_TaiwanTicker_YahooMiss_FallsBackToTwse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Yahoo Miss TW");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2023, 2, 10, 0, 0, 0, DateTimeKind.Utc);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 2m,
            pricePerShare: 500m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx.CreatedAt = new DateTime(2023, 2, 10, 1, 0, 0, DateTimeKind.Utc);

        var txDeleted = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "2330",
            transactionType: TransactionType.Sell,
            shares: 1m,
            pricePerShare: 505m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        txDeleted.MarkAsDeleted();


        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(tx.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx, txDeleted]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);

        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);
        twse.Setup(x => x.GetStockPriceAsync("2330", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwseStockPriceResult(
                Price: 630m,
                ActualDate: new DateOnly(2023, 2, 10),
                StockNo: "2330",
                Source: "TPEx"));

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
        await sut.UpsertSnapshotAsync(portfolio.Id, tx.Id, tx.TransactionDate, CancellationToken.None);

        // Assert
        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().HaveCount(1);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        twse.Verify(
            x => x.GetStockPriceAsync("2330", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
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

    [Fact]
    public async Task UpsertSnapshotAsync_MissingHistoricalPrice_DoesNotWriteSnapshot()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "USD", displayName: "Missing Price");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "VT",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(tx.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tx);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        stooq.Setup(x => x.GetStockPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StooqPriceResult?)null);

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
        await sut.UpsertSnapshotAsync(portfolio.Id, tx.Id, tx.TransactionDate, CancellationToken.None);

        // Assert
        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task BackfillSnapshotsAsync_SameDayMultipleStockTransactions_DoesNotRepeatExpensiveStockValuationPerTransaction()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Backfill Single Day");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2023, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var tx1 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 500m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx1.CreatedAt = new DateTime(2023, 1, 10, 1, 0, 0, DateTimeKind.Utc);

        var tx2 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 510m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx2.CreatedAt = new DateTime(2023, 1, 10, 2, 0, 0, DateTimeKind.Utc);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx1, tx2]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 620m,
                ActualDate = new DateOnly(2023, 1, 10),
                Currency = "TWD"
            });

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
        await sut.BackfillSnapshotsAsync(portfolio.Id, date, date, CancellationToken.None);

        // Assert
        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().HaveCount(2);

        var first = snapshots.Single(s => s.TransactionId == tx1.Id);
        var second = snapshots.Single(s => s.TransactionId == tx2.Id);

        first.PortfolioValueBeforeHome.Should().Be(0m);
        first.PortfolioValueAfterHome.Should().Be(1240m);
        first.PortfolioValueBeforeSource.Should().Be(0m);
        first.PortfolioValueAfterSource.Should().Be(1240m);

        second.PortfolioValueBeforeHome.Should().Be(1240m);
        second.PortfolioValueAfterHome.Should().Be(1240m);
        second.PortfolioValueBeforeSource.Should().Be(1240m);
        second.PortfolioValueAfterSource.Should().Be(1240m);

        // 同日 backfill 僅需 dayEnd 一次估值（且 source==home 會重用 home）。
        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(1));

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        twse.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task BackfillSnapshotsAsync_MultiDateSourceEqualsHome_CachesValuationByDateAndAvoidsRepeatedLookups()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Backfill Multi-Day Cache");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var day1 = new DateTime(2023, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2023, 1, 11, 0, 0, 0, DateTimeKind.Utc);

        var tx1 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: day1,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 500m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx1.CreatedAt = new DateTime(2023, 1, 10, 1, 0, 0, DateTimeKind.Utc);

        var tx2 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: day1,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 505m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx2.CreatedAt = new DateTime(2023, 1, 10, 2, 0, 0, DateTimeKind.Utc);

        var tx3 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: day2,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 510m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx3.CreatedAt = new DateTime(2023, 1, 11, 1, 0, 0, DateTimeKind.Utc);

        var allTransactions = new List<StockTransaction> { tx1, tx2, tx3 };

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => allTransactions.SingleOrDefault(t => t.Id == id));
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(allTransactions);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var priceByDate = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2023, 1, 10)] = 600m,
            [new DateOnly(2023, 1, 11)] = 610m
        };

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateOnly queryDate, CancellationToken _) =>
            {
                var resolvedPrice = priceByDate.TryGetValue(queryDate, out var price)
                    ? price
                    : 600m;

                return new YahooHistoricalPriceResult
                {
                    Price = resolvedPrice,
                    ActualDate = queryDate,
                    Currency = "TWD"
                };
            });

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
        await sut.BackfillSnapshotsAsync(portfolio.Id, day1, day2, CancellationToken.None);

        // Assert - 兩個日期只應觸發兩次查價（day1/day2），而不是隨交易數成長
        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().HaveCount(3);

        var first = snapshots.Single(s => s.TransactionId == tx1.Id);
        var second = snapshots.Single(s => s.TransactionId == tx2.Id);
        var third = snapshots.Single(s => s.TransactionId == tx3.Id);

        first.PortfolioValueBeforeHome.Should().Be(0m);
        first.PortfolioValueAfterHome.Should().Be(1200m);
        first.PortfolioValueBeforeSource.Should().Be(0m);
        first.PortfolioValueAfterSource.Should().Be(1200m);

        second.PortfolioValueBeforeHome.Should().Be(1200m);
        second.PortfolioValueAfterHome.Should().Be(1200m);
        second.PortfolioValueBeforeSource.Should().Be(1200m);
        second.PortfolioValueAfterSource.Should().Be(1200m);

        third.PortfolioValueBeforeHome.Should().Be(1200m);
        third.PortfolioValueAfterHome.Should().Be(1830m);
        third.PortfolioValueBeforeSource.Should().Be(1200m);
        third.PortfolioValueAfterSource.Should().Be(1830m);
    }

    [Fact]
    public async Task BackfillSnapshotsAsync_SameDayMultipleStockTransactions_ResultMatchesSequentialUpsertBehavior()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Backfill Equivalence");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var day1 = new DateTime(2023, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2023, 1, 11, 0, 0, 0, DateTimeKind.Utc);

        var tx1 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: day1,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 500m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx1.CreatedAt = new DateTime(2023, 1, 10, 1, 0, 0, DateTimeKind.Utc);

        var tx2 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: day1,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 505m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx2.CreatedAt = new DateTime(2023, 1, 10, 2, 0, 0, DateTimeKind.Utc);

        var tx3 = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: day2,
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 510m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);
        tx3.CreatedAt = new DateTime(2023, 1, 11, 1, 0, 0, DateTimeKind.Utc);

        var allTransactions = new List<StockTransaction> { tx1, tx2, tx3 };

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => allTransactions.SingleOrDefault(t => t.Id == id));
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(allTransactions);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var priceByDate = new Dictionary<DateOnly, decimal>
        {
            [new DateOnly(2023, 1, 10)] = 600m,
            [new DateOnly(2023, 1, 11)] = 610m
        };

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateOnly queryDate, CancellationToken _) =>
            {
                var resolvedPrice = priceByDate.TryGetValue(queryDate, out var price)
                    ? price
                    : 600m;

                return new YahooHistoricalPriceResult
                {
                    Price = resolvedPrice,
                    ActualDate = queryDate,
                    Currency = "TWD"
                };
            });

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

        // Act - 新 backfill 流程（按日期）
        await sut.BackfillSnapshotsAsync(portfolio.Id, day1, day2, CancellationToken.None);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        var backfillSnapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        var backfillByTx = backfillSnapshots.ToDictionary(
            s => s.TransactionId,
            s => new
            {
                s.PortfolioValueBeforeHome,
                s.PortfolioValueAfterHome,
                s.PortfolioValueBeforeSource,
                s.PortfolioValueAfterSource
            });

        dbContext.TransactionPortfolioSnapshots.RemoveRange(backfillSnapshots);
        await dbContext.SaveChangesAsync();

        // Act - 舊 per-transaction 行為（逐筆 upsert）
        foreach (var tx in allTransactions
                     .OrderBy(t => t.TransactionDate)
                     .ThenBy(t => t.CreatedAt)
                     .ThenBy(t => t.Id))
        {
            await sut.UpsertSnapshotAsync(portfolio.Id, tx.Id, tx.TransactionDate, CancellationToken.None);
        }

        var sequentialSnapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        // Assert
        sequentialSnapshots.Should().HaveCount(backfillSnapshots.Count);

        foreach (var sequential in sequentialSnapshots)
        {
            backfillByTx.TryGetValue(sequential.TransactionId, out var expected).Should().BeTrue();
            expected.Should().NotBeNull();

            sequential.PortfolioValueBeforeHome.Should().Be(expected!.PortfolioValueBeforeHome);
            sequential.PortfolioValueAfterHome.Should().Be(expected.PortfolioValueAfterHome);
            sequential.PortfolioValueBeforeSource.Should().Be(expected.PortfolioValueBeforeSource);
            sequential.PortfolioValueAfterSource.Should().Be(expected.PortfolioValueAfterSource);
        }
    }

    private static AppDbContext CreateInMemoryDbContext(ICurrentUserService currentUserService)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, currentUserService);
    }
}
