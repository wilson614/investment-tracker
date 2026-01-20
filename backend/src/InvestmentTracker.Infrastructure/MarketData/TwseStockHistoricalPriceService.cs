using System.Globalization;
using System.Text.Json;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 TWSE 取得台股個股歷史價格的服務。
/// 使用 TWSE STOCK_DAY API 取得日成交資料。
/// 透過 ITwseRateLimiter 進行 rate limiting，避免被封鎖。
/// </summary>
public class TwseStockHistoricalPriceService(
    HttpClient httpClient,
    ITwseRateLimiter rateLimiter,
    ILogger<TwseStockHistoricalPriceService> logger) : ITwseStockHistoricalPriceService
{
    // TWSE 個股日資料 API
    private const string StockDayUrl = "https://www.twse.com.tw/exchangeReport/STOCK_DAY";

    public async Task<TwseStockPriceResult?> GetStockPriceAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await rateLimiter.WaitForSlotAsync(cancellationToken);

            // TWSE API 需要日期格式：YYYYMM01（該月第一天）
            var dateParam = $"{date.Year}{date.Month:D2}01";
            var url = $"{StockDayUrl}?response=json&date={dateParam}&stockNo={stockNo}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TWSE STOCK_DAY returned {Status} for {StockNo}", response.StatusCode, stockNo);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseStockDayResponse(content, stockNo, date);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching TWSE stock price for {StockNo} on {Date}", stockNo, date);
            return null;
        }
    }

    public async Task<TwseStockPriceResult?> GetYearEndPriceAsync(
        string stockNo,
        int year,
        CancellationToken cancellationToken = default)
    {
        // 先嘗試 12 月
        var result = await GetStockPriceAsync(stockNo, new DateOnly(year, 12, 31), cancellationToken);
        if (result != null)
        {
            return result;
        }

        // 若 12 月失敗（例如尚未上市），改嘗試更早月份
        logger.LogDebug("No December data for {StockNo}/{Year}, trying November", stockNo, year);
        return await GetStockPriceAsync(stockNo, new DateOnly(year, 11, 30), cancellationToken);
    }

    private TwseStockPriceResult? ParseStockDayResponse(string content, string stockNo, DateOnly targetDate)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            // 確認回應是否有效
            if (!root.TryGetProperty("stat", out var stat) || stat.GetString() != "OK")
            {
                var statValue = stat.ValueKind == JsonValueKind.String ? stat.GetString() : "unknown";
                logger.LogDebug("TWSE response stat: {Stat} for {StockNo}", statValue, stockNo);
                return null;
            }

            if (!root.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                logger.LogDebug("No data in TWSE response for {StockNo}", stockNo);
                return null;
            }

            // 資料格式：[日期, 成交股數, 成交金額, 開盤價, 最高價, 最低價, 收盤價, 漲跌價差, 成交筆數]
            // 日期格式："114/01/02"（民國年/月/日）
            // 找出最接近（且不晚於）target date 的那一筆

            TwseStockPriceResult? bestResult = null;

            foreach (var row in data.EnumerateArray())
            {
                if (row.GetArrayLength() < 7)
                    continue;

                var dateStr = row[0].GetString();
                var closePriceStr = row[6].GetString();

                if (string.IsNullOrEmpty(dateStr) || string.IsNullOrEmpty(closePriceStr))
                    continue;

                // 解析民國日期（例如 "114/01/02"）
                var dateParts = dateStr.Split('/');
                if (dateParts.Length != 3)
                    continue;

                if (!int.TryParse(dateParts[0], out var rocYear) ||
                    !int.TryParse(dateParts[1], out var month) ||
                    !int.TryParse(dateParts[2], out var day))
                    continue;

                var year = rocYear + 1911;
                var rowDate = new DateOnly(year, month, day);

                // 略過晚於 target 的日期
                if (rowDate > targetDate)
                    continue;

                // 解析價格（移除逗號）
                var priceStr = closePriceStr.Replace(",", "");
                if (!decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    continue;

                // 保留最後一筆 <= target 的日期
                if (bestResult == null || rowDate > bestResult.ActualDate)
                {
                    bestResult = new TwseStockPriceResult(price, rowDate, stockNo);
                }
            }

            if (bestResult != null)
            {
                logger.LogDebug("Found TWSE price for {StockNo}: {Price} on {Date}",
                    stockNo, bestResult.Price, bestResult.ActualDate);
            }

            return bestResult;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse TWSE response for {StockNo}", stockNo);
            return null;
        }
    }
}
