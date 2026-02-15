using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

public interface IStockImportSymbolResolver
{
    Task<StockImportSymbolResolutionResult> ResolveAsync(
        IReadOnlyList<StockImportParsedRow> rows,
        CancellationToken cancellationToken = default);
}

public sealed class StockImportSymbolResolver(
    ITwSecurityMappingRepository mappingRepository,
    ITwseSymbolMappingSyncService twseSymbolMappingSyncService) : IStockImportSymbolResolver
{
    private const string ActionInputTicker = "input_ticker";

    private const string ErrorCodeSymbolUnresolved = "SYMBOL_UNRESOLVED";
    private const string ErrorCodeSymbolSyncUnavailable = "SYMBOL_SYNC_UPSTREAM_UNAVAILABLE";

    private const string FieldRawSecurityName = "rawSecurityName";

    public async Task<StockImportSymbolResolutionResult> ResolveAsync(
        IReadOnlyList<StockImportParsedRow> rows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rows);

        if (rows.Count == 0)
        {
            return new StockImportSymbolResolutionResult([], []);
        }

        var rowsNeedingResolution = rows
            .Where(CanResolveBySecurityName)
            .OrderBy(row => row.RowNumber)
            .ToList();

        if (rowsNeedingResolution.Count == 0)
        {
            return new StockImportSymbolResolutionResult(rows, []);
        }

        var diagnostics = new List<StockImportDiagnosticDto>();
        Dictionary<int, StockImportParsedRow> resolvedByRowNumber = [];

        var securityNames = rowsNeedingResolution
            .Select(row => NormalizeSecurityName(row.RawSecurityName!))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var localMappings = await mappingRepository.GetBySecurityNamesAsync(securityNames, cancellationToken);
        var localCandidatesByName = BuildCandidatesBySecurityName(localMappings);

        HashSet<string> namesRequireSync = [];

        foreach (var row in rowsNeedingResolution)
        {
            var securityName = NormalizeSecurityName(row.RawSecurityName!);
            var candidates = GetCandidates(localCandidatesByName, securityName);

            if (candidates.Count == 1)
            {
                resolvedByRowNumber[row.RowNumber] = ResolveTicker(row, candidates[0].Ticker);
                continue;
            }

            if (candidates.Count > 1)
            {
                resolvedByRowNumber[row.RowNumber] = RequireTickerInput(row);
                diagnostics.Add(CreateUnresolvedDiagnostic(
                    row.RowNumber,
                    row.RawSecurityName,
                    message: "證券名稱對應到多個代號，無法唯一解析",
                    correctionGuidance: "請手動輸入 ticker 或排除此列。"));
                continue;
            }

            namesRequireSync.Add(securityName);
        }

        Dictionary<string, TwseSymbolMappingSyncError> syncErrorsByName = [];

        if (namesRequireSync.Count > 0)
        {
            var syncResult = await twseSymbolMappingSyncService.SyncOnDemandAsync(namesRequireSync, cancellationToken);
            syncErrorsByName = syncResult.Errors
                .Select(error => new KeyValuePair<string, TwseSymbolMappingSyncError>(
                    NormalizeSecurityName(error.SecurityName),
                    error))
                .GroupBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

            var postSyncMappings = await mappingRepository.GetBySecurityNamesAsync(namesRequireSync, cancellationToken);
            var postSyncCandidatesByName = BuildCandidatesBySecurityName(postSyncMappings);

            foreach (var row in rowsNeedingResolution)
            {
                if (resolvedByRowNumber.ContainsKey(row.RowNumber))
                {
                    continue;
                }

                var securityName = NormalizeSecurityName(row.RawSecurityName!);
                if (!namesRequireSync.Contains(securityName))
                {
                    continue;
                }

                var candidates = GetCandidates(postSyncCandidatesByName, securityName);
                if (candidates.Count == 1)
                {
                    resolvedByRowNumber[row.RowNumber] = ResolveTicker(row, candidates[0].Ticker);
                    continue;
                }

                resolvedByRowNumber[row.RowNumber] = RequireTickerInput(row);

                if (syncErrorsByName.TryGetValue(securityName, out var syncError) &&
                    string.Equals(syncError.ErrorCode, "UPSTREAM_UNAVAILABLE", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(CreateDiagnostic(
                        rowNumber: row.RowNumber,
                        fieldName: FieldRawSecurityName,
                        invalidValue: row.RawSecurityName,
                        errorCode: ErrorCodeSymbolSyncUnavailable,
                        message: "TWSE 對照同步暫時不可用，無法自動解析代號",
                        correctionGuidance: "請手動輸入 ticker，或稍後重新預覽。"));
                    continue;
                }

                diagnostics.Add(CreateUnresolvedDiagnostic(
                    row.RowNumber,
                    row.RawSecurityName,
                    message: "無法解析對應的證券代號",
                    correctionGuidance: "請手動輸入 ticker 或排除此列。"));
            }
        }

        var mergedRows = rows
            .Select(row => resolvedByRowNumber.TryGetValue(row.RowNumber, out var resolved) ? resolved : row)
            .ToList();

        var sortedDiagnostics = diagnostics
            .OrderBy(d => d.RowNumber)
            .ThenBy(d => d.FieldName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StockImportSymbolResolutionResult(mergedRows, sortedDiagnostics);
    }

    private static bool CanResolveBySecurityName(StockImportParsedRow row)
        => !row.IsInvalid &&
           string.IsNullOrWhiteSpace(row.Ticker) &&
           !string.IsNullOrWhiteSpace(row.RawSecurityName);

    private static Dictionary<string, IReadOnlyList<TwSecurityMapping>> BuildCandidatesBySecurityName(
        IEnumerable<TwSecurityMapping> mappings)
    {
        return mappings
            .GroupBy(mapping => NormalizeSecurityName(mapping.SecurityName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<TwSecurityMapping>)group.ToList(),
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<TwSecurityMapping> GetCandidates(
        IReadOnlyDictionary<string, IReadOnlyList<TwSecurityMapping>> bySecurityName,
        string securityName)
    {
        return bySecurityName.TryGetValue(securityName, out var candidates)
            ? candidates
            : [];
    }

    private static StockImportParsedRow ResolveTicker(StockImportParsedRow row, string ticker)
    {
        return row with
        {
            Ticker = ticker.Trim().ToUpperInvariant(),
            ActionsRequired = RemoveAction(row.ActionsRequired, ActionInputTicker)
        };
    }

    private static StockImportParsedRow RequireTickerInput(StockImportParsedRow row)
    {
        return row with
        {
            ActionsRequired = AddAction(row.ActionsRequired, ActionInputTicker)
        };
    }

    private static IReadOnlyList<string> AddAction(IReadOnlyList<string> actions, string action)
    {
        if (actions.Contains(action, StringComparer.Ordinal))
        {
            return actions;
        }

        var updated = actions.ToList();
        updated.Add(action);
        return updated;
    }

    private static IReadOnlyList<string> RemoveAction(IReadOnlyList<string> actions, string action)
    {
        if (actions.Count == 0)
        {
            return actions;
        }

        return actions
            .Where(existing => !string.Equals(existing, action, StringComparison.Ordinal))
            .ToList();
    }

    private static StockImportDiagnosticDto CreateUnresolvedDiagnostic(
        int rowNumber,
        string? invalidValue,
        string message,
        string correctionGuidance)
        => CreateDiagnostic(
            rowNumber,
            FieldRawSecurityName,
            invalidValue,
            ErrorCodeSymbolUnresolved,
            message,
            correctionGuidance);

    private static StockImportDiagnosticDto CreateDiagnostic(
        int rowNumber,
        string fieldName,
        string? invalidValue,
        string errorCode,
        string message,
        string correctionGuidance)
        => new()
        {
            RowNumber = rowNumber,
            FieldName = fieldName,
            InvalidValue = string.IsNullOrWhiteSpace(invalidValue) ? null : invalidValue,
            ErrorCode = errorCode,
            Message = message,
            CorrectionGuidance = correctionGuidance
        };

    private static string NormalizeSecurityName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var replaced = value.Trim()
            .Replace('\u3000', ' ')
            .Replace('\u00A0', ' ');

        return string.Join(' ', replaced.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}

public sealed record StockImportSymbolResolutionResult(
    IReadOnlyList<StockImportParsedRow> Rows,
    IReadOnlyList<StockImportDiagnosticDto> Diagnostics);
