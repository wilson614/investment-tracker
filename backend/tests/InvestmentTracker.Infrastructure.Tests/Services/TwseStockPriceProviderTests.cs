using System.Net;
using System.Text;
using FluentAssertions;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class TwseStockPriceProviderTests
{
    [Fact]
    public async Task GetQuoteAsync_TseNoDataThenOtcValid_FallbacksToOtcQuote()
    {
        // Arrange
        var symbol = "6488"; // OTC symbol

        var tseNoDataJson = """
        {
          "msgArray": [
            { "tv": "-", "s": "-", "c": "", "z": "-" }
          ]
        }
        """;

        var otcValidJson = """
        {
          "msgArray": [
            {
              "z": "445.5000",
              "n": "環球晶",
              "y": "491.5000"
            }
          ]
        }
        """;

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tseNoDataJson, Encoding.UTF8, "application/json")
            },
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(otcValidJson, Encoding.UTF8, "application/json")
            }
        ]);

        var httpClient = new HttpClient(handler);

        var limiter = new Mock<ITwseRateLimiter>(MockBehavior.Strict);
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<TwseStockPriceProvider>>().Object;

        var sut = new TwseStockPriceProvider(httpClient, limiter.Object, logger);

        // Act
        var quote = await sut.GetQuoteAsync(StockMarket.TW, symbol, CancellationToken.None);

        // Assert
        quote.Should().NotBeNull();
        quote!.Symbol.Should().Be("6488");
        quote.Name.Should().Be("環球晶");
        quote.Source.Should().Be("TPEx");
        quote.Price.Should().Be(445.5m);
        quote.Change.Should().Be(445.5m - 491.5m);

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.Query.Should().Contain("ex_ch=tse_6488.tw");
        handler.Requests[1].RequestUri!.Query.Should().Contain("ex_ch=otc_6488.tw");

        limiter.Verify(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetQuoteAsync_TseUpstreamError_DoesNotFallbackToOtc_AndReturnsNull()
    {
        // Arrange
        var symbol = "6488";

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("upstream failure", Encoding.UTF8, "text/plain")
            },
            // Should not be called if logic is correct
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                {
                  "msgArray": [
                    {
                      "z": "445.5000",
                      "n": "環球晶",
                      "y": "491.5000"
                    }
                  ]
                }
                """, Encoding.UTF8, "application/json")
            }
        ]);

        var httpClient = new HttpClient(handler);

        var limiter = new Mock<ITwseRateLimiter>(MockBehavior.Strict);
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<TwseStockPriceProvider>>().Object;

        var sut = new TwseStockPriceProvider(httpClient, limiter.Object, logger);

        // Act
        var quote = await sut.GetQuoteAsync(StockMarket.TW, symbol, CancellationToken.None);

        // Assert
        quote.Should().BeNull();

        handler.Requests.Should().HaveCount(1, "TSE upstream error should stop fallback to OTC");
        handler.Requests[0].RequestUri!.Query.Should().Contain("ex_ch=tse_6488.tw");

        limiter.Verify(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetQuoteAsync_TseValid_DoesNotFallbackToOtc()
    {
        // Arrange
        var symbol = "2330";

        var tseValidJson = """
        {
          "msgArray": [
            {
              "z": "1915.0000",
              "n": "台積電",
              "y": "1890.0000"
            }
          ]
        }
        """;

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(tseValidJson, Encoding.UTF8, "application/json")
            }
        ]);

        var httpClient = new HttpClient(handler);

        var limiter = new Mock<ITwseRateLimiter>(MockBehavior.Strict);
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<TwseStockPriceProvider>>().Object;

        var sut = new TwseStockPriceProvider(httpClient, limiter.Object, logger);

        // Act
        var quote = await sut.GetQuoteAsync(StockMarket.TW, symbol, CancellationToken.None);

        // Assert
        quote.Should().NotBeNull();
        quote!.Source.Should().Be("TWSE");
        quote.Symbol.Should().Be("2330");
        quote.Name.Should().Be("台積電");

        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.Query.Should().Contain("ex_ch=tse_2330.tw");

        limiter.Verify(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private sealed class SequenceHttpMessageHandler(IReadOnlyList<HttpResponseMessage> responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (_responses.Count == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            return Task.FromResult(_responses.Dequeue());
        }
    }
}
