using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class TransactionDateExchangeRateServiceTests
{
    [Fact]
    public async Task GetOrFetchAsync_SameCurrency_ReturnsIdentityRate_WithoutCallingDependencies()
    {
        // Arrange
        var repositoryMock = new Mock<IHistoricalExchangeRateCacheRepository>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<TransactionDateExchangeRateService>>();

        var sut = new TransactionDateExchangeRateService(
            repositoryMock.Object,
            yahooServiceMock.Object,
            stooqServiceMock.Object,
            loggerMock.Object);

        var transactionDate = new DateTime(2024, 1, 15, 14, 30, 0, DateTimeKind.Local);

        // Act
        var result = await sut.GetOrFetchAsync("TWD", "TWD", transactionDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Rate.Should().Be(1.0m);
        result.CurrencyPair.Should().Be("TWDTWD");
        result.Source.Should().Be("SameCurrency");
        result.FromCache.Should().BeFalse();
        result.RequestedDate.Should().Be(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        result.ActualDate.Should().Be(new DateTime(2024, 1, 15, 0, 0, 0, DateTimeKind.Utc));

        repositoryMock.Verify(
            x => x.GetAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repositoryMock.Verify(
            x => x.AddAsync(It.IsAny<HistoricalExchangeRateCache>(), It.IsAny<CancellationToken>()),
            Times.Never);
        yahooServiceMock.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        stooqServiceMock.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchAsync_SameCurrency_IgnoresCase_WhenShortCircuiting()
    {
        // Arrange
        var repositoryMock = new Mock<IHistoricalExchangeRateCacheRepository>(MockBehavior.Strict);
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>(MockBehavior.Strict);
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>(MockBehavior.Strict);
        var loggerMock = new Mock<ILogger<TransactionDateExchangeRateService>>();

        var sut = new TransactionDateExchangeRateService(
            repositoryMock.Object,
            yahooServiceMock.Object,
            stooqServiceMock.Object,
            loggerMock.Object);

        // Act
        var result = await sut.GetOrFetchAsync(
            "twd",
            "TWD",
            new DateTime(2024, 2, 20, 9, 0, 0, DateTimeKind.Unspecified),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Rate.Should().Be(1.0m);
        result.CurrencyPair.Should().Be("TWDTWD");
        result.Source.Should().Be("SameCurrency");

        repositoryMock.Verify(
            x => x.GetAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        yahooServiceMock.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
        stooqServiceMock.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrFetchAsync_DifferentCurrencies_UsesNormalCacheAndFetchFlow()
    {
        // Arrange
        var repositoryMock = new Mock<IHistoricalExchangeRateCacheRepository>();
        var yahooServiceMock = new Mock<IYahooHistoricalPriceService>();
        var stooqServiceMock = new Mock<IStooqHistoricalPriceService>();
        var loggerMock = new Mock<ILogger<TransactionDateExchangeRateService>>();

        repositoryMock
            .Setup(x => x.GetAsync("USDTWD", new DateTime(2024, 3, 8, 0, 0, 0, DateTimeKind.Utc), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalExchangeRateCache?)null);

        yahooServiceMock
            .Setup(x => x.GetExchangeRateAsync("USD", "TWD", new DateOnly(2024, 3, 8), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooExchangeRateResult
            {
                Rate = 31.25m,
                ActualDate = new DateOnly(2024, 3, 8),
                CurrencyPair = "USDTWD"
            });

        repositoryMock
            .Setup(x => x.AddAsync(It.IsAny<HistoricalExchangeRateCache>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HistoricalExchangeRateCache cacheEntry, CancellationToken _) => cacheEntry);

        var sut = new TransactionDateExchangeRateService(
            repositoryMock.Object,
            yahooServiceMock.Object,
            stooqServiceMock.Object,
            loggerMock.Object);

        // Act
        var result = await sut.GetOrFetchAsync(
            "USD",
            "TWD",
            new DateTime(2024, 3, 8, 16, 45, 0, DateTimeKind.Utc),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Rate.Should().Be(31.25m);
        result.CurrencyPair.Should().Be("USDTWD");
        result.Source.Should().Be("Yahoo");
        result.FromCache.Should().BeFalse();

        repositoryMock.Verify(
            x => x.GetAsync("USDTWD", new DateTime(2024, 3, 8, 0, 0, 0, DateTimeKind.Utc), It.IsAny<CancellationToken>()),
            Times.Once);
        yahooServiceMock.Verify(
            x => x.GetExchangeRateAsync("USD", "TWD", new DateOnly(2024, 3, 8), It.IsAny<CancellationToken>()),
            Times.Once);
        repositoryMock.Verify(
            x => x.AddAsync(
                It.Is<HistoricalExchangeRateCache>(cache =>
                    cache.CurrencyPair == "USDTWD" &&
                    cache.RequestedDate == new DateTime(2024, 3, 8, 0, 0, 0, DateTimeKind.Utc) &&
                    cache.ActualDate == new DateTime(2024, 3, 8, 0, 0, 0, DateTimeKind.Utc) &&
                    cache.Rate == 31.25m &&
                    cache.Source == "Yahoo"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        stooqServiceMock.Verify(
            x => x.GetExchangeRateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
