using System.Data.Common;
using FluentAssertions;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class MonthlySnapshotServiceTests
{
    [Fact]
    public async Task GetMonthlyNetWorthAsync_CacheHit_ReturnsCachedValues_AndDoesNotCallPriceServices()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);

        var month = new DateOnly(2023, 1, 1);
        dbContext.MonthlyNetWorthSnapshots.Add(new MonthlyNetWorthSnapshot(
            portfolioId: portfolio.Id,
            month: month,
            totalValueHome: 12345.6789m,
            totalContributions: 1000m,
            dataSource: "Yahoo",
            calculatedAt: DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "VT",
                    transactionType: TransactionType.Buy,
                    shares: 1m,
                    pricePerShare: 100m,
                    exchangeRate: 30m,
                    fees: 0m,
                    market: StockMarket.US)
            ]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: month,
            toMonth: month,
            CancellationToken.None);

        // Assert
        result.Currency.Should().Be("TWD");
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("Yahoo");

        result.Data.Should().HaveCount(1);
        result.Data[0].Month.Should().Be("2023-01");
        result.Data[0].Value.Should().Be(12345.6789m);
        result.Data[0].Contributions.Should().Be(1000m);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_TaiwanTicker_YahooMiss_FallsBackToTwse_AndSkipsFxCall()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 500m,
            exchangeRate: null,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);

        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);
        twse.Setup(x => x.GetStockPriceAsync("2330", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwseStockPriceResult(
                Price: 600m,
                ActualDate: new DateOnly(2023, 1, 31),
                StockNo: "2330"));

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("TWSE");

        result.Data[0].Value.Should().Be(6000m);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        twse.Verify(
            x => x.GetStockPriceAsync("2330", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_TaiwanTicker_UsesYahooPrice_WhenYahooHasData()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            ticker: "2330",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 500m,
            exchangeRate: null,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 620m,
                ActualDate = new DateOnly(2023, 1, 31),
                Currency = "TWD"
            });

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("Yahoo");
        result.Data[0].Value.Should().Be(6200m);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        twse.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_YahooPriceMissing_FallsBackToStooq_WhenMarketIsUS()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            ticker: "VT",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        yahoo.Setup(x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooExchangeRateResult
            {
                Rate = 30m,
                ActualDate = new DateOnly(2023, 1, 31),
                CurrencyPair = "USDTWD"
            });

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        stooq.Setup(x => x.GetStockPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(
                Price: 110m,
                ActualDate: new DateOnly(2023, 1, 31),
                Currency: "USD"));

        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("Stooq");
        result.Data[0].Value.Should().Be(33000m);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        stooq.Verify(
            x => x.GetStockPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        yahoo.Verify(
            x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_YahooPriceMissing_DoesNotFallbackToStooq_WhenMarketIsEU()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "EUR", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            ticker: "AGAC",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 35m,
            fees: 0m,
            market: StockMarket.EU,
            currency: Currency.EUR);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("AGAC.AS", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("Mixed");
        result.Data[0].Value.Should().BeNull();

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("AGAC.AS", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        stooq.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_YahooPriceMissing_DoesNotFallbackToStooq_WhenMarketIsTWAndTickerNotNumeric()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
            ticker: "ABC",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            market: StockMarket.TW,
            currency: Currency.TWD);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("ABC.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("ABC.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);

        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);
        twse.Setup(x => x.GetStockPriceAsync("ABC", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwseStockPriceResult?)null);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("Mixed");
        result.Data[0].Value.Should().BeNull();

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("ABC.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("ABC.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        stooq.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_YahooFxMissing_FallsBackToStooqExchangeRate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: new DateTime(2023, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            ticker: "VT",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: 30m,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 110m,
                ActualDate = new DateOnly(2023, 1, 31),
                Currency = "USD"
            });

        yahoo.Setup(x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooExchangeRateResult?)null);

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        stooq.Setup(x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqExchangeRateResult(
                Rate: 31m,
                ActualDate: new DateOnly(2023, 1, 31),
                FromCurrency: "USD",
                ToCurrency: "TWD"));

        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("Yahoo");
        result.Data[0].Value.Should().Be(34100m);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        stooq.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        yahoo.Verify(
            x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        stooq.Verify(
            x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_ReusesExchangeRateLookupForSamePairAndDate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var tx = new StockTransaction(
            portfolioId: portfolio.Id,
            transactionDate: new DateTime(2023, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            ticker: "VT",
            transactionType: TransactionType.Buy,
            shares: 10m,
            pricePerShare: 100m,
            exchangeRate: null,
            fees: 0m,
            market: StockMarket.US,
            currency: Currency.USD);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([tx]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 110m,
                ActualDate = new DateOnly(2023, 1, 31),
                Currency = "USD"
            });

        yahoo.Setup(x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooExchangeRateResult
            {
                Rate = 30m,
                ActualDate = new DateOnly(2023, 1, 31),
                CurrencyPair = "USDTWD"
            });

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.DataSource.Should().Be("Yahoo");
        result.Data[0].Value.Should().Be(33000m);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Called by both contribution and valuation paths.
        yahoo.Verify(
            x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_SameTickerAcrossMonths_UsesYahooRangeSeriesCache()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "VT",
                    transactionType: TransactionType.Buy,
                    shares: 10m,
                    pricePerShare: 100m,
                    exchangeRate: 30m,
                    fees: 0m,
                    market: StockMarket.US,
                    currency: Currency.USD)
            ]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceSeriesAsync("VT", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new YahooHistoricalPricePoint
                {
                    Date = new DateOnly(2023, 1, 31),
                    Price = 100m,
                    Currency = "USD"
                },
                new YahooHistoricalPricePoint
                {
                    Date = new DateOnly(2023, 2, 28),
                    Price = 110m,
                    Currency = "USD"
                },
                new YahooHistoricalPricePoint
                {
                    Date = new DateOnly(2023, 3, 31),
                    Price = 120m,
                    Currency = "USD"
                }
            ]);

        yahoo.Setup(x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooExchangeRateResult
            {
                Rate = 30m,
                ActualDate = new DateOnly(2023, 3, 31),
                CurrencyPair = "USDTWD"
            });

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 3, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(3);
        result.DataSource.Should().Be("Yahoo");
        result.Data.Select(x => x.Month).Should().Equal("2023-01", "2023-02", "2023-03");
        result.Data.Select(x => x.Value).Should().Equal(30000m, 33000m, 36000m);

        yahoo.Verify(
            x => x.GetHistoricalPriceSeriesAsync("VT", It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        yahoo.Verify(
            x => x.GetHistoricalPriceAsync("VT", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);

        yahoo.Verify(
            x => x.GetExchangeRateAsync("USD", "TWD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_FromMonthGreaterThanToMonth_SwapsAndReturnsInclusiveRange()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);

        var jan = new DateOnly(2023, 1, 1);
        var mar = new DateOnly(2023, 3, 1);

        dbContext.MonthlyNetWorthSnapshots.AddRange(
            new MonthlyNetWorthSnapshot(portfolio.Id, jan, 100m, 10m, "Yahoo", DateTime.UtcNow),
            new MonthlyNetWorthSnapshot(portfolio.Id, new DateOnly(2023, 2, 1), 200m, 20m, "Yahoo", DateTime.UtcNow),
            new MonthlyNetWorthSnapshot(portfolio.Id, mar, 300m, 30m, "Yahoo", DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "VT",
                    transactionType: TransactionType.Buy,
                    shares: 1m,
                    pricePerShare: 100m,
                    exchangeRate: 30m,
                    fees: 0m,
                    market: StockMarket.US)
            ]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act (fromMonth > toMonth)
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: mar,
            toMonth: jan,
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(3);
        result.DataSource.Should().Be("Yahoo");

        result.Data.Select(x => x.Month).Should().Equal("2023-01", "2023-02", "2023-03");
        result.Data.Select(x => x.Value).Should().Equal(100m, 200m, 300m);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_MultiMonthCacheHit_BatchLoadsSnapshotsOnce()
    {
        // Arrange
        var user = new User("cache-hit@test.local", "hash", "Cache Hit User");
        var ledger = new CurrencyLedger(user.Id, "USD", "Cache Hit Ledger", "TWD");
        var portfolio = new Portfolio(user.Id, ledger.Id, baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(user.Id);

        var snapshotQueryCounter = new SnapshotSelectCommandInterceptor();

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        await using var dbContext = CreateSqliteDbContext(currentUserService.Object, snapshotQueryCounter, connection);

        dbContext.Users.Add(user);
        dbContext.CurrencyLedgers.Add(ledger);
        dbContext.Portfolios.Add(portfolio);

        var jan = new DateOnly(2023, 1, 1);
        var feb = new DateOnly(2023, 2, 1);
        var mar = new DateOnly(2023, 3, 1);

        dbContext.MonthlyNetWorthSnapshots.AddRange(
            new MonthlyNetWorthSnapshot(portfolio.Id, jan, 100m, 10m, "Yahoo", DateTime.UtcNow),
            new MonthlyNetWorthSnapshot(portfolio.Id, feb, 200m, 20m, "Yahoo", DateTime.UtcNow),
            new MonthlyNetWorthSnapshot(portfolio.Id, mar, 300m, 30m, "Yahoo", DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "VT",
                    transactionType: TransactionType.Buy,
                    shares: 1m,
                    pricePerShare: 100m,
                    exchangeRate: 30m,
                    fees: 0m,
                    market: StockMarket.US)
            ]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: jan,
            toMonth: mar,
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(3);
        result.DataSource.Should().Be("Yahoo");
        result.Data.Select(x => x.Month).Should().Equal("2023-01", "2023-02", "2023-03");
        result.Data.Select(x => x.Value).Should().Equal(100m, 200m, 300m);

        snapshotQueryCounter.MonthlySnapshotSelectCount.Should().Be(1);
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_Contributions_TaiwanSell_UsesFloorNetProceeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "2330",
                    transactionType: TransactionType.Buy,
                    shares: 1m,
                    pricePerShare: 100.6m,
                    exchangeRate: 1m,
                    fees: 10m,
                    market: StockMarket.TW,
                    currency: Currency.TWD),
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "2330",
                    transactionType: TransactionType.Sell,
                    shares: 1m,
                    pricePerShare: 100.6m,
                    exchangeRate: 1m,
                    fees: 10m,
                    market: StockMarket.TW,
                    currency: Currency.TWD)
            ]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 100m,
                ActualDate = new DateOnly(2023, 1, 31),
                Currency = "TWD"
            });

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.Data[0].Contributions.Should().Be(20m);
        // Buy: floor(100.6)+10 = 110; Sell: floor(100.6)-10 = 90; Net = 20
    }

    [Fact]
    public async Task GetMonthlyNetWorthAsync_Contributions_NonTaiwanSell_UsesExactNetProceeds()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "USD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);
        await dbContext.SaveChangesAsync();

        var portfolioRepo = new Mock<IPortfolioRepository>();
        portfolioRepo.Setup(x => x.GetByIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(portfolio);

        var txRepo = new Mock<IStockTransactionRepository>();
        txRepo.Setup(x => x.GetByPortfolioIdAsync(portfolio.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 5, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "VOO",
                    transactionType: TransactionType.Buy,
                    shares: 1m,
                    pricePerShare: 100.6m,
                    exchangeRate: 1m,
                    fees: 10m,
                    market: StockMarket.US,
                    currency: Currency.USD),
                new StockTransaction(
                    portfolioId: portfolio.Id,
                    transactionDate: new DateTime(2023, 1, 20, 0, 0, 0, DateTimeKind.Utc),
                    ticker: "VOO",
                    transactionType: TransactionType.Sell,
                    shares: 1m,
                    pricePerShare: 100.6m,
                    exchangeRate: 1m,
                    fees: 10m,
                    market: StockMarket.US,
                    currency: Currency.USD)
            ]);

        var yahoo = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        yahoo.Setup(x => x.GetHistoricalPriceAsync("VOO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 100m,
                ActualDate = new DateOnly(2023, 1, 31),
                Currency = "USD"
            });

        var stooq = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var twse = new Mock<ITwseStockHistoricalPriceService>(MockBehavior.Strict);

        var sut = CreateSut(
            dbContext,
            portfolioRepo.Object,
            txRepo.Object,
            currentUserService.Object,
            yahoo.Object,
            stooq.Object,
            twse.Object);

        // Act
        var result = await sut.GetMonthlyNetWorthAsync(
            portfolio.Id,
            fromMonth: new DateOnly(2023, 1, 1),
            toMonth: new DateOnly(2023, 1, 1),
            CancellationToken.None);

        // Assert
        result.TotalMonths.Should().Be(1);
        result.Data[0].Contributions.Should().Be(20m);
        // Buy: 100.6+10 = 110.6; Sell: 100.6-10 = 90.6; Net = 20
    }

    [Fact]
    public async Task InvalidateFromMonthAsync_RemovesSnapshotsFromMonthInclusive()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var portfolio = new Portfolio(userId, Guid.NewGuid(), baseCurrency: "USD", homeCurrency: "TWD", displayName: "Test");

        var currentUserService = new Mock<ICurrentUserService>();
        currentUserService.SetupGet(x => x.UserId).Returns(userId);

        var dbContext = CreateInMemoryDbContext(currentUserService.Object);
        dbContext.Portfolios.Add(portfolio);

        var jan = new DateOnly(2023, 1, 1);
        var feb = new DateOnly(2023, 2, 1);
        var mar = new DateOnly(2023, 3, 1);

        dbContext.MonthlyNetWorthSnapshots.AddRange(
            new MonthlyNetWorthSnapshot(portfolio.Id, jan, 100m, 10m, "Yahoo", DateTime.UtcNow),
            new MonthlyNetWorthSnapshot(portfolio.Id, feb, 200m, 20m, "Yahoo", DateTime.UtcNow),
            new MonthlyNetWorthSnapshot(portfolio.Id, mar, 300m, 30m, "Yahoo", DateTime.UtcNow));

        await dbContext.SaveChangesAsync();

        var sut = CreateSut(
            dbContext,
            portfolioRepository: Mock.Of<IPortfolioRepository>(),
            transactionRepository: Mock.Of<IStockTransactionRepository>(),
            currentUserService: currentUserService.Object,
            yahooService: Mock.Of<IYahooHistoricalPriceService>(),
            stooqService: Mock.Of<IStooqHistoricalPriceService>(),
            twseStockService: Mock.Of<ITwseStockHistoricalPriceService>());

        // Act
        await sut.InvalidateFromMonthAsync(portfolio.Id, fromMonth: feb, CancellationToken.None);

        // Assert
        var remaining = await dbContext.MonthlyNetWorthSnapshots
            .IgnoreQueryFilters()
            .Where(s => s.PortfolioId == portfolio.Id)
            .OrderBy(s => s.Month)
            .ToListAsync();

        remaining.Select(x => x.Month).Should().Equal(jan);
    }

    private static AppDbContext CreateInMemoryDbContext(ICurrentUserService currentUserService)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options, currentUserService);
    }

    private static AppDbContext CreateSqliteDbContext(
        ICurrentUserService currentUserService,
        DbCommandInterceptor interceptor,
        SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .EnableSensitiveDataLogging()
            .Options;

        var context = new AppDbContext(options, currentUserService);
        context.Database.EnsureCreated();
        return context;
    }

    private sealed class SnapshotSelectCommandInterceptor : DbCommandInterceptor
    {
        public int MonthlySnapshotSelectCount { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CountIfMonthlySnapshotSelect(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CountIfMonthlySnapshotSelect(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        private void CountIfMonthlySnapshotSelect(string commandText)
        {
            if (commandText.Contains("select", StringComparison.OrdinalIgnoreCase) &&
                commandText.Contains("monthly_net_worth_snapshots", StringComparison.OrdinalIgnoreCase))
            {
                MonthlySnapshotSelectCount++;
            }
        }
    }

    private static MonthlySnapshotService CreateSut(
        AppDbContext dbContext,
        IPortfolioRepository portfolioRepository,
        IStockTransactionRepository transactionRepository,
        ICurrentUserService currentUserService,
        IYahooHistoricalPriceService yahooService,
        IStooqHistoricalPriceService stooqService,
        ITwseStockHistoricalPriceService twseStockService)
    {
        var calculator = new PortfolioCalculator();

        return new MonthlySnapshotService(
            dbContext,
            portfolioRepository,
            transactionRepository,
            calculator,
            currentUserService,
            yahooService,
            stooqService,
            twseStockService);
    }
}
