using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Caching.Memory;
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
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    IMemoryCache memoryCache,
    ILogger<TwseStockHistoricalPriceService> logger) : ITwseStockHistoricalPriceService
{
    // TWSE 個股日資料 API（上市）
    private const string StockDayUrl = "https://www.twse.com.tw/exchangeReport/STOCK_DAY";

    // TPEx 每日收盤行情 API（上櫃）
    private const string TpexDailyQuotesUrl = "https://www.tpex.org.tw/www/zh-tw/afterTrading/dailyQuotes";

    // 上櫃 fallback 最多往前回溯天數，涵蓋跨月與長假情境。
    private const int TpexMaxLookbackDays = 45;

    private static readonly TimeSpan HotCacheDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromMinutes(3);

    // key: stockNo + yyyyMMdd，避免同一個價格查詢被併發重複打外部 API
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InFlightLocks = new(StringComparer.Ordinal);

    public async Task<TwseStockPriceResult?> GetStockPriceAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var normalizedStockNo = NormalizeStockNo(stockNo);
        if (normalizedStockNo is null)
        {
            return null;
        }

        var cacheKey = BuildCacheKey(normalizedStockNo, date);
        if (memoryCache.TryGetValue<TwseStockPriceResult?>(cacheKey, out var cachedResult))
        {
            return cachedResult;
        }

        var gate = InFlightLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            if (memoryCache.TryGetValue<TwseStockPriceResult?>(cacheKey, out cachedResult))
            {
                return cachedResult;
            }

            var fetched = await FetchStockPriceCoreAsync(normalizedStockNo, date, cancellationToken);
            memoryCache.Set(
                cacheKey,
                fetched,
                fetched is null ? NegativeCacheDuration : HotCacheDuration);
            return fetched;
        }
        finally
        {
            gate.Release();
            InFlightLocks.TryRemove(cacheKey, out _);
        }
    }

    private async Task<TwseStockPriceResult?> FetchStockPriceCoreAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        try
        {
            // 月初遇到連假/週末時，該月可能沒有任何 <= 目標日期的交易日。
            // 這會導致回傳 null，進而讓快照估值變成 0（會把 TWR 乘到 0 變成 -100%）。
            // 因此當月初（1-7 日）查不到資料時，回退到上一個月再查一次。

            var result = await TryGetStockPriceInMonthAsync(stockNo, date, cancellationToken);
            if (result != null)
            {
                return result;
            }

            if (date.Day <= 7)
            {
                var previousMonth = date.AddMonths(-1);
                var fallbackDate = new DateOnly(previousMonth.Year, previousMonth.Month, DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month));

                logger.LogDebug(
                    "No TWSE/TPEx price found for {StockNo} on {Date} (early-month). Falling back to previous month {FallbackDate}",
                    stockNo,
                    date,
                    fallbackDate);

                result = await TryGetStockPriceInMonthAsync(stockNo, fallbackDate, cancellationToken);
                if (result != null)
                {
                    return result;
                }
            }

            return await TryGetStockPriceFromYahooFallbackAsync(stockNo, date, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching TWSE stock price for {StockNo} on {Date}", stockNo, date);
            return null;
        }
    }

    private async Task<TwseStockPriceResult?> TryGetStockPriceInMonthAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await rateLimiter.WaitForSlotAsync(cancellationToken);

        // TWSE API 需要日期格式：YYYYMM01（該月第一天）
        var dateParam = $"{date.Year}{date.Month:D2}01";
        var url = $"{StockDayUrl}?response=json&date={dateParam}&stockNo={stockNo}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("TWSE STOCK_DAY returned {Status} for {StockNo}", response.StatusCode, stockNo);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var twseResult = ParseTwseStockDayResponse(content, stockNo, date);

        if (twseResult.Result != null)
        {
            return twseResult.Result;
        }

        // 只有 TWSE 明確回應「查無資料」時，才改走上櫃日行情 fallback。
        // 這可避免影響上市在月中查無 <= target 交易日時的既有流程。
        if (!twseResult.ShouldTryTpexFallback)
        {
            return null;
        }

        logger.LogDebug(
            "TWSE has no stock-day data for {StockNo} on {Date}, trying TPEx daily quotes fallback",
            stockNo,
            date);

        return await TryGetStockPriceFromTpexAsync(stockNo, date, cancellationToken);
    }

    private async Task<TwseStockPriceResult?> TryGetStockPriceFromTpexAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        for (var offset = 0; offset <= TpexMaxLookbackDays; offset++)
        {
            var lookupDate = date.AddDays(-offset);
            var result = await TryGetTpexDailyPriceAsync(stockNo, lookupDate, cancellationToken);
            if (result != null)
            {
                return result;
            }
        }

        logger.LogDebug(
            "No TPEx data found for {StockNo} within lookback window ({LookbackDays} days) from {Date}",
            stockNo,
            TpexMaxLookbackDays,
            date);

        return null;
    }

    private async Task<TwseStockPriceResult?> TryGetStockPriceFromYahooFallbackAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        // 先嘗試上市符號（.TW），失敗再嘗試上櫃符號（.TWO）。
        // 使用既有 Yahoo 服務作為跨來源 fallback，優先確保可用性與速度。
        foreach (var suffix in new[] { ".TW", ".TWO" })
        {
            var symbol = $"{stockNo}{suffix}";

            try
            {
                var yahoo = await yahooHistoricalPriceService.GetHistoricalPriceAsync(symbol, date, cancellationToken);
                if (yahoo == null)
                {
                    continue;
                }

                logger.LogDebug(
                    "Yahoo fallback hit for {StockNo}: {Symbol} => {Price} on {Date}",
                    stockNo,
                    symbol,
                    yahoo.Price,
                    yahoo.ActualDate);

                return new TwseStockPriceResult(
                    yahoo.Price,
                    yahoo.ActualDate,
                    stockNo,
                    Source: $"Yahoo:{symbol}");
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Yahoo fallback failed for {StockNo} with symbol {Symbol}", stockNo, symbol);
            }
        }

        logger.LogWarning("No historical price found for {StockNo} from TWSE/TPEx/Yahoo fallback on {Date}", stockNo, date);
        return null;
    }

    private async Task<TwseStockPriceResult?> TryGetTpexDailyPriceAsync(
        string stockNo,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        await rateLimiter.WaitForSlotAsync(cancellationToken);

        var dateParam = date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);
        var url = $"{TpexDailyQuotesUrl}?date={Uri.EscapeDataString(dateParam)}&id={stockNo}&response=json";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogDebug("TPEx dailyQuotes returned {Status} for {StockNo} on {Date}", response.StatusCode, stockNo, date);
            return null;
        }

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseTpexDailyQuotesResponse(content, stockNo, date);
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

    private TwseStockDayParseResult ParseTwseStockDayResponse(string content, string stockNo, DateOnly targetDate)
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

                return new TwseStockDayParseResult(
                    Result: null,
                    ShouldTryTpexFallback: IsTwseNoDataStat(statValue));
            }

            if (!root.TryGetProperty("data", out var data) || data.GetArrayLength() == 0)
            {
                logger.LogDebug("No data in TWSE response for {StockNo}", stockNo);
                return new TwseStockDayParseResult(Result: null, ShouldTryTpexFallback: false);
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
                    bestResult = new TwseStockPriceResult(price, rowDate, stockNo, Source: "TWSE");
                }
            }

            if (bestResult != null)
            {
                logger.LogDebug("Found TWSE price for {StockNo}: {Price} on {Date}",
                    stockNo, bestResult.Price, bestResult.ActualDate);
            }

            return new TwseStockDayParseResult(Result: bestResult, ShouldTryTpexFallback: false);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse TWSE response for {StockNo}", stockNo);
            return new TwseStockDayParseResult(Result: null, ShouldTryTpexFallback: false);
        }
    }

    private TwseStockPriceResult? ParseTpexDailyQuotesResponse(string content, string stockNo, DateOnly targetDate)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (root.TryGetProperty("stat", out var stat) &&
                !string.Equals(stat.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogDebug("TPEx response stat: {Stat} for {StockNo} on {Date}", stat.GetString(), stockNo, targetDate);
                return null;
            }

            // API 可能在日期格式錯誤時回傳最新資料，需驗證日期一致性。
            if (root.TryGetProperty("date", out var dateElement))
            {
                var dateString = dateElement.GetString();
                if (!string.IsNullOrWhiteSpace(dateString) &&
                    DateOnly.TryParseExact(dateString, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var responseDate) &&
                    responseDate != targetDate)
                {
                    logger.LogDebug(
                        "TPEx response date mismatch for {StockNo}: requested {RequestedDate}, got {ResponseDate}",
                        stockNo,
                        targetDate,
                        responseDate);
                    return null;
                }
            }

            if (!root.TryGetProperty("tables", out var tables) || tables.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (var table in tables.EnumerateArray())
            {
                if (!table.TryGetProperty("data", out var rows) || rows.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var row in rows.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 3)
                    {
                        continue;
                    }

                    var rowStockNo = row[0].GetString();
                    if (!string.Equals(rowStockNo, stockNo, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var closePriceStr = row[2].GetString();
                    if (string.IsNullOrWhiteSpace(closePriceStr))
                    {
                        return null;
                    }

                    var normalizedPrice = closePriceStr.Replace(",", string.Empty).Trim();
                    if (!decimal.TryParse(normalizedPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
                    {
                        return null;
                    }

                    logger.LogDebug("Found TPEx price for {StockNo}: {Price} on {Date}", stockNo, price, targetDate);
                    return new TwseStockPriceResult(price, targetDate, stockNo, Source: "TPEx");
                }
            }

            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse TPEx response for {StockNo} on {Date}", stockNo, targetDate);
            return null;
        }
    }

    private static string? NormalizeStockNo(string? stockNo)
    {
        if (string.IsNullOrWhiteSpace(stockNo))
        {
            return null;
        }

        return stockNo.Trim().ToUpperInvariant();
    }

    private static string BuildCacheKey(string stockNo, DateOnly date)
        => $"twse-stock-price:{stockNo}:{date:yyyyMMdd}";

    private static bool IsTwseNoDataStat(string? statValue)
    {
        if (string.IsNullOrWhiteSpace(statValue))
        {
            return false;
        }

        return statValue.Contains("沒有符合條件", StringComparison.Ordinal) ||
               statValue.Contains("no data", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TwseStockDayParseResult(
        TwseStockPriceResult? Result,
        bool ShouldTryTpexFallback);
}
