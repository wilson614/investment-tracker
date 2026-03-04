using FluentAssertions;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Infrastructure.Tests.Services;

public class StockImportSessionStoreTests
{
    [Fact]
    public async Task TryStartExecutionAsync_ConcurrentCallsForSameSession_ShouldAllowOnlyOneStarter()
    {
        // Arrange
        var databaseName = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var portfolioId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        await SeedSessionAsync(databaseName, userId, portfolioId, sessionId);

        var contenderCount = 20;
        var tasks = Enumerable.Range(0, contenderCount)
            .Select(_ => Task.Run(async () =>
            {
                await using var db = CreateInMemoryDbContext(databaseName, userId);
                var store = new StockImportSessionStore(db);
                return await store.TryStartExecutionAsync(sessionId, userId, portfolioId, CancellationToken.None);
            }))
            .ToList();

        // Act
        var results = await Task.WhenAll(tasks);

        // Assert
        results.Count(started => started).Should().Be(1);

        await using var verifyDb = CreateInMemoryDbContext(databaseName, userId);
        var entity = await verifyDb.StockImportSessions
            .IgnoreQueryFilters()
            .SingleAsync(x => x.SessionId == sessionId);

        entity.ExecutionStatus.Should().Be("processing");
        entity.StartedAtUtc.Should().NotBeNull();
    }

    private static async Task SeedSessionAsync(string databaseName, Guid userId, Guid portfolioId, Guid sessionId)
    {
        await using var db = CreateInMemoryDbContext(databaseName, userId);

        var session = StockImportSessionSnapshotFactory.Create(
            sessionId: sessionId,
            userId: userId,
            portfolioId: portfolioId);

        var store = new StockImportSessionStore(db);
        await store.SaveAsync(session, CancellationToken.None);
    }

    private static AppDbContext CreateInMemoryDbContext(string databaseName, Guid userId)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options;

        return new AppDbContext(options, new FixedCurrentUserService(userId));
    }

    private sealed class FixedCurrentUserService(Guid userId) : Application.Interfaces.ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => null;
        public bool IsAuthenticated => true;
    }

    private static class StockImportSessionSnapshotFactory
    {
        public static StockImportSessionSnapshotDto Create(Guid sessionId, Guid userId, Guid portfolioId)
            => new()
            {
                SessionId = sessionId,
                UserId = userId,
                PortfolioId = portfolioId,
                DetectedFormat = "broker_statement",
                SelectedFormat = "broker_statement",
                Baseline = new StockImportSessionBaselineSnapshotDto
                {
                    AsOfDate = new DateTime(2026, 1, 31),
                    BaselineDate = new DateTime(2026, 1, 31),
                    CurrentHoldings = [],
                    OpeningPositions = [],
                    OpeningCashBalance = 0m,
                    OpeningLedgerBalance = 0m
                },
                Rows =
                [
                    new StockImportSessionRowSnapshotDto
                    {
                        RowNumber = 1,
                        Ticker = "2330",
                        TradeDate = new DateTime(2026, 1, 15),
                        Quantity = 1m,
                        UnitPrice = 600m,
                        Fees = 0m,
                        Taxes = 0m,
                        NetSettlement = -600m,
                        Currency = "TWD",
                        TradeSide = "buy",
                        ConfirmedTradeSide = "buy",
                        Status = "valid",
                        ActionsRequired = [],
                        IsInvalid = false
                    }
                ]
            };
    }
}
