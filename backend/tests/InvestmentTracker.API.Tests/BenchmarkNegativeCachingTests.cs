using FluentAssertions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace InvestmentTracker.API.Tests;

/// <summary>
/// Unit tests for benchmark negative caching behavior (T123).
/// Tests that NotAvailable markers are persisted and prevent repeated Stooq calls.
/// </summary>
public class BenchmarkNegativeCachingTests
{
    /// <summary>
    /// Creates an in-memory database context for testing.
    /// </summary>
    private static AppDbContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public async Task IndexPriceSnapshot_NotAvailableMarker_IsPersisted()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var marketKey = "Test Market";
        var yearMonth = "202312";

        var snapshot = new IndexPriceSnapshot
        {
            MarketKey = marketKey,
            YearMonth = yearMonth,
            Price = null,
            IsNotAvailable = true,
            RecordedAt = DateTime.UtcNow
        };

        // Act
        context.IndexPriceSnapshots.Add(snapshot);
        await context.SaveChangesAsync();

        // Assert
        var saved = await context.IndexPriceSnapshots
            .FirstOrDefaultAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth);

        saved.Should().NotBeNull();
        saved!.IsNotAvailable.Should().BeTrue();
        saved.Price.Should().BeNull();
    }

    [Fact]
    public async Task IndexPriceSnapshot_NotAvailableQuery_FiltersCorrectly()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var yearMonth = "202312";

        // Add a valid price
        context.IndexPriceSnapshots.Add(new IndexPriceSnapshot
        {
            MarketKey = "US Large",
            YearMonth = yearMonth,
            Price = 450.50m,
            IsNotAvailable = false,
            RecordedAt = DateTime.UtcNow
        });

        // Add a NotAvailable marker
        context.IndexPriceSnapshots.Add(new IndexPriceSnapshot
        {
            MarketKey = "Unavailable Market",
            YearMonth = yearMonth,
            Price = null,
            IsNotAvailable = true,
            RecordedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        // Act - Query valid prices (excluding NotAvailable)
        var validPrices = await context.IndexPriceSnapshots
            .Where(s => s.YearMonth == yearMonth && !s.IsNotAvailable && s.Price.HasValue)
            .ToListAsync();

        // Act - Query NotAvailable markers
        var notAvailableMarkers = await context.IndexPriceSnapshots
            .Where(s => s.YearMonth == yearMonth && s.IsNotAvailable)
            .Select(s => s.MarketKey)
            .ToListAsync();

        // Assert
        validPrices.Should().HaveCount(1);
        validPrices[0].MarketKey.Should().Be("US Large");

        notAvailableMarkers.Should().HaveCount(1);
        notAvailableMarkers[0].Should().Be("Unavailable Market");
    }

    [Fact]
    public async Task IndexPriceSnapshot_SkipFetchingForNotAvailable()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var yearMonth = "202312";
        var allBenchmarks = new[] { "US Large", "Europe", "Japan", "Unavailable Market" };

        // Pre-populate: US Large has price, Unavailable Market has NotAvailable marker
        context.IndexPriceSnapshots.Add(new IndexPriceSnapshot
        {
            MarketKey = "US Large",
            YearMonth = yearMonth,
            Price = 450.50m,
            IsNotAvailable = false,
            RecordedAt = DateTime.UtcNow
        });

        context.IndexPriceSnapshots.Add(new IndexPriceSnapshot
        {
            MarketKey = "Unavailable Market",
            YearMonth = yearMonth,
            Price = null,
            IsNotAvailable = true,
            RecordedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        // Act - Determine which markets need fetching
        var existingPrices = await context.IndexPriceSnapshots
            .Where(s => s.YearMonth == yearMonth && !s.IsNotAvailable && s.Price.HasValue)
            .Select(s => s.MarketKey)
            .ToListAsync();

        var notAvailableMarkets = await context.IndexPriceSnapshots
            .Where(s => s.YearMonth == yearMonth && s.IsNotAvailable)
            .Select(s => s.MarketKey)
            .ToListAsync();

        var marketsToFetch = allBenchmarks
            .Where(k => !existingPrices.Contains(k) && !notAvailableMarkets.Contains(k))
            .ToList();

        // Assert
        marketsToFetch.Should().HaveCount(2);
        marketsToFetch.Should().Contain("Europe");
        marketsToFetch.Should().Contain("Japan");
        marketsToFetch.Should().NotContain("US Large"); // Already has price
        marketsToFetch.Should().NotContain("Unavailable Market"); // Has NotAvailable marker
    }

    [Fact]
    public async Task IndexPriceSnapshot_NotAvailableMarker_PreventsDuplicateFetches()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var marketKey = "Dead Market";
        var yearMonth = "202312";

        // Simulate first fetch attempt that returns null - save NotAvailable marker
        context.IndexPriceSnapshots.Add(new IndexPriceSnapshot
        {
            MarketKey = marketKey,
            YearMonth = yearMonth,
            Price = null,
            IsNotAvailable = true,
            RecordedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // Act - Check if we should skip fetching
        var existingEntry = await context.IndexPriceSnapshots
            .AnyAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth);

        var notAvailableEntry = await context.IndexPriceSnapshots
            .AnyAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth && s.IsNotAvailable);

        // Assert
        existingEntry.Should().BeTrue("entry exists in cache");
        notAvailableEntry.Should().BeTrue("entry is marked as NotAvailable");
        
        // In real code, this would prevent the Stooq API call:
        // if (existingEntry) continue; // Skip API call
    }

    [Fact]
    public async Task IndexPriceSnapshot_ValidPrice_NotMarkedAsNotAvailable()
    {
        // Arrange
        using var context = CreateInMemoryContext();
        var marketKey = "Valid Market";
        var yearMonth = "202312";

        // Save a valid price (simulating successful Stooq fetch)
        context.IndexPriceSnapshots.Add(new IndexPriceSnapshot
        {
            MarketKey = marketKey,
            YearMonth = yearMonth,
            Price = 123.45m,
            IsNotAvailable = false,
            RecordedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        // Act
        var entry = await context.IndexPriceSnapshots
            .FirstOrDefaultAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth);

        // Assert
        entry.Should().NotBeNull();
        entry!.Price.Should().Be(123.45m);
        entry.IsNotAvailable.Should().BeFalse();
    }
}
