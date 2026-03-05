using System.Text.RegularExpressions;

namespace InvestmentTracker.API;

public static class MigrationHistorySyncPolicy
{
    private const string PostgresProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static readonly IReadOnlyList<MigrationRequiredTableRule> RequiredTableRules =
    [
        new MigrationRequiredTableRule(
            MigrationPrefix: "20260304103000_AddStockImportSessionPersistence",
            RequiredTableNames:
            [
                "stock_import_sessions"
            ])
    ];

    public static bool IsSupportedProvider(string? providerName)
    {
        return string.Equals(providerName, PostgresProviderName, StringComparison.Ordinal);
    }

    public static bool ShouldMarkMigrationAsApplied(string migrationId, IReadOnlySet<string> existingTableNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        ArgumentNullException.ThrowIfNull(existingTableNames);

        var matchingRule = FindMatchingRule(migrationId);
        if (matchingRule is null)
        {
            // Fail-closed by default: only explicitly configured migrations may be marked as applied.
            return false;
        }

        return matchingRule.RequiredTableNames.All(existingTableNames.Contains);
    }

    public static IReadOnlyList<string> GetAppliedMigrationMarkersToRemove(
        IReadOnlyCollection<string> appliedMigrationIds,
        IReadOnlySet<string> existingTableNames)
    {
        ArgumentNullException.ThrowIfNull(appliedMigrationIds);
        ArgumentNullException.ThrowIfNull(existingTableNames);

        if (appliedMigrationIds.Count == 0)
        {
            return [];
        }

        var markersToRemove = new List<string>();
        foreach (var appliedMigrationId in appliedMigrationIds)
        {
            if (string.IsNullOrWhiteSpace(appliedMigrationId))
            {
                continue;
            }

            var matchingRule = FindMatchingRule(appliedMigrationId);
            if (matchingRule is null)
            {
                continue;
            }

            if (matchingRule.RequiredTableNames.All(existingTableNames.Contains))
            {
                continue;
            }

            markersToRemove.Add(appliedMigrationId.Trim());
        }

        return markersToRemove;
    }

    internal static string NormalizeMigrationId(string migrationId)
    {
        var normalized = migrationId.Trim();
        var suffixSeparator = normalized.LastIndexOf('.');
        if (suffixSeparator < 0)
        {
            return normalized;
        }

        var suffix = normalized[(suffixSeparator + 1)..];
        if (Regex.IsMatch(suffix, "^[0-9A-Fa-f]{8}$"))
        {
            return normalized[..suffixSeparator];
        }

        return normalized;
    }

    private static MigrationRequiredTableRule? FindMatchingRule(string migrationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);

        var normalizedMigrationId = NormalizeMigrationId(migrationId);
        return RequiredTableRules.FirstOrDefault(rule =>
            normalizedMigrationId.StartsWith(rule.MigrationPrefix, StringComparison.Ordinal));
    }

    internal sealed record MigrationRequiredTableRule(string MigrationPrefix, IReadOnlyList<string> RequiredTableNames);
}
