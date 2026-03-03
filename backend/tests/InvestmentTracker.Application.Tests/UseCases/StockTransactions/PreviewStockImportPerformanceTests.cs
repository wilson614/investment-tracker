using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Moq;

namespace InvestmentTracker.Application.Tests.UseCases.StockTransactions;

/// <summary>
/// Performance benchmark tests for broker preview parsing/normalization path (Speckit T041).
/// </summary>
public class PreviewStockImportPerformanceTests
{
    private const int BenchmarkRowCount = 500;
    private static readonly TimeSpan PreviewTarget = TimeSpan.FromSeconds(3);

    [Fact]
    public void ResolveSessionBaselineAnchorDate_BrokerStatementEarliestTradeJan2_ShouldAnchorToJan1WithoutJan1Guard()
    {
        // Arrange
        var method = typeof(PreviewStockImportUseCase).GetMethod(
            "ResolveSessionBaselineAnchorDate",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();

        var baseline = new StockImportSessionBaselineSnapshotDto
        {
            AsOfDate = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
            BaselineDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        IReadOnlyList<StockImportParsedRow> orderedRows =
        [
            new(
                RowNumber: 1,
                TradeDate: new DateTime(2026, 1, 2, 13, 30, 0, DateTimeKind.Utc),
                RawSecurityName: "台積電",
                Ticker: "2330",
                TradeSide: "sell",
                Quantity: 100m,
                UnitPrice: 165.5m,
                Fees: 6m,
                Taxes: 0m,
                NetSettlement: 16544m,
                Currency: "TWD",
                IsInvalid: false,
                ActionsRequired: [])
        ];

        // Act
        var anchor = (DateTime)method!.Invoke(null, [StockImportParser.FormatBrokerStatement, baseline, orderedRows])!;

        // Assert
        anchor.Should().Be(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        anchor.Should().NotBe(new DateTime(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc));
        anchor.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task ExecuteAsync_BrokerPreview500Rows_PerformanceBaseline_MedianElapsedShouldBeWithin3Seconds()
    {
        // Arrange
        var fixture = new Fixture();
        var csvContent = BuildBrokerStatementCsv(BenchmarkRowCount);
        var request = new PreviewStockImportRequest
        {
            PortfolioId = fixture.PortfolioId,
            CsvContent = csvContent,
            SelectedFormat = "broker_statement"
        };

        // Warm-up run to reduce one-time JIT/allocation noise in measured runs.
        var warmUpResult = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);
        AssertValidPreview(warmUpResult, BenchmarkRowCount);

        var measurements = new List<TimeSpan>(capacity: 3);

        // Act
        for (var i = 0; i < 3; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);
            stopwatch.Stop();

            AssertValidPreview(result, BenchmarkRowCount);
            measurements.Add(stopwatch.Elapsed);
        }

        var medianElapsed = GetMedian(measurements);
        var minElapsed = measurements.Min();
        var maxElapsed = measurements.Max();
        var sampleSeries = string.Join(", ", measurements.Select(value => value.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)));

        Console.WriteLine(
            "[PerfBaseline][PreviewStockImport] rows={0}; runs={1}; minMs={2:F2}; medianMs={3:F2}; maxMs={4:F2}; samplesMs=[{5}]",
            BenchmarkRowCount,
            measurements.Count,
            minElapsed.TotalMilliseconds,
            medianElapsed.TotalMilliseconds,
            maxElapsed.TotalMilliseconds,
            sampleSeries);

        // Assert
        medianElapsed.Should().BeLessThanOrEqualTo(
            PreviewTarget,
            $"500-row broker preview median elapsed {medianElapsed.TotalMilliseconds:F0} ms should be <= {PreviewTarget.TotalMilliseconds:F0} ms");

        fixture.SessionStoreMock.Verify(
            store => store.SaveAsync(It.IsAny<StockImportSessionSnapshotDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4)); // 1 warm-up + 3 measured runs
    }

    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public async Task ExecuteAsync_BrokerPreviewBenchmark_ShouldRecordColdAndWarmPathObservations(int rowCount)
    {
        // Arrange
        var fixture = new Fixture();
        var request = new PreviewStockImportRequest
        {
            PortfolioId = fixture.PortfolioId,
            CsvContent = BuildBrokerStatementCsv(rowCount),
            SelectedFormat = "broker_statement"
        };

        // Act
        var coldRun = await MeasurePreviewRunAsync(fixture.UseCase, request);
        AssertValidPreview(coldRun.Response, rowCount);

        var warmMeasurements = new List<TimeSpan>(capacity: 3);
        for (var i = 0; i < 3; i++)
        {
            var run = await MeasurePreviewRunAsync(fixture.UseCase, request);
            AssertValidPreview(run.Response, rowCount);
            warmMeasurements.Add(run.Elapsed);
        }

        var warmMedianElapsed = GetMedian(warmMeasurements);
        var warmMinElapsed = warmMeasurements.Min();
        var warmMaxElapsed = warmMeasurements.Max();
        var warmSampleSeries = string.Join(", ", warmMeasurements.Select(value => value.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)));

        Console.WriteLine(
            "[PerfObservation][PreviewStockImport] rows={0}; coldMs={1:F2}; warmRuns={2}; warmMinMs={3:F2}; warmMedianMs={4:F2}; warmMaxMs={5:F2}; warmSamplesMs=[{6}]",
            rowCount,
            coldRun.Elapsed.TotalMilliseconds,
            warmMeasurements.Count,
            warmMinElapsed.TotalMilliseconds,
            warmMedianElapsed.TotalMilliseconds,
            warmMaxElapsed.TotalMilliseconds,
            warmSampleSeries);

        // Assert
        coldRun.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
        warmMedianElapsed.Should().BeGreaterThan(TimeSpan.Zero);

        fixture.SessionStoreMock.Verify(
            store => store.SaveAsync(It.IsAny<StockImportSessionSnapshotDto>(), It.IsAny<CancellationToken>()),
            Times.Exactly(4)); // 1 cold + 3 warm observations
    }

