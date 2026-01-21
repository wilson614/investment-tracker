using FluentAssertions;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for HistoricalYearEndDataService focusing on cache behavior (T125).
/// Tests year-end price/FX caching to prevent duplicate API calls.
/// </summary>
public class HistoricalYearEndDataServiceTests
{
    private readonly Mock<IHistoricalYearEndDataRepository> _repositoryMock;
    private readonly Mock<IStooqHistoricalPriceService> _stooqServiceMock;
    private readonly Mock<ITwseStockHistoricalPriceService> _twseServiceMock;
    private readonly Mock<IYahooHistoricalPriceService> _yahooServiceMock;
    private readonly HistoricalYearEndDataService _service;

    public HistoricalYearEndDataServiceTests()
    {
        _repositoryMock = new Mock<IHistoricalYearEndDataRepository>();
        _stooqServiceMock = new Mock<IStooqHistoricalPriceService>();
        _twseServiceMock = new Mock<ITwseStockHistoricalPriceService>();
        _yahooServiceMock = new Mock<IYahooHistoricalPriceService>();
        var loggerMock = new Mock<ILogger<HistoricalYearEndDataService>>();

        _service = new HistoricalYearEndDataService(
            _repositoryMock.Object,
            _stooqServiceMock.Object,
            _twseServiceMock.Object,
            _yahooServiceMock.Object,
            loggerMock.Object);
    }

    #region Year-End Price Cache Tests (T125)

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_CacheHit_ReturnsFromCache()
    {
        // Arrange
        var ticker = "VT";
        var year = 2023;
        var cachedEntry = HistoricalYearEndData.CreateStockPrice(
            ticker, year, 100.50m, "USD",
            new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc), "Stooq");

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedEntry);

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Price.Should().Be(100.50m);
        result.FromCache.Should().BeTrue();

        // Verify NO API call was made
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_CacheMiss_FetchesAndCaches()
    {
        // Arrange
        var ticker = "VT";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(105.25m, new DateOnly(2023, 12, 29), "USD"));

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Price.Should().Be(105.25m);
        result.FromCache.Should().BeFalse();

        // Verify cache was updated
        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d =>
                d.Ticker == ticker && d.Year == year && d.Value == 105.25m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_SecondCall_UsesCachedValue()
    {
        // Arrange
        var ticker = "VT";
        var year = 2023;
        var callCount = 0;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                    return null; // First call: cache miss
                return HistoricalYearEndData.CreateStockPrice(
                    ticker, year, 105.25m, "USD",
                    new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc), "Stooq");
            });

        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(105.25m, new DateOnly(2023, 12, 29), "USD"));

        // Act - First call
        var result1 = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);
        // Act - Second call
        var result2 = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.FromCache.Should().BeFalse();
        result2.FromCache.Should().BeTrue();

        // Stooq should only be called ONCE
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_CurrentYear_ReturnsNullForLivePrice()
    {
        // Arrange - Current year should return null so frontend uses live price
        var ticker = "VT";
        var currentYear = DateTime.UtcNow.Year;

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, currentYear, market: null, CancellationToken.None);

        // Assert - Should return null (frontend should use live price for current year)
        result.Should().BeNull();

        // Cache should NOT be checked or updated for current year
        _repositoryMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repositoryMock.Verify(
            x => x.AddAsync(It.IsAny<HistoricalYearEndData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // API should NOT be called for current year
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_TaiwanStock_UsesTwseService()
    {
        // Arrange
        var ticker = "2330";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _twseServiceMock
            .Setup(x => x.GetYearEndPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwseStockPriceResult(580.00m, new DateOnly(2023, 12, 29), ticker));

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Price.Should().Be(580.00m);
        result.Currency.Should().Be("TWD");
        result.Source.Should().Be("TWSE");

        // Verify TWSE was called, not Stooq
        _twseServiceMock.Verify(
            x => x.GetYearEndPriceAsync(ticker, year, It.IsAny<CancellationToken>()),
            Times.Once);
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion

    #region Year-End Exchange Rate Cache Tests

    [Fact]
    public async Task GetOrFetchYearEndExchangeRateAsync_CacheHit_ReturnsFromCache()
    {
        // Arrange
        var fromCurrency = "USD";
        var toCurrency = "TWD";
        var currencyPair = "USDTWD";
        var year = 2023;
        var cachedEntry = HistoricalYearEndData.CreateExchangeRate(
            currencyPair, year, 30.75m,
            new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc), "Stooq");

        _repositoryMock
            .Setup(x => x.GetExchangeRateAsync(currencyPair, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedEntry);

        // Act
        var result = await _service.GetOrFetchYearEndExchangeRateAsync(
            fromCurrency, toCurrency, year, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Rate.Should().Be(30.75m);
        result.FromCache.Should().BeTrue();

        // Verify NO API call was made
        _stooqServiceMock.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), 
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndExchangeRateAsync_CacheMiss_FetchesAndCaches()
    {
        // Arrange
        var fromCurrency = "USD";
        var toCurrency = "TWD";
        var currencyPair = "USDTWD";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetExchangeRateAsync(currencyPair, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _stooqServiceMock
            .Setup(x => x.GetExchangeRateAsync(fromCurrency, toCurrency, 
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqExchangeRateResult(31.25m, new DateOnly(2023, 12, 29), fromCurrency, toCurrency));

        // Act
        var result = await _service.GetOrFetchYearEndExchangeRateAsync(
            fromCurrency, toCurrency, year, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Rate.Should().Be(31.25m);
        result.FromCache.Should().BeFalse();

        // Verify cache was updated
        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d =>
                d.Ticker == currencyPair && d.Year == year && d.Value == 31.25m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Yahoo-Stooq Fallback Tests (T114)

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_USStock_TriesYahooFirst()
    {
        // Arrange
        var ticker = "VT";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 105.25m,
                ActualDate = new DateOnly(2023, 12, 29),
                Currency = "USD"
            });

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, Domain.Enums.StockMarket.US, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(105.25m);
        result.Source.Should().Be("Yahoo");

        // Verify Yahoo was called
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Verify Stooq was NOT called (Yahoo succeeded)
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_YahooFails_FallsBackToStooq()
    {
        // Arrange
        var ticker = "VT";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        // Yahoo fails
        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        // Stooq succeeds
        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(105.25m, new DateOnly(2023, 12, 29), "USD"));

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, Domain.Enums.StockMarket.US, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(105.25m);
        result.Source.Should().Be("Stooq");

        // Verify both were called in order
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_BothSourcesFail_ReturnsNull()
    {
        // Arrange
        var ticker = "VT";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        // Both sources fail
        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StooqPriceResult?)null);

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, Domain.Enums.StockMarket.US, CancellationToken.None);

        // Assert
        result.Should().BeNull();

        // Verify both sources were tried
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_UKStock_UsesCorrectYahooSymbol()
    {
        // Arrange
        var ticker = "VWRL";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("VWRL.L", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 85.50m,
                ActualDate = new DateOnly(2023, 12, 29),
                Currency = "GBP"
            });

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, Domain.Enums.StockMarket.UK, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(85.50m);
        result.Currency.Should().Be("GBP");
        result.Source.Should().Be("Yahoo");

        // Verify Yahoo was called with .L suffix
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("VWRL.L", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_EUStock_NoStooqFallback()
    {
        // Arrange - EU stocks should only use Yahoo, no Stooq fallback
        var ticker = "AGAC";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        // Yahoo fails
        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, Domain.Enums.StockMarket.EU, CancellationToken.None);

        // Assert
        result.Should().BeNull();

        // Stooq should NOT be called for EU stocks
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}
