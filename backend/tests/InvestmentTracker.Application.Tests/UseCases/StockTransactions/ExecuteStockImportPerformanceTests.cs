using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Moq;

namespace InvestmentTracker.Application.Tests.UseCases.StockTransactions;

/// <summary>
/// Performance observation benchmarks for stock import execute path (Speckit T152).
/// Captures cold/warm behavior for 500 and 2000 rows without hard timing thresholds.
/// </summary>
public class ExecuteStockImportPerformanceTests
{
    [Theory]
    [InlineData(500)]
    [InlineData(2000)]
    public async Task ExecuteAsync_ExecutePathBenchmark_ShouldRecordColdAndWarmPathObservations(int rowCount)
    {
        // Act
        var coldRun = await MeasureExecuteRunAsync(rowCount);
        AssertCommittedResult(coldRun.Response, rowCount);

        var warmMeasurements = new List<TimeSpan>(capacity: 3);
        for (var i = 0; i < 3; i++)
        {
            var warmRun = await MeasureExecuteRunAsync(rowCount);
            AssertCommittedResult(warmRun.Response, rowCount);
            warmMeasurements.Add(warmRun.Elapsed);
        }

        var warmMedianElapsed = GetMedian(warmMeasurements);
        var warmMinElapsed = warmMeasurements.Min();
        var warmMaxElapsed = warmMeasurements.Max();
        var warmSampleSeries = string.Join(", ", warmMeasurements.Select(value => value.TotalMilliseconds.ToString("F2", CultureInfo.InvariantCulture)));

        Console.WriteLine(
            "[PerfObservation][ExecuteStockImport] rows={0}; coldMs={1:F2}; warmRuns={2}; warmMinMs={3:F2}; warmMedianMs={4:F2}; warmMaxMs={5:F2}; warmSamplesMs=[{6}]",
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
    }

    private static async Task<(StockImportExecuteResponseDto Response, TimeSpan Elapsed)> MeasureExecuteRunAsync(int rowCount)
    {
        var fixture = new Fixture();
        var request = fixture.BuildExecuteRequest(rowCount);

        var stopwatch = Stopwatch.StartNew();
        var response = await fixture.UseCase.ExecuteAsync(request, CancellationToken.None);
        stopwatch.Stop();

        fixture.StockImportSessionStoreMock.Verify(
            store => store.SaveExecutionResultAsync(
                It.IsAny<StockImportExecuteSessionStateDto>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        return (response, stopwatch.Elapsed);
    }

    private static void AssertCommittedResult(StockImportExecuteResponseDto response, int expectedRows)
    {
        response.Status.Should().Be("committed");
        response.Summary.TotalRows.Should().Be(expectedRows);
        response.Summary.InsertedRows.Should().Be(expectedRows);
        response.Summary.FailedRows.Should().Be(0);
        response.Results.Should().HaveCount(expectedRows);
        response.Results.All(result => result.Success).Should().BeTrue();
        response.Errors.Should().BeEmpty();
    }

    private static TimeSpan GetMedian(IReadOnlyCollection<TimeSpan> values)
    {
        values.Should().NotBeEmpty();

        var ordered = values
            .OrderBy(value => value)
            .ToArray();

        return ordered[ordered.Length / 2];
    }

    private sealed class Fixture
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid PortfolioId { get; } = Guid.NewGuid();
        public Guid BoundLedgerId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();

        public Mock<IStockImportSessionStore> StockImportSessionStoreMock { get; } = new();

        public ExecuteStockImportUseCase UseCase { get; }

        private readonly List<StockImportSessionRowSnapshotDto> _sessionRows;
        private readonly List<CurrencyTransaction> _ledgerTransactions;
        private readonly List<StockTransaction> _stockTransactions = [];

        public Fixture()
        {
            var portfolio = new Portfolio(UserId, BoundLedgerId, baseCurrency: "TWD", homeCurrency: "TWD", displayName: "Execute Perf Portfolio");
            typeof(Portfolio).GetProperty(nameof(Portfolio.Id))!.SetValue(portfolio, PortfolioId);

            var boundLedger = new Domain.Entities.CurrencyLedger(
                UserId,
                currencyCode: "TWD",
                name: "TWD Ledger",
                homeCurrency: "TWD");
            typeof(Domain.Entities.CurrencyLedger).GetProperty(nameof(Domain.Entities.CurrencyLedger.Id))!.SetValue(boundLedger, BoundLedgerId);

            _sessionRows = BuildSessionRows(2000).ToList();

            _ledgerTransactions =
            [
                CreateCurrencyTransaction(
                    currencyLedgerId: BoundLedgerId,
                    transactionDate: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    transactionType: CurrencyTransactionType.Deposit,
                    foreignAmount: 2_000_000_000m,
                    homeAmount: 2_000_000_000m,
                    exchangeRate: 1.0m,
                    notes: "perf-seed")
            ];

            var portfolioRepositoryMock = new Mock<IPortfolioRepository>();
            var currentUserServiceMock = new Mock<ICurrentUserService>();
            var stockTransactionRepositoryMock = new Mock<IStockTransactionRepository>();
            var currencyLedgerRepositoryMock = new Mock<ICurrencyLedgerRepository>();
            var currencyTransactionRepositoryMock = new Mock<ICurrencyTransactionRepository>();
            var fxServiceMock = new Mock<ITransactionDateExchangeRateService>();
            var monthlySnapshotServiceMock = new Mock<IMonthlySnapshotService>();
            var txSnapshotServiceMock = new Mock<ITransactionPortfolioSnapshotService>();
            var transactionManagerMock = new Mock<IAppDbTransactionManager>();
            var appDbTransactionMock = new Mock<IAppDbTransaction>();
            var yahooHistoricalPriceServiceMock = new Mock<IYahooHistoricalPriceService>();

            currentUserServiceMock.SetupGet(service => service.UserId).Returns(UserId);

            portfolioRepositoryMock
                .Setup(repository => repository.GetByIdAsync(PortfolioId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(portfolio);

            currencyLedgerRepositoryMock
                .Setup(repository => repository.GetByIdAsync(BoundLedgerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(boundLedger);

            currencyTransactionRepositoryMock
                .Setup(repository => repository.GetByLedgerIdOrderedAsync(BoundLedgerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _ledgerTransactions.OrderBy(transaction => transaction.TransactionDate).ToList());

            currencyTransactionRepositoryMock
                .Setup(repository => repository.AddAsync(It.IsAny<CurrencyTransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CurrencyTransaction transaction, CancellationToken _) =>
                {
                    _ledgerTransactions.Add(transaction);
                    return transaction;
                });

            stockTransactionRepositoryMock
                .Setup(repository => repository.GetByPortfolioIdAsync(PortfolioId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => _stockTransactions
                    .OrderBy(transaction => transaction.TransactionDate)
                    .ThenBy(transaction => transaction.CreatedAt)
                    .ToList());

            stockTransactionRepositoryMock
                .Setup(repository => repository.AddAsync(It.IsAny<StockTransaction>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockTransaction transaction, CancellationToken _) =>
                {
                    _stockTransactions.Add(transaction);
                    return transaction;
                });

            fxServiceMock
                .Setup(service => service.GetOrFetchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TransactionDateExchangeRateResult
                {
                    Rate = 1.0m,
                    CurrencyPair = "TWDTWD",
                    RequestedDate = DateTime.UtcNow.Date,
                    ActualDate = DateTime.UtcNow.Date,
                    Source = "test",
                    FromCache = true
                });

            yahooHistoricalPriceServiceMock
                .Setup(service => service.GetHistoricalPriceAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((YahooHistoricalPriceResult?)null);

            transactionManagerMock
                .Setup(manager => manager.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(appDbTransactionMock.Object);

            var previewSessionSnapshot = new StockImportSessionSnapshotDto
            {
                SessionId = SessionId,
                UserId = UserId,
                PortfolioId = PortfolioId,
                SelectedFormat = "broker_statement",
                DetectedFormat = "broker_statement",
                Baseline = new StockImportSessionBaselineSnapshotDto(),
                Rows = _sessionRows
            };

            StockImportSessionStoreMock
                .Setup(store => store.GetExecutionStateAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((StockImportExecuteSessionStateDto?)null);

            StockImportSessionStoreMock
                .Setup(store => store.GetAsync(SessionId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(previewSessionSnapshot);

            StockImportSessionStoreMock
                .Setup(store => store.TryStartExecutionAsync(
                    SessionId,
                    UserId,
                    PortfolioId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            StockImportSessionStoreMock
                .Setup(store => store.TryConsumeForOwnerAsync(
                    SessionId,
                    UserId,
                    PortfolioId,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(previewSessionSnapshot);

            StockImportSessionStoreMock
                .Setup(store => store.SaveExecutionResultAsync(It.IsAny<StockImportExecuteSessionStateDto>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            UseCase = new ExecuteStockImportUseCase(
                portfolioRepositoryMock.Object,
                currentUserServiceMock.Object,
                StockImportSessionStoreMock.Object,
                stockTransactionRepositoryMock.Object,
                currencyLedgerRepositoryMock.Object,
                currencyTransactionRepositoryMock.Object,
                fxServiceMock.Object,
                monthlySnapshotServiceMock.Object,
                txSnapshotServiceMock.Object,
                new CurrencyLedgerService(),
                new PortfolioCalculator(),
                transactionManagerMock.Object,
                yahooHistoricalPriceServiceMock.Object,
                Mock.Of<Microsoft.Extensions.Logging.ILogger<ExecuteStockImportUseCase>>());
        }

        public ExecuteStockImportRequest BuildExecuteRequest(int rowCount)
        {
            var rows = _sessionRows
                .Take(rowCount)
                .Select(row => new ExecuteStockImportRowRequest
                {
                    RowNumber = row.RowNumber,
                    Ticker = row.Ticker,
                    ConfirmedTradeSide = row.ConfirmedTradeSide,
                    Exclude = false,
                    BalanceAction = null,
                    TopUpTransactionType = null,
                    SellBeforeBuyAction = null
                })
                .ToList();

            return new ExecuteStockImportRequest
            {
                SessionId = SessionId,
                PortfolioId = PortfolioId,
                DefaultBalanceAction = new StockImportDefaultBalanceDecisionRequest
                {
                    Action = BalanceAction.TopUp,
                    TopUpTransactionType = CurrencyTransactionType.Deposit
                },
                Rows = rows
            };
        }

        private static IEnumerable<StockImportSessionRowSnapshotDto> BuildSessionRows(int rowCount)
        {
            for (var index = 1; index <= rowCount; index++)
            {
                yield return new StockImportSessionRowSnapshotDto
                {
                    RowNumber = index,
                    TradeDate = new DateTime(2026, 1, (index % 28) + 1, 0, 0, 0, DateTimeKind.Utc),
                    Ticker = "2330",
                    TradeSide = "buy",
                    ConfirmedTradeSide = "buy",
                    Quantity = 200m + (index % 5) * 10m,
                    UnitPrice = 625m + (index % 7),
                    Fees = 20m + (index % 3),
                    Taxes = 0m,
                    NetSettlement = -((200m + (index % 5) * 10m) * (625m + (index % 7)) + (20m + (index % 3))),
                    Currency = "TWD",
                    Status = "valid",
                    ActionsRequired = [],
                    IsInvalid = false
                };
            }
        }

        private static CurrencyTransaction CreateCurrencyTransaction(
            Guid currencyLedgerId,
            DateTime transactionDate,
            CurrencyTransactionType transactionType,
            decimal foreignAmount,
            decimal? homeAmount,
            decimal? exchangeRate,
            string? notes)
        {
            return new CurrencyTransaction(
                currencyLedgerId,
                transactionDate,
                transactionType,
                foreignAmount,
                homeAmount,
                exchangeRate,
                relatedStockTransactionId: null,
                notes);
        }
    }
}
