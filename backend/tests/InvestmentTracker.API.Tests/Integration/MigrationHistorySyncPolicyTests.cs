using FluentAssertions;
using InvestmentTracker.API;
using InvestmentTracker.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace InvestmentTracker.API.Tests.Integration;

public class MigrationHistorySyncPolicyTests
{
    [Theory]
    [InlineData("20260304103000_AddStockImportSessionPersistence")]
    [InlineData("20260304103000_AddStockImportSessionPersistence.1A2B3C4D")]
    public void ShouldMarkMigrationAsApplied_WhenStockImportSessionTableExists(string migrationId)
    {
        // Arrange
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "users",
            "portfolios",
            "stock_import_sessions"
        };

        // Act
        var shouldMark = MigrationHistorySyncPolicy.ShouldMarkMigrationAsApplied(migrationId, existingTables);

        // Assert
        shouldMark.Should().BeTrue();
    }

    [Theory]
    [InlineData("20260304103000_AddStockImportSessionPersistence")]
    [InlineData("20260304103000_AddStockImportSessionPersistence.1A2B3C4D")]
    public void ShouldMarkMigrationAsApplied_WhenStockImportSessionTableMissing_ShouldReturnFalse(string migrationId)
    {
        // Arrange
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "users",
            "portfolios"
        };

        // Act
        var shouldMark = MigrationHistorySyncPolicy.ShouldMarkMigrationAsApplied(migrationId, existingTables);

        // Assert
        shouldMark.Should().BeFalse();
    }

    [Theory]
    [InlineData("20260111153721_InitialCreate")]
    [InlineData("20260111153721_InitialCreate.ABCD1234")]
    public void ShouldMarkMigrationAsApplied_ForUnlistedMigration_ShouldDefaultToFalse(string migrationId)
    {
        // Arrange
        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Act
        var shouldMark = MigrationHistorySyncPolicy.ShouldMarkMigrationAsApplied(migrationId, existingTables);

        // Assert
        shouldMark.Should().BeFalse();
    }

    [Fact]
    public void GetAppliedMigrationMarkersToRemove_WhenTargetMigrationTableMissing_ShouldReturnMarker()
    {
        // Arrange
        var appliedMigrationIds = new List<string>
        {
            "20260111153721_InitialCreate",
            "20260304103000_AddStockImportSessionPersistence"
        };

        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "users",
            "portfolios"
        };

        // Act
        var markers = MigrationHistorySyncPolicy.GetAppliedMigrationMarkersToRemove(appliedMigrationIds, existingTables);

        // Assert
        markers.Should().BeEquivalentTo([
            "20260304103000_AddStockImportSessionPersistence"
        ]);
    }

    [Fact]
    public void GetAppliedMigrationMarkersToRemove_WhenSuffixMarkerTableMissing_ShouldReturnOriginalMarker()
    {
        // Arrange
        var appliedMigrationIds = new List<string>
        {
            "20260304103000_AddStockImportSessionPersistence.1A2B3C4D"
        };

        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "users",
            "portfolios"
        };

        // Act
        var markers = MigrationHistorySyncPolicy.GetAppliedMigrationMarkersToRemove(appliedMigrationIds, existingTables);

        // Assert
        markers.Should().BeEquivalentTo([
            "20260304103000_AddStockImportSessionPersistence.1A2B3C4D"
        ]);
    }

    [Fact]
    public void GetAppliedMigrationMarkersToRemove_WhenRequiredTableExists_ShouldReturnEmpty()
    {
        // Arrange
        var appliedMigrationIds = new List<string>
        {
            "20260304103000_AddStockImportSessionPersistence"
        };

        var existingTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "users",
            "portfolios",
            "stock_import_sessions"
        };

        // Act
        var markers = MigrationHistorySyncPolicy.GetAppliedMigrationMarkersToRemove(appliedMigrationIds, existingTables);

        // Assert
        markers.Should().BeEmpty();
    }

    [Fact]
    public void EfMigrations_ShouldRecognizeAllDeclaredMigrationTypes()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=investment_tracker_test;Username=postgres;Password=postgres")
            .Options;

        using var context = new AppDbContext(options);

        var migrationTypes = typeof(AppDbContext).Assembly
            .GetTypes()
            .Where(type =>
                typeof(Migration).IsAssignableFrom(type)
                && !type.IsAbstract
                && type.Namespace == "InvestmentTracker.Infrastructure.Persistence.Migrations")
            .ToList();

        var missingAttributeTypes = migrationTypes
            .Where(type => type.GetCustomAttributes(typeof(MigrationAttribute), inherit: false)
                .OfType<MigrationAttribute>()
                .SingleOrDefault() is null)
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        var declaredMigrations = migrationTypes
            .Select(type => type.GetCustomAttributes(typeof(MigrationAttribute), inherit: false)
                .OfType<MigrationAttribute>()
                .SingleOrDefault()?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToList();

        // Act
        var recognizedMigrations = context.Database.GetMigrations().ToList();

        // Assert
        missingAttributeTypes.Should().BeEmpty();
        declaredMigrations.Should().Contain("20260304103000_AddStockImportSessionPersistence");
        recognizedMigrations.Should().Contain(declaredMigrations);
    }

    [Fact]
    public void IsSupportedProvider_WhenProviderIsNpgsql_ShouldReturnTrue()
    {
        // Act
        var supported = MigrationHistorySyncPolicy.IsSupportedProvider("Npgsql.EntityFrameworkCore.PostgreSQL");

        // Assert
        supported.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Microsoft.EntityFrameworkCore.InMemory")]
    public void IsSupportedProvider_WhenProviderIsNotNpgsql_ShouldReturnFalse(string? providerName)
    {
        // Act
        var supported = MigrationHistorySyncPolicy.IsSupportedProvider(providerName);

        // Assert
        supported.Should().BeFalse();
    }
}
