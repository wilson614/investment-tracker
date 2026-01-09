using System.Globalization;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching historical ETF prices from Stooq
/// Stooq provides free historical data for UK-listed ETFs
/// </summary>
public class StooqHistoricalPriceService : IStooqHistoricalPriceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<StooqHistoricalPriceService> _logger;

    private const string BaseUrl = "https://stooq.com/q/d/l/";

    // ETF symbol mappings for Stooq (UK-listed, USD denominated)
    private static readonly Dictionary<string, string> StooqSymbols = new()
    {
        ["All Country"] = "vwra.uk",       // Vanguard FTSE All-World UCITS ETF (Acc)
        ["US Large"] = "vuaa.uk",          // Vanguard S&P 500 UCITS ETF (Acc)
        ["Emerging Markets"] = "vfem.uk",  // Vanguard FTSE Emerging Markets UCITS ETF (Acc)
    };

    public StooqHistoricalPriceService(HttpClient httpClient, ILogger<StooqHistoricalPriceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public static IReadOnlyCollection<string> SupportedMarkets => StooqSymbols.Keys;

    /// <summary>
    /// Get the closing price for the last trading day of a given month
    /// </summary>
    public async Task<decimal?> GetMonthEndPriceAsync(
        string marketKey,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (!StooqSymbols.TryGetValue(marketKey, out var symbol))
        {
            _logger.LogDebug("No Stooq symbol mapping for {Market}", marketKey);
            return null;
        }

        try
        {
            // Get the last day of the month
            var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            // Start from a week before to ensure we get data even if last day is weekend
            var startDay = lastDay.AddDays(-7);

            var url = $"{BaseUrl}?s={symbol}&d1={startDay:yyyyMMdd}&d2={lastDay:yyyyMMdd}&i=d";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Stooq returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseLastClose(content, marketKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching historical price from Stooq for {Market}", marketKey);
            return null;
        }
    }

    /// <summary>
    /// Parse Stooq CSV response and get the last closing price
    /// Format: Date,Open,High,Low,Close,Volume
    /// </summary>
    private decimal? ParseLastClose(string csvContent, string marketKey)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            _logger.LogWarning("No data returned from Stooq for {Market}", marketKey);
            return null;
        }

        // Get the last data line (most recent date)
        var lastLine = lines[^1].Trim();
        if (string.IsNullOrEmpty(lastLine) || lastLine.StartsWith("Date"))
        {
            lastLine = lines.Length > 2 ? lines[^2].Trim() : null;
        }

        if (string.IsNullOrEmpty(lastLine))
        {
            return null;
        }

        var parts = lastLine.Split(',');
        if (parts.Length < 5)
        {
            _logger.LogWarning("Invalid Stooq data format for {Market}: {Line}", marketKey, lastLine);
            return null;
        }

        // Column 4 is Close (0-indexed)
        if (decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var closePrice))
        {
            _logger.LogDebug("Got historical price {Price} for {Market} from Stooq", closePrice, marketKey);
            return closePrice;
        }

        return null;
    }
}

public interface IStooqHistoricalPriceService
{
    Task<decimal?> GetMonthEndPriceAsync(string marketKey, int year, int month, CancellationToken cancellationToken = default);
}
