using FluentAssertions;
using InvestmentTracker.API.Controllers;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Services;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.API.Tests.Controllers;

public class MarketDataControllerHistoricalPriceTests
{
    [Fact]
    public async Task GetHistoricalPrice_NonYearEnd_YahooSuccess_DoesNotCallStooq()
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var targetDate = new DateOnly(2024, 10, 15);
        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 120.50m,
                Currency = "USD",
                ActualDate = targetDate
            });

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        // Act
        var actionResult = await controller.GetHistoricalPrice("VT", "2024-10-15", CancellationToken.None);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = okResult.Value.Should().BeOfType<HistoricalPriceResponse>().Subject;

        payload.Price.Should().Be(120.50m);
        payload.Currency.Should().Be("USD");
        payload.ActualDate.Should().Be("2024-10-15");

        yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetHistoricalPrice_NonYearEnd_YahooFails_UsMarket_FallsBackToStooq()
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var targetDate = new DateOnly(2024, 10, 15);
        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        stooqServiceMock
            .Setup(x => x.GetStockPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(118.25m, targetDate, "USD"));

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        // Act
        var actionResult = await controller.GetHistoricalPrice("VT", "2024-10-15", CancellationToken.None);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = okResult.Value.Should().BeOfType<HistoricalPriceResponse>().Subject;

        payload.Price.Should().Be(118.25m);
        payload.Currency.Should().Be("USD");
        payload.ActualDate.Should().Be("2024-10-15");

        yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoricalPrice_NonYearEnd_YahooFails_UkTicker_FallsBackToStooq()
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var ticker = "VWRL.L";
        var targetDate = new DateOnly(2024, 10, 15);

        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(124.10m, targetDate, "GBP"));

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        // Act
        var actionResult = await controller.GetHistoricalPrice(ticker, "2024-10-15", CancellationToken.None);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = okResult.Value.Should().BeOfType<HistoricalPriceResponse>().Subject;

        payload.Price.Should().Be(124.10m);
        payload.Currency.Should().Be("GBP");
        payload.ActualDate.Should().Be("2024-10-15");

        yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()),
            Times.Once);

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("AGAC.AS", "2024-10-15")]
    [InlineData("2330", "2024-10-15")]
    public async Task GetHistoricalPrice_NonYearEnd_YahooFails_NonUsUkMarket_DoesNotFallbackToStooq(
        string ticker,
        string date)
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var targetDate = DateOnly.Parse(date);
        var expectedYahooSymbol = ticker == "AGAC.AS" ? "AGAC.AS" : "2330.TW";

        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(expectedYahooSymbol, targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        if (ticker == "2330")
        {
            yahooServiceMock
                .Setup(x => x.GetHistoricalPriceAsync("2330.TWO", targetDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync((YahooHistoricalPriceResult?)null);
        }

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        // Act
        var actionResult = await controller.GetHistoricalPrice(ticker, date, CancellationToken.None);

        // Assert
        actionResult.Result.Should().BeOfType<NotFoundObjectResult>();

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetHistoricalPrices_NonYearEnd_YahooSuccess_DoesNotCallStooq()
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var targetDate = new DateOnly(2024, 10, 15);

        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 120.50m,
                Currency = "USD",
                ActualDate = targetDate
            });

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        var request = new HistoricalPricesRequest(
            Tickers: ["VT"],
            Date: "2024-10-15",
            Markets: new Dictionary<string, int?>
            {
                ["VT"] = (int)StockMarket.US
            });

        // Act
        var actionResult = await controller.GetHistoricalPrices(request, CancellationToken.None);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = okResult.Value.Should().BeOfType<Dictionary<string, HistoricalPriceResponse>>().Subject;

        payload.Should().ContainKey("VT");
        payload["VT"].Price.Should().Be(120.50m);

        yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetHistoricalPrices_NonYearEnd_YahooFails_UsMarket_FallsBackToStooq()
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var targetDate = new DateOnly(2024, 10, 15);

        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        stooqServiceMock
            .Setup(x => x.GetStockPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(118.25m, targetDate, "USD"));

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        var request = new HistoricalPricesRequest(
            Tickers: ["VT"],
            Date: "2024-10-15",
            Markets: new Dictionary<string, int?>
            {
                ["VT"] = (int)StockMarket.US
            });

        // Act
        var actionResult = await controller.GetHistoricalPrices(request, CancellationToken.None);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = okResult.Value.Should().BeOfType<Dictionary<string, HistoricalPriceResponse>>().Subject;

        payload.Should().ContainKey("VT");
        payload["VT"].Price.Should().Be(118.25m);

        yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync("VT", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetHistoricalPrices_NonYearEnd_YahooFails_UkTicker_FallsBackToStooq()
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var ticker = "VWRL.L";
        var targetDate = new DateOnly(2024, 10, 15);

        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(124.10m, targetDate, "GBP"));

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        var request = new HistoricalPricesRequest(
            Tickers: [ticker],
            Date: "2024-10-15",
            Markets: null);

        // Act
        var actionResult = await controller.GetHistoricalPrices(request, CancellationToken.None);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = okResult.Value.Should().BeOfType<Dictionary<string, HistoricalPriceResponse>>().Subject;

        payload.Should().ContainKey(ticker);
        payload[ticker].Price.Should().Be(124.10m);
        payload[ticker].Currency.Should().Be("GBP");
        payload[ticker].ActualDate.Should().Be("2024-10-15");

        yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()),
            Times.Once);

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(ticker, targetDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData("AGAC.AS", StockMarket.EU, "AGAC.AS")]
    [InlineData("2330", StockMarket.TW, "2330.TW")]
    public async Task GetHistoricalPrices_NonYearEnd_YahooFails_NonUsUkMarket_DoesNotFallbackToStooq(
        string ticker,
        StockMarket market,
        string yahooSymbol)
    {
        // Arrange
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);

        var targetDate = new DateOnly(2024, 10, 15);

        yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(yahooSymbol, targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        if (market == StockMarket.TW)
        {
            yahooServiceMock
                .Setup(x => x.GetHistoricalPriceAsync("2330.TWO", targetDate, It.IsAny<CancellationToken>()))
                .ReturnsAsync((YahooHistoricalPriceResult?)null);
        }

        await using var dbContext = CreateInMemoryDbContext();
        var controller = CreateController(
            dbContext,
            stooqServiceMock.Object,
            yahooServiceMock.Object,
            historicalYearEndDataService: Mock.Of<IHistoricalYearEndDataService>());

        var request = new HistoricalPricesRequest(
            Tickers: [ticker],
            Date: "2024-10-15",
            Markets: new Dictionary<string, int?>
            {
                [ticker] = (int)market
            });

        // Act
        var actionResult = await controller.GetHistoricalPrices(request, CancellationToken.None);

        // Assert
        var okResult = actionResult.Result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = okResult.Value.Should().BeOfType<Dictionary<string, HistoricalPriceResponse>>().Subject;

        payload.Should().NotContainKey(ticker);

        stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static MarketDataController CreateController(
        AppDbContext dbContext,
        IStooqHistoricalPriceService stooqService,
        IYahooHistoricalPriceService yahooHistoricalPriceService,
        IHistoricalYearEndDataService historicalYearEndDataService)
    {
        var euronextQuoteService = new EuronextQuoteService(
            apiClient: Mock.Of<IEuronextApiClient>(),
            symbolMappingRepository: Mock.Of<IEuronextSymbolMappingRepository>(),
            exchangeRateProvider: Mock.Of<IExchangeRateProvider>(),
            logger: Mock.Of<ILogger<EuronextQuoteService>>());

        var twseSymbolMappingService = new TwseSymbolMappingService(
            httpClient: new HttpClient(),
            mappingRepository: Mock.Of<ITwSecurityMappingRepository>(),
            logger: Mock.Of<ILogger<TwseSymbolMappingService>>());

        var controller = new MarketDataController(
            capeDataService: Mock.Of<ICapeDataService>(),
            marketYtdService: Mock.Of<IMarketYtdService>(),
            euronextQuoteService: euronextQuoteService,
            twseSymbolMappingService: twseSymbolMappingService,
            stooqService: stooqService,
            twseService: Mock.Of<ITwseStockHistoricalPriceService>(),
            yahooHistoricalPriceService: yahooHistoricalPriceService,
            benchmarkAnnualReturnRepository: Mock.Of<IBenchmarkAnnualReturnRepository>(),
            historicalYearEndDataService: historicalYearEndDataService,
            historicalYearEndDataRepository: Mock.Of<IHistoricalYearEndDataRepository>(),
            txDateFxService: Mock.Of<ITransactionDateExchangeRateService>(),
            dbContext: dbContext,
            logger: Mock.Of<ILogger<MarketDataController>>());

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }
}
