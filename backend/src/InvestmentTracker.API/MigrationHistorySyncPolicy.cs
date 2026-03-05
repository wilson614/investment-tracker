using System.Text.RegularExpressions;

namespace InvestmentTracker.API;

public static class MigrationHistorySyncPolicy
{
    private static readonly IReadOnlyList<MigrationRequiredTableRule> RequiredTableRules =
    [
        new MigrationRequiredTableRule(
            MigrationPrefix: "20260304103000_AddStockImportSessionPersistence",
            RequiredTableNames:
            [
                "stock_import_sessions"
            ])
    ];

    public static bool ShouldMarkMigrationAsApplied(string migrationId, IReadOnlySet<string> existingTableNames)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(migrationId);
        ArgumentNullException.ThrowIfNull(existingTableNames);

        var normalizedMigrationId = NormalizeMigrationId(migrationId);
        foreach (var rule in RequiredTableRules)
        {
            if (!normalizedMigrationId.StartsWith(rule.MigrationPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            return rule.RequiredTableNames.All(existingTableNames.Contains);
        }

        // Fail-closed by default: only explicitly configured migrations may be marked as applied.
        return false;
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

    internal sealed record MigrationRequiredTableRule(string MigrationPrefix, IReadOnlyList<string> RequiredTableNames);
}
