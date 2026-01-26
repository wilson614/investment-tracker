using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using InvestmentTracker.Infrastructure.MarketData;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class YahooHistoricalPriceServiceTests
{
    [Fact]
    public async Task GetAnnualTotalReturnAsync_InvalidYear_ReturnsNull_AndDoesNotCallHttp()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<YahooHistoricalPriceService>>().Object;
        var sut = new YahooHistoricalPriceService(httpClient, logger);

        // Act
        var result = await sut.GetAnnualTotalReturnAsync("VT", year: 1999, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetAnnualTotalReturnAsync_NonSuccessStatus_ReturnsNull()
    {
        // Arrange
        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<YahooHistoricalPriceService>>().Object;
        var sut = new YahooHistoricalPriceService(httpClient, logger);

        // Act
        var result = await sut.GetAnnualTotalReturnAsync("VT", year: 2023, CancellationToken.None);

        // Assert
        result.Should().BeNull();
        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAnnualTotalReturnAsync_ChartError_ReturnsNull()
    {
        // Arrange
        var json = BuildChartJson(
            timestamps: [Unix(2023, 1, 3), Unix(2023, 12, 29)],
            close: [100m, 110m],
            adjclose: [100m, 112m],
            errorJson: "{ \"code\": \"Not Found\", \"description\": \"symbol not found\" }");

        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<YahooHistoricalPriceService>>().Object;
        var sut = new YahooHistoricalPriceService(httpClient, logger);

        // Act
        var result = await sut.GetAnnualTotalReturnAsync("VT", year: 2023, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAnnualTotalReturnAsync_MissingAdjclose_ReturnsNull()
    {
        // Arrange
        var json = BuildChartJson(
            timestamps: [Unix(2023, 1, 3), Unix(2023, 12, 29)],
            close: [100m, 110m],
            adjclose: null);

        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<YahooHistoricalPriceService>>().Object;
        var sut = new YahooHistoricalPriceService(httpClient, logger);

        // Act
        var result = await sut.GetAnnualTotalReturnAsync("VT", year: 2023, CancellationToken.None);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAnnualTotalReturnAsync_ValidJson_ReturnsTotalAndPriceReturn_AndBuildsExpectedUrl()
    {
        // Arrange
        var symbol = "VT";
        var year = 2023;

        var json = BuildChartJson(
            timestamps: [
                Unix(2022, 12, 30),
                Unix(2023, 1, 3),
                Unix(2023, 12, 29),
                Unix(2024, 1, 2)
            ],
            close: [99m, 100m, 110m, 111m],
            adjclose: [99m, 100m, 112m, 113m]);

        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<YahooHistoricalPriceService>>().Object;
        var sut = new YahooHistoricalPriceService(httpClient, logger);

        var startDate = new DateOnly(year, 1, 1);
        var endDate = new DateOnly(year, 12, 31);
        var period1 = new DateTimeOffset(startDate.AddDays(-7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var period2 = new DateTimeOffset(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var expectedUrl = $"https://query2.finance.yahoo.com/v8/finance/chart/{symbol}?period1={period1}&period2={period2}&interval=1d";

        // Act
        var result = await sut.GetAnnualTotalReturnAsync(symbol, year, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Year.Should().Be(year);
        result.TotalReturnPercent.Should().Be(12m);
        result.PriceReturnPercent.Should().Be(10m);

        handler.CallCount.Should().Be(1);
        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.ToString().Should().Be(expectedUrl);
        handler.LastRequest.Headers.Contains("User-Agent").Should().BeTrue();
    }

    [Fact]
    public async Task GetAnnualTotalReturnAsync_AdjcloseOnly_ReturnsTotalReturnWithNullPriceReturn()
    {
        // Arrange
        var json = BuildChartJson(
            timestamps: [Unix(2023, 1, 3), Unix(2023, 12, 29)],
            close: null,
            adjclose: [100m, 112m]);

        var handler = new StubHttpMessageHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handler);
        var logger = new Mock<ILogger<YahooHistoricalPriceService>>().Object;
        var sut = new YahooHistoricalPriceService(httpClient, logger);

        // Act
        var result = await sut.GetAnnualTotalReturnAsync("VT", year: 2023, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.TotalReturnPercent.Should().Be(12m);
        result.PriceReturnPercent.Should().BeNull();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _responseFactory = responseFactory;

        public int CallCount { get; private set; }

        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(_responseFactory(request, cancellationToken));
        }
    }

    private static long Unix(int year, int month, int day)
        => new DateTimeOffset(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static string BuildChartJson(
        IReadOnlyList<long> timestamps,
        IReadOnlyList<decimal?>? close,
        IReadOnlyList<decimal?>? adjclose,
        string errorJson = "null")
    {
        var tsJson = string.Join(",", timestamps);

        var indicatorParts = new List<string>();

        if (close != null)
        {
            var closeJson = string.Join(",", close.Select(ToJsonNumber));
            indicatorParts.Add($"\"quote\": [ {{ \"close\": [{closeJson}] }} ]");
        }

        if (adjclose != null)
        {
            var adjJson = string.Join(",", adjclose.Select(ToJsonNumber));
            indicatorParts.Add($"\"adjclose\": [ {{ \"adjclose\": [{adjJson}] }} ]");
        }

        var indicatorsJson = string.Join(",", indicatorParts);

        return $$"""
{
  "chart": {
    "result": [
      {
        "timestamp": [{{tsJson}}],
        "indicators": { {{indicatorsJson}} }
      }
    ],
    "error": {{errorJson}}
  }
}
""";
    }

    private static string ToJsonNumber(decimal? value)
        => value is null ? "null" : value.Value.ToString(CultureInfo.InvariantCulture);
}
