using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching Taiwan Stock Exchange index prices
/// Uses TWSE official API for both real-time and historical data
/// </summary>
public class TwseIndexPriceService : ITwseIndexPriceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TwseIndexPriceService> _logger;

    private const string RealTimeUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp?ex_ch=tse_t00.tw&json=1&delay=0";
    private const string HistoricalUrl = "https://www.twse.com.tw/exchangeReport/FMTQIK?response=json&date=";

    public TwseIndexPriceService(HttpClient httpClient, ILogger<TwseIndexPriceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<decimal?> GetCurrentPriceAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, RealTimeUrl);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TWSE returned {Status}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonDocument.Parse(content);

            var msgArray = json.RootElement.GetProperty("msgArray");
            if (msgArray.GetArrayLength() == 0)
            {
                _logger.LogWarning("Empty msgArray from TWSE");
                return null;
            }

            var data = msgArray[0];
            if (data.TryGetProperty("z", out var priceElement))
            {
                var priceStr = priceElement.GetString();
                if (!string.IsNullOrEmpty(priceStr) && priceStr != "-" &&
                    decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                {
                    _logger.LogDebug("Got TWII real-time price {Price} from TWSE", price);
                    return price;
                }
            }

            // Fallback to yesterday's close if market is closed
            if (data.TryGetProperty("y", out var closeElement))
            {
                var closeStr = closeElement.GetString();
                if (!string.IsNullOrEmpty(closeStr) &&
                    decimal.TryParse(closeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var close))
                {
                    _logger.LogDebug("Got TWII previous close {Price} from TWSE (market closed)", close);
                    return close;
                }
            }

            _logger.LogWarning("Could not parse TWSE price data");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching TWII price from TWSE");
            return null;
        }
    }

    public async Task<decimal?> GetCurrentPriceWithFallbackAsync(CancellationToken cancellationToken = default)
    {
        // Try real-time API first
        var realTimePrice = await GetCurrentPriceAsync(cancellationToken);
        if (realTimePrice != null)
        {
            return realTimePrice;
        }

        // Fallback: use the latest available historical data
        _logger.LogInformation("Real-time TWSE API failed, trying historical fallback");

        // Try current month first
        var now = DateTime.Now;
        var price = await GetMonthEndPriceAsync(now.Year, now.Month, cancellationToken);
        if (price != null)
        {
            _logger.LogDebug("Using current month's latest price {Price} as fallback", price);
            return price;
        }

        // Try previous month if current month has no data yet
        var prevMonth = now.AddMonths(-1);
        price = await GetMonthEndPriceAsync(prevMonth.Year, prevMonth.Month, cancellationToken);
        if (price != null)
        {
            _logger.LogDebug("Using previous month's price {Price} as fallback", price);
            return price;
        }

        return null;
    }

    public async Task<decimal?> GetMonthEndPriceAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        try
        {
            // TWSE uses ROC year (民國年)
            var rocYear = year - 1911;
            var dateParam = $"{year}{month:D2}01";

            var url = $"{HistoricalUrl}{dateParam}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("TWSE historical API returned {Status}", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonDocument.Parse(content);

            if (!json.RootElement.TryGetProperty("data", out var dataArray) ||
                dataArray.GetArrayLength() == 0)
            {
                _logger.LogWarning("No historical data from TWSE for {Year}/{Month}", year, month);
                return null;
            }

            // Get last trading day of the month (last row)
            var lastRow = dataArray[dataArray.GetArrayLength() - 1];

            // Field index 4 is "發行量加權股價指數"
            var indexValue = lastRow[4].GetString();
            if (!string.IsNullOrEmpty(indexValue))
            {
                // Remove commas from number
                indexValue = indexValue.Replace(",", "");
                if (decimal.TryParse(indexValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                {
                    _logger.LogDebug("Got TWII month-end price {Price} for {Year}/{Month} from TWSE", price, year, month);
                    return price;
                }
            }

            _logger.LogWarning("Could not parse TWSE historical price for {Year}/{Month}", year, month);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching TWII historical price from TWSE for {Year}/{Month}", year, month);
            return null;
        }
    }
}

public interface ITwseIndexPriceService
{
    Task<decimal?> GetCurrentPriceAsync(CancellationToken cancellationToken = default);
    Task<decimal?> GetCurrentPriceWithFallbackAsync(CancellationToken cancellationToken = default);
    Task<decimal?> GetMonthEndPriceAsync(int year, int month, CancellationToken cancellationToken = default);
}
