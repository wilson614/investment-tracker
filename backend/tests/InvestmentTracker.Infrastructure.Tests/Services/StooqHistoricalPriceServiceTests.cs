using System.Net;
using FluentAssertions;
using InvestmentTracker.Infrastructure.MarketData;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class StooqHistoricalPriceServiceTests
{
    [Fact]
    public async Task GetExchangeRateAsync_SameCurrency_ReturnsOne_AndDoesNotCallHttp()
    {
        // Arrange
        var date = new DateOnly(2024, 2, 1);
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new InvalidOperationException("HTTP should not be called for same-currency FX lookup."));

        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<StooqHistoricalPriceService>>().Object;
        var sut = new StooqHistoricalPriceService(httpClient, logger);

        // Act
        var result = await sut.GetExchangeRateAsync("twd", "TWD", date, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Rate.Should().Be(1m);
        result.ActualDate.Should().Be(date);
        result.FromCurrency.Should().Be("TWD");
        result.ToCurrency.Should().Be("TWD");
        handler.CallCount.Should().Be(0);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responseFactory = responseFactory;

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responseFactory(request, cancellationToken));
        }
    }
}
