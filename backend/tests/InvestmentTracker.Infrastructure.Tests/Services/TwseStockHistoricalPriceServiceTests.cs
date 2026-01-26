using System.Net;
using System.Text;
using FluentAssertions;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class TwseStockHistoricalPriceServiceTests
{
    [Fact]
    public async Task GetStockPriceAsync_EarlyMonth_NoTradingDayInMonth_FallsBackToPreviousMonth()
    {
        // Arrange
        var targetDate = new DateOnly(2020, 10, 4); // 10/1-10/4 連假，10/4 沒有交易日
        var stockNo = "0050";

        // First request: October month data, no <= 10/04 trading day -> service should fall back
        var octJson = BuildStockDayJsonOk(dataRows: []);

        // Second request: September month data contains 09/30 (<= 09/30)
        var sepJson = BuildStockDayJsonOk(dataRows:
        [
            // [日期, 成交股數, 成交金額, 開盤價, 最高價, 最低價, 收盤價, 漲跌價差, 成交筆數]
            ["109/09/30", "0", "0", "0", "0", "0", "100.00", "0", "0"]
        ]);

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(octJson, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(sepJson, Encoding.UTF8, "application/json") }
        ]);

        var httpClient = new HttpClient(handler);
        var limiter = new Mock<ITwseRateLimiter>();
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(httpClient, limiter.Object, logger);

        // Act
        var result = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(100m);
        result.ActualDate.Should().Be(new DateOnly(2020, 9, 30));

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.Query.Should().Contain("date=20201001");
        handler.Requests[1].RequestUri!.Query.Should().Contain("date=20200901");
    }

    [Fact]
    public async Task GetStockPriceAsync_NotEarlyMonth_NoTradingDayInMonth_DoesNotFallback()
    {
        // Arrange
        var targetDate = new DateOnly(2020, 10, 15);
        var stockNo = "0050";

        var octJson = BuildStockDayJsonOk(dataRows: []);

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(octJson, Encoding.UTF8, "application/json") }
        ]);

        var httpClient = new HttpClient(handler);
        var limiter = new Mock<ITwseRateLimiter>();
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(httpClient, limiter.Object, logger);

        // Act
        var result = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.Query.Should().Contain("date=20201001");
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

    private static string BuildStockDayJsonOk(IReadOnlyList<IReadOnlyList<string>> dataRows)
    {
        var rowsJson = string.Join(",", dataRows.Select(row =>
            "[" + string.Join(",", row.Select(v => $"\"{v}\"")) + "]"));

        return $$"""
{
  "stat": "OK",
  "data": [{{rowsJson}}]
}
""";
    }
}
