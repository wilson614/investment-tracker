using System.Collections.Concurrent;
using FluentAssertions;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Enums;
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
        var stockCacheTicker = $"{ticker}|US";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(105.25m, new DateOnly(2023, 12, 29), "USD"));

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.US, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Price.Should().Be(105.25m);
        result.FromCache.Should().BeFalse();

        // Verify cache was updated
        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d =>
                d.Ticker == stockCacheTicker && d.Year == year && d.Value == 105.25m),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_SecondCall_UsesCachedValue()
    {
        // Arrange
        var ticker = "VT";
        var stockCacheTicker = $"{ticker}|US";
        var year = 2023;

        _repositoryMock
            .SetupSequence(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null) // first call pre-lock
            .ReturnsAsync((HistoricalYearEndData?)null) // first call in-lock
            .ReturnsAsync(HistoricalYearEndData.CreateStockPrice(
                stockCacheTicker,
                year,
                105.25m,
                "USD",
                new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
                "Stooq")); // second call pre-lock cache hit

        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(105.25m, new DateOnly(2023, 12, 29), "USD"));

        // Act - First call
        var result1 = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.US, CancellationToken.None);
        // Act - Second call
        var result2 = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.US, CancellationToken.None);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
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
    public async Task GetOrFetchYearEndPriceAsync_TaiwanStock_YahooHit_AvoidsTwseFallback()
    {
        // Arrange
        var ticker = "2330";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 590.00m,
                ActualDate = new DateOnly(2023, 12, 29),
                Currency = "TWD"
            });

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(590.00m);
        result.Currency.Should().Be("TWD");
        result.Source.Should().Be("Yahoo");

        // Verify Yahoo hit short-circuits TWSE fallback
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _twseServiceMock.Verify(
            x => x.GetYearEndPriceAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_TaiwanStock_YahooMiss_FallsBackToTwseOrTpex()
    {
        // Arrange
        var ticker = "2330";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);
        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _twseServiceMock
            .Setup(x => x.GetYearEndPriceAsync("2330", year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwseStockPriceResult(580.00m, new DateOnly(2023, 12, 29), "2330", "TPEx"));

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(580.00m);
        result.Currency.Should().Be("TWD");
        result.Source.Should().Be("TPEx");

        // Verify Yahoo first, then TWSE/TPEx fallback, no Stooq
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("2330.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("2330.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _twseServiceMock.Verify(
            x => x.GetYearEndPriceAsync("2330", year, It.IsAny<CancellationToken>()),
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
        var currencyPair = "USD:TWD";
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
        var currencyPair = "USD:TWD";
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

    [Fact]
    public async Task GetOrFetchYearEndExchangeRateAsync_ConcurrentCacheMiss_DeduplicatesFetchAndCacheWrite()
    {
        // Arrange
        var fromCurrency = "USD";
        var toCurrency = "TWD";
        var currencyPair = "USD:TWD";
        var year = 2023;

        _repositoryMock
            .SetupSequence(x => x.GetExchangeRateAsync(currencyPair, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null) // call 1: task A pre-lock
            .ReturnsAsync((HistoricalYearEndData?)null) // call 2: task B pre-lock (or task A in-lock)
            .ReturnsAsync((HistoricalYearEndData?)null) // call 3: first in-lock miss
            .ReturnsAsync(HistoricalYearEndData.CreateExchangeRate(
                currencyPair,
                year,
                31.25m,
                new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
                "Stooq")); // call 4: second in-lock hit

        _stooqServiceMock
            .Setup(x => x.GetExchangeRateAsync(fromCurrency, toCurrency,
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqExchangeRateResult(31.25m, new DateOnly(2023, 12, 29), fromCurrency, toCurrency));

        // Act
        var tasks = Enumerable.Range(0, 2)
            .Select(_ => _service.GetOrFetchYearEndExchangeRateAsync(fromCurrency, toCurrency, year, CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().NotContainNulls();
        _stooqServiceMock.Verify(
            x => x.GetExchangeRateAsync(fromCurrency, toCurrency,
                It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            x => x.AddAsync(It.IsAny<HistoricalYearEndData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Yahoo-Stooq Fallback Tests (T114)

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_USStock_TriesYahooFirst()
    {
        // Arrange
        var ticker = "VT";
        var stockCacheTicker = $"{ticker}|US";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
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
    public async Task GetOrFetchYearEndPriceAsync_YahooFails_UsMarket_FallsBackToStooq()
    {
        // Arrange
        var ticker = "VT";
        var stockCacheTicker = $"{ticker}|US";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
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
        var stockCacheTicker = $"{ticker}|US";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
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
        var stockCacheTicker = $"{ticker}|UK";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
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
    public async Task GetOrFetchYearEndPriceAsync_ConcurrentCacheMiss_DeduplicatesFetch()
    {
        // Arrange
        var ticker = "VT";
        var stockCacheTicker = $"{ticker}|US";
        var year = 2023;

        _repositoryMock
            .SetupSequence(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null) // call 1: task A pre-lock
            .ReturnsAsync((HistoricalYearEndData?)null) // call 2: task B pre-lock
            .ReturnsAsync((HistoricalYearEndData?)null) // call 3: task A inside lock
            .ReturnsAsync(HistoricalYearEndData.CreateStockPrice(
                stockCacheTicker,
                year,
                105.25m,
                "USD",
                new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
                "Yahoo")); // call 4: task B inside lock should hit cache

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 105.25m,
                ActualDate = new DateOnly(2023, 12, 29),
                Currency = "USD"
            });

        // Act
        var tasks = Enumerable.Range(0, 2)
            .Select(_ => _service.GetOrFetchYearEndPriceAsync(ticker, year, Domain.Enums.StockMarket.US, CancellationToken.None));

        var results = await Task.WhenAll(tasks);

        // Assert
        results.Should().NotContainNulls();
        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            x => x.AddAsync(It.IsAny<HistoricalYearEndData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveManualPriceAsync_NormalizesTicker_ForExistsCheckAndSave()
    {
        // Arrange
        var rawTicker = "  vt  ";
        var normalizedTicker = "VT";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.ExistsAsync(HistoricalDataType.StockPrice, It.IsAny<string>(), year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<HistoricalYearEndData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData d, CancellationToken _) => d);

        // Act
        var result = await _service.SaveManualPriceAsync(
            rawTicker,
            year,
            100.50m,
            "USD",
            new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        // Assert
        result.Source.Should().Be("Manual");

        _repositoryMock.Verify(
            x => x.ExistsAsync(HistoricalDataType.StockPrice, normalizedTicker, year, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            x => x.ExistsAsync(HistoricalDataType.StockPrice, "VT|TW", year, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            x => x.ExistsAsync(HistoricalDataType.StockPrice, "VT|US", year, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            x => x.ExistsAsync(HistoricalDataType.StockPrice, "VT|UK", year, It.IsAny<CancellationToken>()),
            Times.Once);
        _repositoryMock.Verify(
            x => x.ExistsAsync(HistoricalDataType.StockPrice, "VT|EU", year, It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d => d.Ticker == normalizedTicker && d.Year == year), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveManualPriceAsync_WhenMarketScopedEntryExists_ThrowsConflictAndDoesNotInsert()
    {
        // Arrange
        var ticker = "vt";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.ExistsAsync(HistoricalDataType.StockPrice, It.IsAny<string>(), year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _repositoryMock
            .Setup(x => x.ExistsAsync(HistoricalDataType.StockPrice, "VT|US", year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        Func<Task> act = async () => await _service.SaveManualPriceAsync(
            ticker,
            year,
            100.50m,
            "USD",
            new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*VT|US/{year}*");

        _repositoryMock.Verify(
            x => x.AddAsync(It.IsAny<HistoricalYearEndData>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SaveManualPriceAsync_SavedLegacyKey_IsRetrievableViaMarketScopedReadPath()
    {
        // Arrange
        var year = 2023;
        var store = new Dictionary<string, HistoricalYearEndData>(StringComparer.Ordinal);

        _repositoryMock
            .Setup(x => x.ExistsAsync(HistoricalDataType.StockPrice, It.IsAny<string>(), year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalDataType _, string key, int _, CancellationToken _) => store.ContainsKey(key));

        _repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<HistoricalYearEndData>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData d, CancellationToken _) =>
            {
                store[d.Ticker] = d;
                return d;
            });

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(It.IsAny<string>(), year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, int _, CancellationToken _) =>
                store.TryGetValue(key.Trim().ToUpperInvariant(), out var entry) ? entry : null);

        // Act
        await _service.SaveManualPriceAsync(
            " vt ",
            year,
            100.50m,
            "USD",
            new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
            CancellationToken.None);

        var result = await _service.GetOrFetchYearEndPriceAsync("VT", year, StockMarket.US, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(100.50m);
        result.Source.Should().Be("Manual");
        result.FromCache.Should().BeTrue();

        _repositoryMock.Verify(
            x => x.GetStockPriceAsync("VT|US", year, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _repositoryMock.Verify(
            x => x.GetStockPriceAsync("VT", year, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_EuStock_YahooFails_DoesNotFallbackToStooq()
    {
        // Arrange - EU 市場 Yahoo 失敗時不得 fallback 到 Stooq
        var ticker = "AGAC";
        var stockCacheTicker = $"{ticker}|EU";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        // Yahoo fails
        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, Domain.Enums.StockMarket.EU, CancellationToken.None);

        // Assert
        result.Should().BeNull();

        // Stooq should NOT be called for EU market
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_UkMarket_YahooFails_FallsBackToStooq()
    {
        // Arrange
        var ticker = "VWRL";
        var stockCacheTicker = $"{ticker}|UK";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("VWRL.L", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StooqPriceResult(85.10m, new DateOnly(2023, 12, 29), "GBP"));

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.UK, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(85.10m);
        result.Source.Should().Be("Stooq");

        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync("VWRL.L", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);

        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_TwMarket_YahooFails_DoesNotFallbackToStooq()
    {
        // Arrange
        var ticker = "ABC";
        var stockCacheTicker = $"{ticker}|TW";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync($"{ticker}.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync($"{ticker}.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _twseServiceMock
            .Setup(x => x.GetYearEndPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TwseStockPriceResult?)null);

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.TW, CancellationToken.None);

        // Assert
        result.Should().BeNull();

        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_NullMarket_YahooFails_DoesNotFallbackToStooq()
    {
        // Arrange
        var ticker = "VT";
        var stockCacheTicker = ticker;
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _stooqServiceMock
            .Setup(x => x.GetStockPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((StooqPriceResult?)null);

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result.Should().BeNull();

        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_UsAndTwMarkets_UseDifferentCacheKeys()
    {
        // Arrange
        var ticker = "ABC";
        var usCacheTicker = "ABC|US";
        var twCacheTicker = "ABC|TW";
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(usCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(twCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 10.50m,
                ActualDate = new DateOnly(2023, 12, 29),
                Currency = "USD"
            });

        _twseServiceMock
            .Setup(x => x.GetYearEndPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwseStockPriceResult(300m, new DateOnly(2023, 12, 29), ticker));

        // Act
        var usResult = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.US, CancellationToken.None);
        var twResult = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.TW, CancellationToken.None);

        // Assert
        usResult.Should().NotBeNull();
        twResult.Should().NotBeNull();

        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d => d.Ticker == usCacheTicker && d.Year == year), It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d => d.Ticker == twCacheTicker && d.Year == year), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_MarketScopedCacheMiss_FallsBackToLegacyTickerCache()
    {
        // Arrange
        var ticker = "VT";
        var year = 2023;
        var legacyEntry = HistoricalYearEndData.CreateStockPrice(
            ticker,
            year,
            123.45m,
            "USD",
            new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
            "Legacy");

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync("VT|US", year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync("VT", year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legacyEntry);

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.US, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(123.45m);
        result.FromCache.Should().BeTrue();

        _yahooServiceMock.Verify(
            x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndExchangeRateAsync_NormalizesCurrencies_UsesDelimitedKey()
    {
        // Arrange
        var year = 2023;
        var normalizedPair = "USD:TWD";
        var cachedEntry = HistoricalYearEndData.CreateExchangeRate(
            normalizedPair,
            year,
            30.75m,
            new DateTime(2023, 12, 29, 0, 0, 0, DateTimeKind.Utc),
            "Stooq");

        _repositoryMock
            .Setup(x => x.GetExchangeRateAsync(normalizedPair, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedEntry);

        // Act
        var result = await _service.GetOrFetchYearEndExchangeRateAsync(" usd ", " twd ", year, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.CurrencyPair.Should().Be(normalizedPair);
        result.FromCache.Should().BeTrue();

        _repositoryMock.Verify(
            x => x.GetExchangeRateAsync(normalizedPair, year, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_NumericPrefixTicker_TreatedAsTaiwanStock()
    {
        // Arrange
        var ticker = "00632R";
        var stockCacheTicker = ticker;
        var year = 2023;

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(stockCacheTicker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalYearEndData?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("00632R.TW", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync("00632R.TWO", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);

        _twseServiceMock
            .Setup(x => x.GetYearEndPriceAsync("00632R", year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwseStockPriceResult(18.88m, new DateOnly(2023, 12, 29), "00632R", "TPEx"));

        // Act
        var result = await _service.GetOrFetchYearEndPriceAsync(ticker, year, market: null, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(18.88m);
        result.Source.Should().Be("TPEx");

        _twseServiceMock.Verify(
            x => x.GetYearEndPriceAsync("00632R", year, It.IsAny<CancellationToken>()),
            Times.Once);
        _stooqServiceMock.Verify(
            x => x.GetStockPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchYearEndPriceAsync_ConcurrentSameTickerDifferentMarkets_DoNotBlockEachOther()
    {
        // Arrange
        var ticker = "ABC";
        var year = 2023;
        var usCacheTicker = "ABC|US";
        var twCacheTicker = "ABC|TW";
        var hitCounter = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        _repositoryMock
            .Setup(x => x.GetStockPriceAsync(It.IsAny<string>(), year, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string cacheKey, int _, CancellationToken _) =>
            {
                hitCounter.AddOrUpdate(cacheKey, 1, (_, current) => current + 1);
                return null;
            });

        _yahooServiceMock
            .Setup(x => x.GetHistoricalPriceAsync(ticker, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 11m,
                ActualDate = new DateOnly(2023, 12, 29),
                Currency = "USD"
            });

        _twseServiceMock
            .Setup(x => x.GetYearEndPriceAsync(ticker, year, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TwseStockPriceResult(22m, new DateOnly(2023, 12, 29), ticker));

        // Act
        await Task.WhenAll(
            _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.US, CancellationToken.None),
            _service.GetOrFetchYearEndPriceAsync(ticker, year, StockMarket.TW, CancellationToken.None));

        // Assert
        hitCounter.ContainsKey(usCacheTicker).Should().BeTrue();
        hitCounter.ContainsKey(twCacheTicker).Should().BeTrue();

        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d => d.Ticker == usCacheTicker && d.Year == year), It.IsAny<CancellationToken>()),
            Times.Once);

        _repositoryMock.Verify(
            x => x.AddAsync(It.Is<HistoricalYearEndData>(d => d.Ticker == twCacheTicker && d.Year == year), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