    private static async Task<(StockImportPreviewResponseDto Response, TimeSpan Elapsed)> MeasurePreviewRunAsync(
        PreviewStockImportUseCase useCase,
        PreviewStockImportRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await useCase.ExecuteAsync(request, CancellationToken.None);
        stopwatch.Stop();

        return (response, stopwatch.Elapsed);
    }

    private static void AssertValidPreview(StockImportPreviewResponseDto response, int expectedRows)
    {
        response.Rows.Should().HaveCount(expectedRows);
        response.Errors.Should().BeEmpty();

        response.Summary.TotalRows.Should().Be(expectedRows);
        response.Summary.ValidRows.Should().Be(expectedRows);
        response.Summary.RequiresActionRows.Should().Be(0);
        response.Summary.InvalidRows.Should().Be(0);

        response.Rows.All(row => row.Status == "valid").Should().BeTrue();
        response.Rows.All(row => row.TradeSide == "buy").Should().BeTrue();
        response.Rows.All(row => row.ConfirmedTradeSide == "buy").Should().BeTrue();
    }

    private static TimeSpan GetMedian(IReadOnlyCollection<TimeSpan> values)
    {
        values.Should().NotBeEmpty();

        var ordered = values
            .OrderBy(value => value)
            .ToArray();

        return ordered[ordered.Length / 2];
    }

    private static string BuildBrokerStatementCsv(int rowCount)
    {
        var builder = new StringBuilder();
        builder.AppendLine("股名,股票代號,日期,成交股數,淨收付,成交單價,成交價金,手續費,交易稅,稅款,委託書號,幣別,備註");

        for (var i = 0; i < rowCount; i++)
        {
            var tradeDate = new DateTime(2026, 1, (i % 28) + 1, 0, 0, 0, DateTimeKind.Utc);
            var quantity = 1000 + (i % 11) * 100;
            var unitPrice = 620 + (i % 7);
            var grossAmount = quantity * unitPrice;
            var fees = 1000 + (i % 13) * 10;
            var taxes = 0;
            var netSettlement = -(grossAmount + fees + taxes);

            builder.Append('"').Append("台積電").Append("\",\"")
                .Append("2330").Append("\",\"")
                .Append(tradeDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture)).Append("\",\"")
                .Append(quantity.ToString("N0", CultureInfo.InvariantCulture)).Append("\",\"")
                .Append(netSettlement.ToString("N0", CultureInfo.InvariantCulture)).Append("\",\"")
                .Append(unitPrice.ToString(CultureInfo.InvariantCulture)).Append("\",\"")
                .Append(grossAmount.ToString("N0", CultureInfo.InvariantCulture)).Append("\",\"")
                .Append(fees.ToString("N0", CultureInfo.InvariantCulture)).Append("\",\"")
                .Append("0\",\"")
                .Append(taxes.ToString("N0", CultureInfo.InvariantCulture)).Append("\",\"")
                .Append("A").Append((i + 1).ToString("D4", CultureInfo.InvariantCulture)).Append("\",\"")
                .Append("台幣").Append("\",\"")
                .Append("\"")
                .AppendLine();
        }

        return builder.ToString();
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PortfolioId { get; } = Guid.NewGuid();

        public Mock<IStockImportSessionStore> SessionStoreMock { get; } = new();

        public PreviewStockImportUseCase UseCase { get; }

        public Fixture()
        {
            var portfolioRepositoryMock = new Mock<IPortfolioRepository>();
            var currentUserServiceMock = new Mock<ICurrentUserService>();
            var symbolResolver = new PassthroughSymbolResolver();

            var portfolio = new Portfolio(
                userId: UserId,
                boundCurrencyLedgerId: Guid.NewGuid(),
                baseCurrency: "TWD",
                homeCurrency: "TWD",
                displayName: "Performance Test Portfolio");

            currentUserServiceMock.SetupGet(service => service.UserId).Returns(UserId);

            portfolioRepositoryMock
                .Setup(repository => repository.GetByIdAsync(PortfolioId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(portfolio);

            SessionStoreMock
                .Setup(store => store.SaveAsync(It.IsAny<StockImportSessionSnapshotDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            UseCase = new PreviewStockImportUseCase(
                portfolioRepositoryMock.Object,
                currentUserServiceMock.Object,
                new StockImportParser(),
                symbolResolver,
                SessionStoreMock.Object,
                Mock.Of<IStockTransactionRepository>(),
                new PortfolioCalculator(),
                Mock.Of<Microsoft.Extensions.Logging.ILogger<PreviewStockImportUseCase>>());
        }
    }

    private sealed class PassthroughSymbolResolver : IStockImportSymbolResolver
    {
        public Task<StockImportSymbolResolutionResult> ResolveAsync(
            IReadOnlyList<StockImportParsedRow> rows,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new StockImportSymbolResolutionResult(rows, []));
        }
    }
}
