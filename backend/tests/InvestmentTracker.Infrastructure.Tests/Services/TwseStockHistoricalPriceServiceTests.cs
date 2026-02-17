using System.Net;
using System.Text;
using FluentAssertions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Caching.Memory;
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

        var yahooMock = new Mock<IYahooHistoricalPriceService>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(
            httpClient,
            limiter.Object,
            yahooMock.Object,
            memoryCache,
            logger);

        // Act
        var result = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(100m);
        result.ActualDate.Should().Be(new DateOnly(2020, 9, 30));
        result.Source.Should().Be("TWSE");

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

        var yahooMock = new Mock<IYahooHistoricalPriceService>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(
            httpClient,
            limiter.Object,
            yahooMock.Object,
            memoryCache,
            logger);

        // Act
        var result = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].RequestUri!.Query.Should().Contain("date=20201001");
    }

    [Fact]
    public async Task GetStockPriceAsync_TwseNoData_FallsBackToTpexAndReturnsOtcPrice()
    {
        // Arrange
        var targetDate = new DateOnly(2024, 1, 31);
        var stockNo = "8069";

        var twseNoDataJson = BuildStockDayJsonNoData();
        var tpexJson = BuildTpexDailyQuotesJson(
            targetDate,
            [
                ["8069", "元太", "208.00", "-4.00 ", "217.00", "219.50", "208.00"]
            ]);

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(twseNoDataJson, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tpexJson, Encoding.UTF8, "application/json") }
        ]);

        var httpClient = new HttpClient(handler);
        var limiter = new Mock<ITwseRateLimiter>();
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var yahooMock = new Mock<IYahooHistoricalPriceService>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(
            httpClient,
            limiter.Object,
            yahooMock.Object,
            memoryCache,
            logger);

        // Act
        var result = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(208.00m);
        result.ActualDate.Should().Be(targetDate);
        result.Source.Should().Be("TPEx");

        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.Host.Should().Be("www.twse.com.tw");
        handler.Requests[0].RequestUri!.Query.Should().Contain("stockNo=8069");

        handler.Requests[1].RequestUri!.Host.Should().Be("www.tpex.org.tw");
        handler.Requests[1].RequestUri!.Query.Should().Contain("id=8069");
        handler.Requests[1].RequestUri!.Query.Should().Contain("date=2024%2F01%2F31");
    }

    [Fact]
    public async Task GetStockPriceAsync_TwseNoData_TpexTargetDateNoData_LooksBackAndReturnsPreviousTradingDay()
    {
        // Arrange
        var targetDate = new DateOnly(2024, 1, 1); // 假日
        var previousTradingDay = new DateOnly(2023, 12, 29);
        var stockNo = "8069";

        var twseNoDataJson = BuildStockDayJsonNoData();
        var tpexHolidayJson = BuildTpexDailyQuotesJson(targetDate, dataRows: []);
        var tpexDec31Json = BuildTpexDailyQuotesJson(new DateOnly(2023, 12, 31), dataRows: []);
        var tpexDec30Json = BuildTpexDailyQuotesJson(new DateOnly(2023, 12, 30), dataRows: []);
        var tpexTradingDayJson = BuildTpexDailyQuotesJson(
            previousTradingDay,
            [
                ["8069", "元太", "197.00", "-4.50 ", "201.50", "201.50", "197.00"]
            ]);

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(twseNoDataJson, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tpexHolidayJson, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tpexDec31Json, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tpexDec30Json, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tpexTradingDayJson, Encoding.UTF8, "application/json") }
        ]);

        var httpClient = new HttpClient(handler);
        var limiter = new Mock<ITwseRateLimiter>();
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var yahooMock = new Mock<IYahooHistoricalPriceService>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(
            httpClient,
            limiter.Object,
            yahooMock.Object,
            memoryCache,
            logger);

        // Act
        var result = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(197.00m);
        result.ActualDate.Should().Be(previousTradingDay);
        result.Source.Should().Be("TPEx");

        handler.Requests.Should().HaveCount(5);

        handler.Requests[1].RequestUri!.Host.Should().Be("www.tpex.org.tw");
        handler.Requests[1].RequestUri!.Query.Should().Contain("date=2024%2F01%2F01");

        handler.Requests[2].RequestUri!.Host.Should().Be("www.tpex.org.tw");
        handler.Requests[2].RequestUri!.Query.Should().Contain("date=2023%2F12%2F31");

        handler.Requests[3].RequestUri!.Host.Should().Be("www.tpex.org.tw");
        handler.Requests[3].RequestUri!.Query.Should().Contain("date=2023%2F12%2F30");

        handler.Requests[4].RequestUri!.Host.Should().Be("www.tpex.org.tw");
        handler.Requests[4].RequestUri!.Query.Should().Contain("date=2023%2F12%2F29");
    }

    [Fact]
    public async Task GetStockPriceAsync_TwseAndTpexMiss_UsesYahooTwAndTwoFallback()
    {
        // Arrange
        var targetDate = new DateOnly(2024, 1, 31);
        var stockNo = "8069";

        var twseNoDataJson = BuildStockDayJsonNoData();
        var tpexNoDataJson = BuildTpexDailyQuotesJson(targetDate, dataRows: []);

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(twseNoDataJson, Encoding.UTF8, "application/json") },
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(tpexNoDataJson, Encoding.UTF8, "application/json") }
        ]);

        var httpClient = new HttpClient(handler);
        var limiter = new Mock<ITwseRateLimiter>();
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var yahooMock = new Mock<IYahooHistoricalPriceService>();
        yahooMock
            .Setup(x => x.GetHistoricalPriceAsync("8069.TW", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync((YahooHistoricalPriceResult?)null);
        yahooMock
            .Setup(x => x.GetHistoricalPriceAsync("8069.TWO", targetDate, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new YahooHistoricalPriceResult
            {
                Price = 210.5m,
                ActualDate = targetDate,
                Currency = "TWD"
            });

        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(
            httpClient,
            limiter.Object,
            yahooMock.Object,
            memoryCache,
            logger);

        // Act
        var result = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Price.Should().Be(210.5m);
        result.ActualDate.Should().Be(targetDate);
        result.Source.Should().Be("Yahoo:8069.TWO");

        yahooMock.Verify(
            x => x.GetHistoricalPriceAsync("8069.TW", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);
        yahooMock.Verify(
            x => x.GetHistoricalPriceAsync("8069.TWO", targetDate, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetStockPriceAsync_SameKeySecondCall_HitsMemoryCache_AndSkipsExternalCalls()
    {
        // Arrange
        var targetDate = new DateOnly(2024, 1, 31);
        var stockNo = "2330";

        var twseJson = BuildStockDayJsonOk(
            [
                ["113/01/31", "0", "0", "0", "0", "0", "600.00", "0", "0"]
            ]);

        var handler = new SequenceHttpMessageHandler([
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(twseJson, Encoding.UTF8, "application/json") }
        ]);

        var httpClient = new HttpClient(handler);
        var limiter = new Mock<ITwseRateLimiter>();
        limiter.Setup(x => x.WaitForSlotAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var yahooMock = new Mock<IYahooHistoricalPriceService>();
        var memoryCache = new MemoryCache(new MemoryCacheOptions());

        var logger = new Mock<ILogger<TwseStockHistoricalPriceService>>().Object;
        var sut = new TwseStockHistoricalPriceService(
            httpClient,
            limiter.Object,
            yahooMock.Object,
            memoryCache,
            logger);

        // Act
        var first = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);
        var second = await sut.GetStockPriceAsync(stockNo, targetDate, CancellationToken.None);

        // Assert
        first.Should().NotBeNull();
        second.Should().NotBeNull();
        second!.Price.Should().Be(first!.Price);

        handler.Requests.Should().HaveCount(1, "second call should hit memory cache");
        yahooMock.Verify(
            x => x.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    private static string BuildStockDayJsonNoData()
    {
        return """
{
  "stat": "很抱歉，沒有符合條件的資料!"
}
""";
    }

    private static string BuildTpexDailyQuotesJson(DateOnly date, IReadOnlyList<IReadOnlyList<string>> dataRows)
    {
        var rowsJson = string.Join(",", dataRows.Select(row =>
            "[" + string.Join(",", row.Select(v => $"\"{v}\"")) + "]"));

        var dateString = date.ToString("yyyyMMdd");
        var rocYear = date.Year - 1911;
        var rocDate = $"{rocYear:D3}/{date.Month:D2}/{date.Day:D2}";

        return $$"""
{
  "date": "{{dateString}}",
  "stat": "ok",
  "tables": [
    {
      "date": "{{rocDate}}",
      "totalCount": {{dataRows.Count}},
      "data": [{{rowsJson}}]
    }
  ]
}
""";
    }
}
