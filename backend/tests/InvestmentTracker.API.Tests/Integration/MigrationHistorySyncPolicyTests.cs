using FluentAssertions;
using InvestmentTracker.API;

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
}
