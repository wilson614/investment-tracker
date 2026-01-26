using System.Globalization;
using System.Text.Json;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 Yahoo Finance 取得歷史價格的服務實作。
/// 用於 Euronext、UK 等 Stooq 不支援的市場。
/// 注意：調用者應傳入已轉換好的 Yahoo Finance 符號（如 SWRD.L, AGAC.AS）。
/// 符號轉換邏輯統一在 HistoricalYearEndDataService.ConvertToYahooSymbol 進行。
/// </summary>
public class YahooHistoricalPriceService(
    HttpClient httpClient,
    ILogger<YahooHistoricalPriceService> logger) : IYahooHistoricalPriceService
{
    public async Task<YahooAnnualTotalReturnResult?> GetAnnualTotalReturnAsync(
        string symbol,
        int year,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (year < 2000 || year > DateTime.UtcNow.Year)
            {
                return null;
            }

            var yahooSymbol = symbol;

            // 用於推估「年度 Total Return」：使用 Yahoo chart 的 adjclose (調整後收盤價)。
            // adjclose 通常反映股息與分割調整，可作為 total return proxy。
            var startDate = new DateOnly(year, 1, 1);
            var endDate = new DateOnly(year, 12, 31);

            // 取前後緩衝區間以避開週末/假日，並透過 Parse 取最接近交易日。
            var period1 = new DateTimeOffset(startDate.AddDays(-7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(endDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

            var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{yahooSymbol}?period1={period1}&period2={period2}&interval=1d";

            logger.LogDebug("Fetching Yahoo Finance annual total return (adjclose) from {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Yahoo Finance API returned {StatusCode} for annual total return {Symbol}/{Year}", response.StatusCode, yahooSymbol, year);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var parsed = ParseAnnualTotalReturnFromChart(json, startDate, endDate, yahooSymbol);

            if (parsed == null)
            {
                return null;
            }

            return new YahooAnnualTotalReturnResult
            {
                TotalReturnPercent = parsed.Value.totalReturnPercent,
                PriceReturnPercent = parsed.Value.priceReturnPercent,
                Year = year
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching Yahoo Finance annual total return for {Symbol}/{Year}", symbol, year);
            return null;
        }
    }

    private (decimal totalReturnPercent, decimal? priceReturnPercent)? ParseAnnualTotalReturnFromChart(
        string json,
        DateOnly startDate,
        DateOnly endDate,
        string symbol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("chart", out var chart) &&
                chart.TryGetProperty("error", out var error) &&
                error.ValueKind != JsonValueKind.Null)
            {
                logger.LogWarning("Yahoo Finance API error: {Error}", error.GetRawText());
                return null;
            }

            if (!chart.TryGetProperty("result", out var results) || results.GetArrayLength() == 0)
            {
                logger.LogWarning("Yahoo Finance: No results for {Symbol}", symbol);
                return null;
            }

            var firstResult = results[0];

            if (!firstResult.TryGetProperty("timestamp", out var timestamps) ||
                !firstResult.TryGetProperty("indicators", out var indicators))
            {
                logger.LogWarning("Yahoo Finance: Missing data for {Symbol}", symbol);
                return null;
            }

            // quote.close (price return)
            decimal? startClose = null;
            decimal? endClose = null;

            if (indicators.TryGetProperty("quote", out var quotes) && quotes.GetArrayLength() > 0)
            {
                var quote = quotes[0];
                if (quote.TryGetProperty("close", out var closes))
                {
                    (startClose, endClose) = FindStartEndValues(timestamps, closes, startDate, endDate);
                }
            }

            // adjclose.adjclose (total return proxy)
            decimal? startAdj = null;
            decimal? endAdj = null;

            if (indicators.TryGetProperty("adjclose", out var adjcloses) && adjcloses.GetArrayLength() > 0)
            {
                var adjclose = adjcloses[0];
                if (adjclose.TryGetProperty("adjclose", out var adjs))
                {
                    (startAdj, endAdj) = FindStartEndValues(timestamps, adjs, startDate, endDate);
                }
            }

            if (startAdj is not > 0 || endAdj is null)
            {
                return null;
            }

            var totalReturnPercent = (endAdj.Value - startAdj.Value) / startAdj.Value * 100m;
            totalReturnPercent = Math.Round(totalReturnPercent, 4);

            decimal? priceReturnPercent = null;
            if (startClose is > 0 && endClose is not null)
            {
                var priceReturn = (endClose.Value - startClose.Value) / startClose.Value * 100m;
                priceReturnPercent = Math.Round(priceReturn, 4);
            }

            return (totalReturnPercent, priceReturnPercent);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Yahoo Finance chart response for annual total return {Symbol}", symbol);
            return null;
        }
    }

    private static (decimal? start, decimal? end) FindStartEndValues(
        JsonElement timestamps,
        JsonElement values,
        DateOnly startDate,
        DateOnly endDate)
    {
        var tsArray = timestamps.EnumerateArray().ToList();
        var vArray = values.EnumerateArray().ToList();

        var startTarget = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var endTarget = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

        decimal? start = null;
        decimal? end = null;

        // start: first non-null at or after start date, otherwise first available
        for (int i = 0; i < tsArray.Count && i < vArray.Count; i++)
        {
            var ts = tsArray[i].GetInt64();
            if (ts < startTarget)
                continue;

            var v = vArray[i];
            if (v.ValueKind == JsonValueKind.Null)
                continue;

            start = v.GetDecimal();
            break;
        }

        if (start is null)
        {
            for (int i = 0; i < tsArray.Count && i < vArray.Count; i++)
            {
                var v = vArray[i];
                if (v.ValueKind == JsonValueKind.Null)
                    continue;

                start = v.GetDecimal();
                break;
            }
        }

        // end: last non-null at or before end date, otherwise last available
        for (int i = Math.Min(tsArray.Count, vArray.Count) - 1; i >= 0; i--)
        {
            var ts = tsArray[i].GetInt64();
            if (ts > endTarget)
                continue;

            var v = vArray[i];
            if (v.ValueKind == JsonValueKind.Null)
                continue;

            end = v.GetDecimal();
            break;
        }

        if (end is null)
        {
            for (int i = Math.Min(tsArray.Count, vArray.Count) - 1; i >= 0; i--)
            {
                var v = vArray[i];
                if (v.ValueKind == JsonValueKind.Null)
                    continue;

                end = v.GetDecimal();
                break;
            }
        }

        return (start, end);
    }
    public async Task<YahooHistoricalPriceResult?> GetHistoricalPriceAsync(
        string symbol,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 直接使用傳入的符號（調用者應已完成轉換）
            var yahooSymbol = symbol;

            // 計算時間範圍：目標日期前後各 7 天，確保能找到交易日
            var startDate = date.AddDays(-7);
            var endDate = date.AddDays(1);

            var period1 = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

            var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{yahooSymbol}?period1={period1}&period2={period2}&interval=1d";

            logger.LogDebug("Fetching Yahoo Finance historical price from {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Yahoo Finance API returned {StatusCode} for {Symbol}", response.StatusCode, yahooSymbol);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = ParseYahooResponse(json, date, yahooSymbol);

            if (result != null)
            {
                logger.LogInformation("Yahoo Finance: {Symbol} on {Date} = {Price} {Currency}",
                    yahooSymbol, result.ActualDate, result.Price, result.Currency);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching Yahoo Finance historical price for {Symbol}/{Date}", symbol, date);
            return null;
        }
    }

    private YahooHistoricalPriceResult? ParseYahooResponse(string json, DateOnly targetDate, string symbol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check for errors
            if (root.TryGetProperty("chart", out var chart) &&
                chart.TryGetProperty("error", out var error) &&
                error.ValueKind != JsonValueKind.Null)
            {
                logger.LogWarning("Yahoo Finance API error: {Error}", error.GetRawText());
                return null;
            }

            if (!chart.TryGetProperty("result", out var results) ||
                results.GetArrayLength() == 0)
            {
                logger.LogWarning("Yahoo Finance: No results for {Symbol}", symbol);
                return null;
            }

            var firstResult = results[0];

            // Get currency from meta
            var currency = "USD";
            if (firstResult.TryGetProperty("meta", out var meta) &&
                meta.TryGetProperty("currency", out var currencyElement))
            {
                currency = currencyElement.GetString() ?? "USD";
            }

            // Get timestamps and close prices
            if (!firstResult.TryGetProperty("timestamp", out var timestamps) ||
                !firstResult.TryGetProperty("indicators", out var indicators) ||
                !indicators.TryGetProperty("quote", out var quotes) ||
                quotes.GetArrayLength() == 0)
            {
                logger.LogWarning("Yahoo Finance: Missing price data for {Symbol}", symbol);
                return null;
            }

            var quote = quotes[0];
            if (!quote.TryGetProperty("close", out var closes))
            {
                logger.LogWarning("Yahoo Finance: Missing close prices for {Symbol}", symbol);
                return null;
            }

            // Find the price closest to target date (prefer same day or earlier)
            var timestampArray = timestamps.EnumerateArray().ToList();
            var closeArray = closes.EnumerateArray().ToList();

            if (timestampArray.Count == 0 || closeArray.Count == 0)
            {
                return null;
            }

            // Convert target date to Unix timestamp for comparison
            var targetTimestamp = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

            // Find the last trading day on or before target date
            int? bestIndex = null;
            long bestTimestamp = 0;

            for (int i = 0; i < timestampArray.Count && i < closeArray.Count; i++)
            {
                var ts = timestampArray[i].GetInt64();
                var closeValue = closeArray[i];

                // Skip null values
                if (closeValue.ValueKind == JsonValueKind.Null)
                    continue;

                // Find latest date on or before target
                if (ts <= targetTimestamp && ts > bestTimestamp)
                {
                    bestIndex = i;
                    bestTimestamp = ts;
                }
            }

            if (bestIndex == null)
            {
                // If no date before target, use the first available
                for (int i = 0; i < timestampArray.Count && i < closeArray.Count; i++)
                {
                    if (closeArray[i].ValueKind != JsonValueKind.Null)
                    {
                        bestIndex = i;
                        bestTimestamp = timestampArray[i].GetInt64();
                        break;
                    }
                }
            }

            if (bestIndex == null)
            {
                return null;
            }

            var price = closeArray[bestIndex.Value].GetDecimal();
            var actualDate = DateTimeOffset.FromUnixTimeSeconds(bestTimestamp).DateTime;

            return new YahooHistoricalPriceResult
            {
                Price = Math.Round(price, 4),
                ActualDate = DateOnly.FromDateTime(actualDate),
                Currency = currency
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Yahoo Finance response for {Symbol}", symbol);
            return null;
        }
    }

    public async Task<YahooExchangeRateResult?> GetExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Yahoo Finance 匯率符號格式：USDTWD=X
            var yahooSymbol = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}=X";

            // 計算時間範圍：目標日期前後各 7 天，確保能找到交易日
            var startDate = date.AddDays(-7);
            var endDate = date.AddDays(1);

            var period1 = new DateTimeOffset(startDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
            var period2 = new DateTimeOffset(endDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

            var url = $"https://query2.finance.yahoo.com/v8/finance/chart/{yahooSymbol}?period1={period1}&period2={period2}&interval=1d";

            logger.LogDebug("Fetching Yahoo Finance exchange rate from {Url}", url);

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Yahoo Finance API returned {StatusCode} for {Symbol}", response.StatusCode, yahooSymbol);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = ParseExchangeRateResponse(json, date, fromCurrency, toCurrency);

            if (result != null)
            {
                logger.LogInformation("Yahoo Finance: {CurrencyPair} on {Date} = {Rate}",
                    result.CurrencyPair, result.ActualDate, result.Rate);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error fetching Yahoo Finance exchange rate for {From}/{To}/{Date}",
                fromCurrency, toCurrency, date);
            return null;
        }
    }

    private YahooExchangeRateResult? ParseExchangeRateResponse(string json, DateOnly targetDate, string fromCurrency, string toCurrency)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Check for errors
            if (root.TryGetProperty("chart", out var chart) &&
                chart.TryGetProperty("error", out var error) &&
                error.ValueKind != JsonValueKind.Null)
            {
                logger.LogWarning("Yahoo Finance API error: {Error}", error.GetRawText());
                return null;
            }

            if (!chart.TryGetProperty("result", out var results) ||
                results.GetArrayLength() == 0)
            {
                logger.LogWarning("Yahoo Finance: No results for {From}/{To}", fromCurrency, toCurrency);
                return null;
            }

            var firstResult = results[0];

            // Get timestamps and close prices
            if (!firstResult.TryGetProperty("timestamp", out var timestamps) ||
                !firstResult.TryGetProperty("indicators", out var indicators) ||
                !indicators.TryGetProperty("quote", out var quotes) ||
                quotes.GetArrayLength() == 0)
            {
                logger.LogWarning("Yahoo Finance: Missing rate data for {From}/{To}", fromCurrency, toCurrency);
                return null;
            }

            var quote = quotes[0];
            if (!quote.TryGetProperty("close", out var closes))
            {
                logger.LogWarning("Yahoo Finance: Missing close rates for {From}/{To}", fromCurrency, toCurrency);
                return null;
            }

            // Find the rate closest to target date (prefer same day or earlier)
            var timestampArray = timestamps.EnumerateArray().ToList();
            var closeArray = closes.EnumerateArray().ToList();

            if (timestampArray.Count == 0 || closeArray.Count == 0)
            {
                return null;
            }

            // Convert target date to Unix timestamp for comparison
            var targetTimestamp = new DateTimeOffset(targetDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

            // Find the last trading day on or before target date
            int? bestIndex = null;
            long bestTimestamp = 0;

            for (int i = 0; i < timestampArray.Count && i < closeArray.Count; i++)
            {
                var ts = timestampArray[i].GetInt64();
                var closeValue = closeArray[i];

                // Skip null values
                if (closeValue.ValueKind == JsonValueKind.Null)
                    continue;

                // Find latest date on or before target
                if (ts <= targetTimestamp && ts > bestTimestamp)
                {
                    bestIndex = i;
                    bestTimestamp = ts;
                }
            }

            if (bestIndex == null)
            {
                // If no date before target, use the first available
                for (int i = 0; i < timestampArray.Count && i < closeArray.Count; i++)
                {
                    if (closeArray[i].ValueKind != JsonValueKind.Null)
                    {
                        bestIndex = i;
                        bestTimestamp = timestampArray[i].GetInt64();
                        break;
                    }
                }
            }

            if (bestIndex == null)
            {
                return null;
            }

            var rate = closeArray[bestIndex.Value].GetDecimal();
            var actualDate = DateTimeOffset.FromUnixTimeSeconds(bestTimestamp).DateTime;

            return new YahooExchangeRateResult
            {
                Rate = Math.Round(rate, 6),
                ActualDate = DateOnly.FromDateTime(actualDate),
                CurrencyPair = $"{fromCurrency.ToUpperInvariant()}{toCurrency.ToUpperInvariant()}"
            };
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse Yahoo Finance exchange rate response for {From}/{To}",
                fromCurrency, toCurrency);
            return null;
        }
    }
}
