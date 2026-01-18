using System.Globalization;
using System.Text.Json;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Infrastructure.Services;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching historical prices for individual Taiwan stocks from TWSE.
/// Uses TWSE STOCK_DAY API for daily trading data.
/// Rate limited via ITwseRateLimiter to avoid being blocked.
/// </summary>
public class TwseStockHistoricalPriceService : ITwseStockHistoricalPriceService
{
    private readonly HttpClient _httpClient;
    private readonly ITwseRateLimiter _rateLimiter;
    private readonly ILogger<TwseStockHistoricalPriceService> _logger;

    // TWSE API for individual stock daily data
    private const string StockDayUrl = "https://www.twse.com.tw/exchangeReport/STOCK_DAY";

    public TwseStockHistoricalPriceService(
        HttpClient httpClient,
        ITwseRateLimiter rateLimiter,
        ILogger<TwseStockHistoricalPriceService> logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<TwseStockPriceResult?> GetStockPriceAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _rateLimiter.WaitForSlotAsync(cancellationToken);

            // TWSE API requires date format: YYYYMM01 (first day of month)
            var dateParam = $"{date.Year}{date.Month:D2}01";
            var url = $"{StockDayUrl}?response=json&date={dateParam}&stockNo={stockNo}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TWSE STOCK_DAY returned {Status} for {StockNo}", response.StatusCode, stockNo);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseStockDayResponse(content, stockNo, date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching TWSE stock price for {StockNo} on {Date}", stockNo, date);
            return null;
        }
    }

    public async Task<TwseStockPriceResult?> GetYearEndPriceAsync(
        string stockNo,
        int year,
        CancellationToken cancellationToken = default)
    {
        // Try December first
        var result = await GetStockPriceAsync(stockNo, new DateOnly(year, 12, 31), cancellationToken);
        if (result != null)
        {
            return result;
        }

        // If December fails (e.g., stock not yet listed), try earlier months
        _logger.LogDebug("No December data for {StockNo}/{Year}, trying November", stockNo, year);
        return await GetStockPriceAsync(stockNo, new DateOnly(year, 11, 30), cancellationToken);
    }

    private TwseStockPriceResult? ParseStockDayResponse(string content, string stockNo, DateOnly targetDate)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // Check for valid response
            if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK")
            {
                var statValue = stat.ValueKind == JsonValueKind.String ? stat.GetString() : "unknown";
                _logger.LogDebug("TWSE response stat: {Stat} for {StockNo}", statValue, stockNo);
                return null;
            }

            if (!root.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                _logger.LogDebug("No data in TWSE response for {StockNo}", stockNo);
                return null;
            }

            // Data format: [日期, 成交股數, 成交金額, 開盤價, 最高價, 最低價, 收盤價, 漲跌價差, 成交筆數]
            // Date format in data: "114/01/02" (ROC year/month/day)
            // Find the closest date <= target date

            TwseStockPriceResult? bestResult = null;

            foreach (var row in data.EnumerateArray())
            {
                if (row.GetArrayLength() < 7)
                    continue;

                var dateStr = row[0].GetString();
                var closePriceStr = row[6].GetString();

                if (string.IsNullOrEmpty(dateStr) || string.IsNullOrEmpty(closePriceStr))
                    continue;

                // Parse ROC date (e.g., "114/01/02")
                var dateParts = dateStr.Split('/');
                if (dateParts.Length != 3)
                    continue;

                if (!int.TryParse(dateParts[0], out var rocYear) ||
                    !int.TryParse(dateParts[1], out var month) ||
                    !int.TryParse(dateParts[2], out var day))
                    continue;

                var year = rocYear + 1911;
                var rowDate = new DateOnly(year, month, day);

                // Skip dates after target
                if (rowDate > targetDate)
                    continue;

                // Parse price (remove commas)
                var priceStr = closePriceStr.Replace(",", "");
                if (!decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    continue;

                // Keep the latest date <= target
                if (bestResult == null || rowDate > bestResult.ActualDate)
                {
                    bestResult = new TwseStockPriceResult(price, rowDate, stockNo);
                }
            }

            if (bestResult != null)
            {
                _logger.LogDebug("Found TWSE price for {StockNo}: {Price} on {Date}",
                    stockNo, bestResult.Price, bestResult.ActualDate);
            }

            return bestResult;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse TWSE response for {StockNo}", stockNo);
            return null;
        }
    }
}
