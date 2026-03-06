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
    public async Task UpsertSnapshotAsync_SameCurrencyFxWithWhitespace_DoesNotCallFxProviders_AndWritesSnapshot()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Same Currency FX Whitespace");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2024, 3, 15, 0, 0, 0, DateTimeKind.Utc);

        var tx = new StockTransaction(
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
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 620m,
                ActualDate = new DateOnly(2024, 3, 15),
                Currency = "  twd  "
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
        await sut.UpsertSnapshotAsync(portfolio.Id, tx.Id, tx.TransactionDate, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == tx.Id);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(620m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(620m);

        yahoo.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        stooq.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
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
    public async Task BackfillSnapshotsAsync_InitialBalanceIncluded_OffsetSpendExcludedFromExternalCashFlowEvents()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, ledgerId, baseCurrency: "USD", homeCurrency: "USD", displayName: "Offset Spend Selection");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var eventDate = new DateTime(2025, 2, 10, 0, 0, 0, DateTimeKind.Utc);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
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
            transactionDate: eventDate,
            transactionType: CurrencyTransactionType.InitialBalance,
            foreignAmount: 100m,
            homeAmount: 100m,
            exchangeRate: 1m,
            notes: "import-execute-opening-initial-balance");

        var offsetSpend = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: eventDate,
            transactionType: CurrencyTransactionType.Spend,
            foreignAmount: 100m,
            relatedStockTransactionId: Guid.NewGuid(),
            notes: "import-execute-opening-initial-balance-offset");

        ledger.AddTransaction(initialBalance);
        ledger.AddTransaction(offsetSpend);

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
        await sut.BackfillSnapshotsAsync(portfolio.Id, eventDate, eventDate, CancellationToken.None);

        // Assert
        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().HaveCount(1);
        snapshots.Single().TransactionId.Should().Be(initialBalance.Id);
        snapshots.Select(s => s.TransactionId).Should().NotContain(offsetSpend.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackfillSnapshotsAsync_NonTradingAnchorSeededAdjustment_WhenPriceMissingOrZero_Day1SnapshotUsesSeedCost_AndOffsetSpendExcluded(bool yahooReturnsZeroPrice)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, ledgerId, baseCurrency: "USD", homeCurrency: "USD", displayName: "Non-Trading Anchor Seeded Adjustment");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var anchorDate = new DateTime(2024, 5, 12, 0, 0, 0, DateTimeKind.Utc); // Sunday
        var seededDate = anchorDate.AddDays(-2); // Friday

        var seededAdjustment = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: seededDate,
            ticker: "VT",
            transactionType: TransactionType.Adjustment,
            shares: 10_000m,
            pricePerShare: 100m,
            exchangeRate: 0.0000004m, // rounds to 0 with 6 decimals, but HasExchangeRate remains true
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD,
            notes: "import-execute-adjustment");

        var seededPosition = new PortfolioCalculator()
            .RecalculateAllPositionsWithSplitAdjustments(
                [seededAdjustment],
                splits: [],
                splitService: new StockSplitAdjustmentService())
            .Single();

        seededPosition.AverageCostPerShareHome.Should().Be(0m);
        seededPosition.TotalCostSource.Should().Be(1_000_000m);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([seededAdjustment]);

        var ledger = new CurrencyLedger(userId, "USD", "USD Ledger", "USD");
        typeof(CurrencyLedger)
            .BaseType!
            .GetProperty(nameof(CurrencyLedger.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(ledger, [ledgerId]);

        var openingInitialBalance = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: anchorDate,
            transactionType: CurrencyTransactionType.InitialBalance,
            foreignAmount: 1m,
            homeAmount: 1m,
            exchangeRate: 1m,
            relatedStockTransactionId: seededAdjustment.Id,
            notes: "import-execute-opening-initial-balance");

        var openingOffsetSpend = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: anchorDate,
            transactionType: CurrencyTransactionType.Spend,
            foreignAmount: 1m,
            relatedStockTransactionId: seededAdjustment.Id,
            notes: "import-execute-opening-initial-balance-offset");

        ledger.AddTransaction(openingInitialBalance);
        ledger.AddTransaction(openingOffsetSpend);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(ledgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        if (yahooReturnsZeroPrice)
        {
            yahoo.Setup(x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string _, DateOnly queryDate, CancellationToken _) => new YahooHistoricalPriceResult
                {
                    Price = 0m,
                    ActualDate = queryDate,
                    Currency = "USD"
                });
        }
        else
        {
            yahoo.Setup(x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((YahooHistoricalPriceResult?)null);
        }

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        if (!yahooReturnsZeroPrice)
        {
            stooq.Setup(x => x.GetStockPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StooqPriceResult?)null);
        }

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
        await sut.BackfillSnapshotsAsync(portfolio.Id, anchorDate, anchorDate, CancellationToken.None);

        // Assert
        var snapshots = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .ToListAsync();

        snapshots.Should().HaveCount(1);
        snapshots.Select(s => s.TransactionId).Should().ContainSingle().Which.Should().Be(openingInitialBalance.Id);
        snapshots.Select(s => s.TransactionId).Should().NotContain(openingOffsetSpend.Id);

        var snapshot = snapshots.Single();
        snapshot.PortfolioValueBeforeSource.Should().BeApproximately(1_000_000m, 0.0001m);
        snapshot.PortfolioValueBeforeHome.Should().BeApproximately(1_000_000m, 0.0001m);
        snapshot.PortfolioValueBeforeSource.Should().BeGreaterThan(900_000m);
        snapshot.PortfolioValueBeforeHome.Should().BeGreaterThan(900_000m);

        // InitialBalance and paired offset spend occur on the same anchor day and should net to 0 in ledger valuation.
        snapshot.PortfolioValueAfterSource.Should().BeApproximately(1_000_000m, 0.0001m);
        snapshot.PortfolioValueAfterHome.Should().BeApproximately(1_000_000m, 0.0001m);

        if (yahooReturnsZeroPrice)
        {
            stooq.Verify(
                x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }
        else
        {
            stooq.Verify(
                x => x.GetStockPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
                Times.AtLeastOnce);
        }
    }

    [Fact]
    public async Task UpsertSnapshotAsync_AdjustmentImportInitZeroValues_UsesUsableTotalCostSourceFallback_AndRepresentativeTwrStaysPositive()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "USD", displayName: "Adjustment Import Zero Fallback");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var eventDate = new DateTime(2024, 5, 12, 0, 0, 0, DateTimeKind.Utc);
        var seededDate = eventDate.AddDays(-2);

        var adjustmentTx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: seededDate,
            ticker: "VT",
            transactionType: TransactionType.Adjustment,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 1m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD,
            notes: "import-execute-adjustment");
        adjustmentTx.SetImportInitialization(marketValueAtImport: 0m, historicalTotalCost: 0m);
        adjustmentTx.TotalCostSource.Should().Be(1000m);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTransaction?)null);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([adjustmentTx]);

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
        await sut.UpsertSnapshotAsync(portfolio.Id, eventId, eventDate, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == eventId);

        snapshot.PortfolioValueBeforeSource.Should().Be(1000m);
        snapshot.PortfolioValueAfterSource.Should().Be(1000m);
        snapshot.PortfolioValueBeforeHome.Should().Be(1000m);
        snapshot.PortfolioValueAfterHome.Should().Be(1000m);

        var twr = new ReturnCalculator().CalculateTimeWeightedReturn(
            startValue: 1000m,
            endValue: 1100m,
            cashFlowSnapshots:
            new[]
            {
                new ReturnValuationSnapshot(
                    Date: snapshot.SnapshotDate,
                    ValueBefore: snapshot.PortfolioValueBeforeSource,
                    ValueAfter: snapshot.PortfolioValueAfterSource)
            });

        twr.Should().NotBeNull();
        twr.Should().BeGreaterThan(0m);
        twr.Should().NotBe(-1m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task UpsertSnapshotAsync_HistoricalPriceCurrencyNullOrWhitespace_DoesNotZeroValuation(string? historicalPriceCurrency)
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Null/Whitespace FX Guard");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var buyTx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "VT",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 0.0000004m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        buyTx.TotalCostHome.Should().Be(0m);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(buyTx.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(buyTx);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buyTx]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, DateOnly queryDate, CancellationToken _) => new YahooHistoricalPriceResult
            {
                Price = 150m,
                ActualDate = queryDate,
                Currency = historicalPriceCurrency!
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
        await sut.UpsertSnapshotAsync(portfolio.Id, buyTx.Id, buyTx.TransactionDate, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == buyTx.Id);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(150m);
        snapshot.PortfolioValueAfterHome.Should().BeGreaterThan(0m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(150m);
        snapshot.PortfolioValueAfterSource.Should().BeGreaterThan(0m);

        yahoo.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        stooq.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_MissingHistoricalPrice_FallsBackToCostBasisAndWritesSnapshot()
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
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == tx.Id);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(100m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(100m);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_BuyOnlyMissingPrice_NoAdjustment_ZeroedHomeCost_FallsBackToTransactionBuyCostAndWritesSnapshot()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "USD", displayName: "Buy-Only Missing Price");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var buyTx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "VT",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 0.0000004m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        // 交易匯率四捨五入至 6 位後會變成 0，強制走 transaction-level fallback。
        buyTx.TotalCostHome.Should().Be(0m);
        buyTx.TotalCostSource.Should().Be(100m);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(buyTx.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(buyTx);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buyTx]);

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
        await sut.UpsertSnapshotAsync(portfolio.Id, buyTx.Id, buyTx.TransactionDate, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == buyTx.Id);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(100m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(100m);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_BuyOnlyMissingPrice_ForeignCurrencyZeroedCost_FallbackGuardReturnsMinimumOneForHome()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "EUR", homeCurrency: "TWD", displayName: "Buy Fallback Guard");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var buyTx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "AGAC",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 20m,
            exchangeRate: 0.0000004m,
            fees: 0m,
            market: StockMarket.EU,
            currency: Currency.EUR);

        buyTx.TotalCostHome.Should().Be(0m);
        buyTx.PricePerShare.Should().BePositive();
        buyTx.Shares.Should().BePositive();

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(buyTx.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(buyTx);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buyTx]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("AGAC.AS", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

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
        await sut.UpsertSnapshotAsync(portfolio.Id, buyTx.Id, buyTx.TransactionDate, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == buyTx.Id);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(1m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(200m);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_AdjustmentImportInitZeroCostFields_ForeignCurrencyZeroedCost_FallbackGuardReturnsMinimumOne()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Adjustment Fallback Guard");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var date = new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var adjustmentTx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "AGAC",
            transactionType: TransactionType.Adjustment,
            shares: 10m,
            pricePerShare: 20m,
            exchangeRate: 0.0000004m,
            fees: 0m,
            market: StockMarket.EU,
            currency: Currency.EUR);
        adjustmentTx.SetImportInitialization(marketValueAtImport: 0m, historicalTotalCost: 0m);

        adjustmentTx.MarketValueAtImport.Should().Be(0m);
        adjustmentTx.HistoricalTotalCost.Should().Be(0m);
        adjustmentTx.TotalCostHome.Should().Be(0m);
        adjustmentTx.PricePerShare.Should().BePositive();
        adjustmentTx.Shares.Should().BePositive();

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTransaction?)null);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([adjustmentTx]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("AGAC.AS", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

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
        await sut.UpsertSnapshotAsync(portfolio.Id, eventId, date, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == eventId);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(1m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(1m);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_BuyOnlyMissingPrice_WeekendFxProviderFailure_DoesNotCrashAndStillWritesSnapshot()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Weekend FX Guard");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var sunday = new DateTime(2024, 5, 12, 0, 0, 0, DateTimeKind.Utc);

        var buyTx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: sunday,
            ticker: "VWRA",
            transactionType: TransactionType.Buy,
            shares: 1m,
            pricePerShare: 100m,
            exchangeRate: 40m,
            fees: 0m,
            market: StockMarket.UK,
            currency: Currency.EUR);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(buyTx.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(buyTx);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([buyTx]);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(portfolio.BoundCurrencyLedgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CurrencyLedger?)null);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("VWRA.L", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        yahoo.Setup(x => x.GetExchangeRateAsync("EUR", "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Yahoo weekend FX unavailable"));
        yahoo.Setup(x => x.GetExchangeRateAsync("TWD", "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Yahoo weekend FX unavailable"));

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        stooq.Setup(x => x.GetStockPriceAsync("VWRA", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StooqPriceResult?)null);
        stooq.Setup(x => x.GetExchangeRateAsync("EUR", "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StooqExchangeRateResult?)null);
        stooq.Setup(x => x.GetExchangeRateAsync("TWD", "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StooqExchangeRateResult?)null);

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
        var act = () => sut.UpsertSnapshotAsync(portfolio.Id, buyTx.Id, buyTx.TransactionDate, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();

        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == buyTx.Id);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(4000m);
        snapshot.PortfolioValueAfterHome.Should().BeGreaterThan(0m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(0m);

        yahoo.Verify(
            x => x.GetExchangeRateAsync("EUR", "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_AdjustmentOnlyMultiCurrency_ZeroHistoricalPrice_UsesPositionCurrencyForSourceFallback()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var ledgerId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, ledgerId, baseCurrency: "USD", homeCurrency: "TWD", displayName: "Adjustment Only Fallback");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var eventId = Guid.NewGuid();
        var date = new DateTime(2024, 5, 10, 0, 0, 0, DateTimeKind.Utc);

        var adjustmentTx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: date,
            ticker: "AGAC",
            transactionType: TransactionType.Adjustment,
            shares: 10m,
            pricePerShare: 20m,
            exchangeRate: 40m,
            fees: 0m,
            market: StockMarket.EU,
            currency: Currency.EUR);

        var portfolioRepository = new Mock<IPortfolioRepository>(MockBehavior.Strict);
        portfolioRepository
            .Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var transactionRepository = new Mock<IStockTransactionRepository>(MockBehavior.Strict);
        transactionRepository
            .Setup(x => x.GetByIdAsync(eventId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((StockTransaction?)null);
        transactionRepository
            .Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([adjustmentTx]);

        var ledger = new CurrencyLedger(userId, "USD", "USD Ledger", "TWD");
        typeof(CurrencyLedger)
            .BaseType!
            .GetProperty(nameof(CurrencyLedger.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(ledger, [ledgerId]);

        var initialBalance = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: date,
            transactionType: CurrencyTransactionType.InitialBalance,
            foreignAmount: 1m,
            homeAmount: 1m,
            exchangeRate: 1m);
        ledger.AddTransaction(initialBalance);

        var eventTx = new CurrencyTransaction(
            currencyLedgerId: ledgerId,
            transactionDate: date,
            transactionType: CurrencyTransactionType.Deposit,
            foreignAmount: 1m,
            homeAmount: 1m,
            exchangeRate: 1m);
        typeof(CurrencyTransaction)
            .BaseType!
            .GetProperty(nameof(CurrencyTransaction.Id))!
            .GetSetMethod(nonPublic: true)!
            .Invoke(eventTx, [eventId]);
        ledger.AddTransaction(eventTx);

        var currencyLedgerRepository = new Mock<ICurrencyLedgerRepository>(MockBehavior.Strict);
        currencyLedgerRepository
            .Setup(x => x.GetByIdWithTransactionsAsync(ledgerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledger);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("AGAC.AS", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 0m,
                ActualDate = new DateOnly(2024, 5, 10),
                Currency = "EUR"
            });
        yahoo.Setup(x => x.GetExchangeRateAsync("EUR", "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooExchangeRateResult
            {
                Rate = 1.25m,
                ActualDate = new DateOnly(2024, 5, 10),
                CurrencyPair = "EURUSD"
            });
        yahoo.Setup(x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooExchangeRateResult
            {
                Rate = 30m,
                ActualDate = new DateOnly(2024, 5, 10),
                CurrencyPair = "USDTWD"
            });

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        stooq.Setup(x => x.GetStockPriceAsync("AGAC", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
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
        await sut.UpsertSnapshotAsync(portfolio.Id, eventId, date, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == eventId);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(8060m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(252m);

        yahoo.Verify(
            x => x.GetExchangeRateAsync("EUR", "USD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task UpsertSnapshotAsync_ZeroHistoricalPrice_FallsBackToCostBasisAndWritesSnapshot()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "USD", displayName: "Zero Price");

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
            shares: 2m,
            pricePerShare: 50m,
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
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 0m,
                ActualDate = new DateOnly(2024, 5, 10),
                Currency = "USD"
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
        await sut.UpsertSnapshotAsync(portfolio.Id, tx.Id, tx.TransactionDate, CancellationToken.None);

        // Assert
        var snapshot = await dbContext.TransactionPortfolioSnapshots
            .IgnoreQueryFilters()
            .SingleAsync(s => s.PortfolioId == portfolio.Id && s.TransactionId == tx.Id);

        snapshot.PortfolioValueBeforeHome.Should().Be(0m);
        snapshot.PortfolioValueAfterHome.Should().Be(100m);
        snapshot.PortfolioValueBeforeSource.Should().Be(0m);
        snapshot.PortfolioValueAfterSource.Should().Be(100m);
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
