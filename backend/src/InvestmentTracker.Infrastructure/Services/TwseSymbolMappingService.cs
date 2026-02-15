using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 從 TWSE ISIN source 進行按需同步，建立/更新台股證券名稱與代號映射。
/// </summary>
public class TwseSymbolMappingService(
    HttpClient httpClient,
    ITwSecurityMappingRepository mappingRepository,
    ILogger<TwseSymbolMappingService> logger)
{
    private const string TwseIsinUrl = "https://isin.twse.com.tw/isin/C_public.jsp?strMode=2";
    private const string DefaultCurrency = "TWD";

    private static readonly Regex TableRowRegex = new(
        "<tr[^>]*>(?<row>.*?)</tr>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex TableCellRegex = new(
        "<td[^>]*>(?<cell>.*?)</td>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex HtmlTagRegex = new(
        "<[^>]+>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex TickerAndNameRegex = new(
        @"^(?<ticker>[0-9A-Z]{2,10})\s+(?<name>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static TwseSymbolMappingService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// 對指定證券名稱執行按需同步。
    /// </summary>
    public async Task<TwseSymbolSyncResult> SyncOnDemandAsync(
        IEnumerable<string> securityNames,
        CancellationToken cancellationToken = default)
    {
        var requestedNames = securityNames
            .Select(NormalizeSecurityName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (requestedNames.Count == 0)
        {
            return new TwseSymbolSyncResult(0, 0, 0, [], []);
        }

        var records = await FetchTwseRecordsAsync(cancellationToken);
        if (records is null)
        {
            logger.LogWarning("TWSE ISIN synchronization failed because source is unavailable");

            var upstreamErrors = requestedNames
                .Select(name => new TwseSymbolSyncError(
                    name,
                    "UPSTREAM_UNAVAILABLE",
                    "TWSE ISIN source is unavailable"))
                .ToList();

            return new TwseSymbolSyncResult(
                requestedNames.Count,
                0,
                upstreamErrors.Count,
                [],
                upstreamErrors);
        }

        var bySecurityName = records
            .GroupBy(record => record.SecurityName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var syncedAt = DateTime.UtcNow;
        List<TwseSymbolSyncMapping> mappings = [];
        List<TwseSymbolSyncError> errors = [];

        foreach (var securityName in requestedNames)
        {
            if (!bySecurityName.TryGetValue(securityName, out var candidates) || candidates.Count == 0)
            {
                errors.Add(new TwseSymbolSyncError(
                    securityName,
                    "NOT_FOUND",
                    "No mapping found after synchronization"));
                continue;
            }

            if (candidates.Count > 1)
            {
                foreach (var candidate in candidates)
                {
                    await UpsertAsync(candidate, syncedAt, cancellationToken);
                }

                errors.Add(new TwseSymbolSyncError(
                    securityName,
                    "AMBIGUOUS_MATCH",
                    $"Multiple mappings found after synchronization ({candidates.Count} candidates)"));
                continue;
            }

            var matched = candidates[0];
            await UpsertAsync(matched, syncedAt, cancellationToken);

            mappings.Add(new TwseSymbolSyncMapping(
                matched.SecurityName,
                matched.Ticker,
                matched.Isin,
                matched.Market));
        }

        return new TwseSymbolSyncResult(
            requestedNames.Count,
            mappings.Count,
            errors.Count,
            mappings,
            errors);
    }

    private async Task UpsertAsync(TwseIsinRecord record, DateTime syncedAt, CancellationToken cancellationToken)
    {
        var mapping = new TwSecurityMapping(
            record.Ticker,
            record.SecurityName,
            syncedAt,
            TwSecurityMapping.SourceTwseIsin,
            record.Isin,
            record.Market,
            DefaultCurrency);

        await mappingRepository.UpsertAsync(mapping, cancellationToken);
    }

    private async Task<IReadOnlyList<TwseIsinRecord>?> FetchTwseRecordsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, TwseIsinUrl);
            request.Headers.Add("Accept", "text/html");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TWSE ISIN source returned status code {StatusCode}", response.StatusCode);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var html = DecodeTwseHtml(bytes);
            var records = ParseTwseRecords(html);

            logger.LogInformation("Fetched {Count} records from TWSE ISIN source", records.Count);
            return records;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to synchronize mappings from TWSE ISIN source");
            return null;
        }
    }

    private static string DecodeTwseHtml(byte[] bytes)
    {
        var utf8 = Encoding.UTF8.GetString(bytes);

        // TWSE 目前實際內容常為 Big5，若 UTF-8 解碼後無關鍵中文則回退 Big5。
        if (utf8.Contains("有價證券", StringComparison.Ordinal) || utf8.Contains("證券", StringComparison.Ordinal))
        {
            return utf8;
        }

        var big5 = Encoding.GetEncoding(950);
        return big5.GetString(bytes);
    }

    private static IReadOnlyList<TwseIsinRecord> ParseTwseRecords(string html)
    {
        Dictionary<string, TwseIsinRecord> records = new(StringComparer.Ordinal);

        foreach (Match rowMatch in TableRowRegex.Matches(html))
        {
            var rowHtml = rowMatch.Groups["row"].Value;
            var cells = TableCellRegex.Matches(rowHtml)
                .Select(match => NormalizeCellText(match.Groups["cell"].Value))
                .ToList();

            if (cells.Count < 4)
            {
                continue;
            }

            var codeAndName = cells[0];
            var isin = NormalizeOptionalUpper(cells[1]);
            var listingType = cells[3];

            if (!TryParseTickerAndName(codeAndName, out var ticker, out var securityName))
            {
                continue;
            }

            if (isin is null)
            {
                continue;
            }

            var market = MapMarket(listingType);
            records[ticker] = new TwseIsinRecord(ticker, securityName, isin, market);
        }

        return records.Values.ToList();
    }

    private static bool TryParseTickerAndName(string value, out string ticker, out string securityName)
    {
        ticker = string.Empty;
        securityName = string.Empty;

        var normalized = NormalizeCellText(value);
        var match = TickerAndNameRegex.Match(normalized);
        if (!match.Success)
        {
            return false;
        }

        ticker = match.Groups["ticker"].Value.Trim().ToUpperInvariant();
        securityName = NormalizeSecurityName(match.Groups["name"].Value);

        return !string.IsNullOrWhiteSpace(securityName);
    }

    private static string NormalizeCellText(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return string.Empty;
        }

        var withoutTags = HtmlTagRegex.Replace(rawValue, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags)
            .Replace('\u3000', ' ')
            .Replace('\u00A0', ' ')
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);

        return CollapseSpaces(decoded.Trim());
    }

    private static string NormalizeSecurityName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        var replaced = rawName.Trim()
            .Replace('\u3000', ' ')
            .Replace('\u00A0', ' ');

        return CollapseSpaces(replaced);
    }

    private static string? NormalizeOptionalUpper(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim().ToUpperInvariant();
    }

    private static string? MapMarket(string listingType)
    {
        if (listingType.Contains("上市", StringComparison.Ordinal))
        {
            return "TWSE";
        }

        if (listingType.Contains("上櫃", StringComparison.Ordinal) || listingType.Contains("興櫃", StringComparison.Ordinal))
        {
            return "TPEX";
        }

        return null;
    }

    private static string CollapseSpaces(string value)
    {
        return string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed record TwseIsinRecord(string Ticker, string SecurityName, string Isin, string? Market);
}

public record TwseSymbolSyncResult(
    int Requested,
    int Resolved,
    int Unresolved,
    IReadOnlyList<TwseSymbolSyncMapping> Mappings,
    IReadOnlyList<TwseSymbolSyncError> Errors);

public record TwseSymbolSyncMapping(
    string SecurityName,
    string Ticker,
    string Isin,
    string? Market);

public record TwseSymbolSyncError(
    string SecurityName,
    string ErrorCode,
    string Message);
