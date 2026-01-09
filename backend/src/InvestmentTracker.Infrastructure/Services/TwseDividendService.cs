using System.Text.Json;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// DTO for dividend record
/// </summary>
public record DividendRecord(
    DateTime ExDividendDate,
    string StockNo,
    string StockName,
    decimal DividendAmount,
    string DividendType // "息" for cash dividend
);

/// <summary>
/// Service for fetching dividend data from TWSE
/// Used to adjust YTD calculations for dividend distributions
/// </summary>
public interface ITwseDividendService
{
    /// <summary>
    /// Get dividends for a stock within a date range
    /// </summary>
    Task<List<DividendRecord>> GetDividendsAsync(
        string stockNo,
        int year,
        CancellationToken cancellationToken = default);
}

public class TwseDividendService : ITwseDividendService
{
    private readonly HttpClient _httpClient;
    private readonly ITwseRateLimiter _rateLimiter;
    private readonly ILogger<TwseDividendService> _logger;

    public TwseDividendService(
        HttpClient httpClient,
        ITwseRateLimiter rateLimiter,
        ILogger<TwseDividendService> logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<List<DividendRecord>> GetDividendsAsync(
        string stockNo,
        int year,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DividendRecord>();

        try
        {
            await _rateLimiter.WaitForSlotAsync(cancellationToken);

            // TWSE uses ROC year (民國年) - subtract 1911
            var rocYear = year - 1911;
            var startDate = $"{year}0101";
            var endDate = $"{year}1231";

            // TWT49U returns historical dividend calculation results
            var url = $"https://www.twse.com.tw/rwd/zh/exRight/TWT49U?startDate={startDate}&endDate={endDate}&stockNo={stockNo}&response=json";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TWSE dividend API returned {Status}", response.StatusCode);
                return results;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonDocument.Parse(content);

            if (!json.RootElement.TryGetProperty("data", out var dataArray))
            {
                _logger.LogDebug("No dividend data for {StockNo} in {Year}", stockNo, year);
                return results;
            }

            // Fields: [0]=日期, [1]=股票代號, [2]=名稱, [3]=除息前收盤價, [4]=除息參考價, [5]=權值+息值, [6]=權/息
            foreach (var row in dataArray.EnumerateArray())
            {
                var rowStockNo = row[1].GetString();
                if (rowStockNo != stockNo)
                    continue;

                var dividendType = row[6].GetString();
                // Only process cash dividends (息), skip stock dividends (權)
                if (dividendType != "息")
                    continue;

                var dateStr = row[0].GetString(); // e.g., "114年01月17日"
                var stockName = row[2].GetString() ?? stockNo;
                var dividendStr = row[5].GetString();

                if (!TryParseRocDate(dateStr, out var exDate))
                {
                    _logger.LogWarning("Failed to parse date: {Date}", dateStr);
                    continue;
                }

                if (!decimal.TryParse(dividendStr, out var dividendAmount))
                {
                    _logger.LogWarning("Failed to parse dividend amount: {Amount}", dividendStr);
                    continue;
                }

                results.Add(new DividendRecord(
                    exDate,
                    stockNo,
                    stockName,
                    dividendAmount,
                    dividendType
                ));

                _logger.LogDebug("Found dividend for {StockNo}: {Date} = {Amount}",
                    stockNo, exDate.ToString("yyyy-MM-dd"), dividendAmount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching dividends for {StockNo} in {Year}", stockNo, year);
        }

        return results;
    }

    /// <summary>
    /// Parse ROC date format (e.g., "114年01月17日") to DateTime
    /// </summary>
    private static bool TryParseRocDate(string? dateStr, out DateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(dateStr))
            return false;

        try
        {
            // Extract year, month, day from "114年01月17日"
            var yearEnd = dateStr.IndexOf('年');
            var monthEnd = dateStr.IndexOf('月');
            var dayEnd = dateStr.IndexOf('日');

            if (yearEnd < 0 || monthEnd < 0 || dayEnd < 0)
                return false;

            var rocYear = int.Parse(dateStr[..yearEnd]);
            var month = int.Parse(dateStr[(yearEnd + 1)..monthEnd]);
            var day = int.Parse(dateStr[(monthEnd + 1)..dayEnd]);

            var year = rocYear + 1911; // Convert ROC year to AD
            result = new DateTime(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
