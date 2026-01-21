using System.Globalization;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 Stooq 取得 ETF 歷史價格的服務。
/// Stooq 提供英國掛牌 ETF 的免費歷史資料。
/// </summary>
public class StooqHistoricalPriceService(HttpClient httpClient, ILogger<StooqHistoricalPriceService> logger) : IStooqHistoricalPriceService
{
    private const string BaseUrl = "https://stooq.com/q/d/l/";

    // Stooq 的 ETF symbol 對應（英國掛牌，部分以 USD 計價）
    private static readonly Dictionary<string, string> StooqSymbols = new()
    {
        ["All Country"] = "vwra.uk",                // Vanguard FTSE All-World UCITS ETF (Acc)
        ["US Large"] = "vuaa.uk",                   // Vanguard S&P 500 UCITS ETF (Acc)
        ["US Small"] = "xrsu.uk",                   // Xtrackers Russell 2000 UCITS ETF (Acc)
        ["Developed Markets Large"] = "vhve.uk",   // Vanguard FTSE Developed World UCITS ETF (Acc)
        ["Developed Markets Small"] = "wsml.uk",   // iShares MSCI World Small Cap UCITS ETF (Acc)
        ["Dev ex US Large"] = "exus.uk",           // Vanguard FTSE Developed ex US UCITS ETF (Acc)
        ["Emerging Markets"] = "vfem.uk",          // Vanguard FTSE Emerging Markets UCITS ETF (Acc)
        ["Europe"] = "veua.uk",                    // Vanguard FTSE Developed Europe UCITS ETF (Acc)
        ["Japan"] = "vjpa.uk",                     // Vanguard FTSE Japan UCITS ETF (Acc)
        ["China"] = "hcha.uk" // HSBC MSCI China A UCITS ETF (Acc)
    };

    public static IReadOnlyCollection<string> SupportedMarkets => StooqSymbols.Keys;

    /// <summary>
    /// 取得指定月份最後一個交易日的收盤價。
    /// </summary>
    public async Task<decimal?> GetMonthEndPriceAsync(
        string marketKey,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (!StooqSymbols.TryGetValue(marketKey, out var symbol))
        {
            logger.LogDebug("No Stooq symbol mapping for {Market}", marketKey);
            return null;
        }

        try
        {
            // 取得當月最後一天
            var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            // 往前抓一週，避免月底落在週末而拿不到資料
            var startDay = lastDay.AddDays(-7);

            var url = $"{BaseUrl}?s={symbol}&d1={startDay:yyyyMMdd}&d2={lastDay:yyyyMMdd}&i=d";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Stooq returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Stooq 可能在被 rate limited 時回傳 HTTP 200 + error.csv。
            return content.Contains("Exceeded the daily hits limit", StringComparison.OrdinalIgnoreCase) ? throw new StooqDailyHitsLimitExceededException(symbol) : ParseLastClose(content, marketKey);
        }
        catch (StooqDailyHitsLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching historical price from Stooq for {Market}", marketKey);
            return null;
        }
    }

    /// <summary>
    /// 解析 Stooq 的 CSV 回應並取得最後一筆收盤價。
    /// 格式：Date,Open,High,Low,Close,Volume
    /// </summary>
    private decimal? ParseLastClose(string csvContent, string marketKey)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            logger.LogWarning("No data returned from Stooq for {Market}", marketKey);
            return null;
        }

        // 取得最後一筆資料列（最新日期）
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
            logger.LogWarning("Invalid Stooq data format for {Market}: {Line}", marketKey, lastLine);
            return null;
        }

        // 第 4 欄為 Close（0-indexed）
        if (decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var closePrice))
        {
            logger.LogDebug("Got historical price {Price} for {Market} from Stooq", closePrice, marketKey);
            return closePrice;
        }

        return null;
    }

    /// <summary>
    /// 取得指定日期的個股歷史價格。
    /// 會嘗試多個市場 suffix，以提高命中率。
    /// </summary>
    public async Task<StooqPriceResult?> GetStockPriceAsync(
        string ticker,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // 正規化 ticker：移除既有 suffix
        var baseTicker = ticker.Split('.')[0].ToUpperInvariant();

        // 依 ticker 型態決定要嘗試的市場 suffix
        var suffixes = GetMarketSuffixes(ticker);

        foreach (var suffix in suffixes)
        {
            var symbol = baseTicker + suffix;
            var result = await TryGetStockPriceAsync(symbol, date, cancellationToken);
            if (result != null)
            {
                return result;
            }
        }

        logger.LogWarning("Could not find historical price for {Ticker} on {Date} from Stooq", ticker, date);
        return null;
    }

    /// <summary>
    /// 依 ticker 型態決定要嘗試的市場 suffix。
    /// </summary>
    private static string[] GetMarketSuffixes(string ticker)
    {
        var upperTicker = ticker.ToUpperInvariant();

        // 若 ticker 已包含 suffix，直接使用
        if (upperTicker.Contains('.'))
        {
            var suffix = "." + upperTicker.Split('.')[^1].ToLowerInvariant();
            return [suffix];
        }

        // 英國掛牌 ETF（常見代號）
        if (upperTicker is "VWRA" or "VWRD" or "VUAA" or "VUSA" or "VHVE" or "VFEM" or
            "VEVE" or "VJPA" or "VEUA" or "WSML" or "XRSU" or "EXUS" or "HCHA" or
            "SWDA" or "IWDA" or "EIMI" or "EMIM" or "CSPX")
        {
            return [StooqMarkets.UK];
        }

        // Euronext（阿姆斯特丹、巴黎）常見代號
        if (upperTicker.StartsWith("VWCE") || upperTicker.StartsWith("V3AA"))
        {
            return [StooqMarkets.DE, StooqMarkets.NL];
        }

        // 預設：先嘗試美股，再嘗試英股
        return [StooqMarkets.US, StooqMarkets.UK];
    }

    /// <summary>
    /// 嘗試從 Stooq 取得指定 symbol 的價格。
    /// </summary>
    private async Task<StooqPriceResult?> TryGetStockPriceAsync(
        string symbol,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        try
        {
            // 抓取以指定日期結尾的 10 天區間，處理週末／假日
            var startDate = date.AddDays(-10);

            var url = $"{BaseUrl}?s={symbol}&d1={startDate:yyyyMMdd}&d2={date:yyyyMMdd}&i=d";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Stooq returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Stooq 可能在被 rate limited 時回傳 HTTP 200 + error.csv。
            return content.Contains("Exceeded the daily hits limit", StringComparison.OrdinalIgnoreCase) ? throw new StooqDailyHitsLimitExceededException(symbol) : ParseStockPrice(content, symbol, date);
        }
        catch (StooqDailyHitsLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error fetching {Symbol} from Stooq", symbol);
            return null;
        }
    }

    /// <summary>
    /// 解析 Stooq CSV，找出最接近目標日期（且不晚於目標日期）的價格。
    /// </summary>
    private StooqPriceResult? ParseStockPrice(string csvContent, string symbol, DateOnly targetDate)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            return null;
        }

        // 解析所有資料列，找出最接近（且不晚於）目標日期的那一筆
        StooqPriceResult? bestMatch = null;

        foreach (var line in lines.Skip(1)) // Skip header
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var parts = trimmed.Split(',');
            if (parts.Length < 5)
                continue;

            if (!DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lineDate))
                continue;

            if (lineDate > targetDate)
                continue;

            if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var closePrice))
                continue;

            // 由 symbol 推導交易幣別
            // 注意：許多英國掛牌 ETF（特別是 Vanguard UCITS ETFs）以 USD 計價
            var baseTicker = symbol.Split('.')[0].ToUpperInvariant();
            var currency = GetTradingCurrency(baseTicker, symbol);

            // 英股若以 GBX（便士）報價，需換算成 GBP
            // 僅適用於 GBP 計價的標的
            if (currency == "GBP" && closePrice > 100)
            {
                closePrice /= 100m;
            }

            if (bestMatch == null || lineDate > bestMatch.ActualDate)
            {
                bestMatch = new StooqPriceResult(closePrice, lineDate, currency);
            }
        }

        if (bestMatch != null)
        {
            logger.LogDebug("Got historical price {Price} {Currency} for {Symbol} on {Date} from Stooq",
                bestMatch.Price, bestMatch.Currency, symbol, bestMatch.ActualDate);
        }

        return bestMatch;
    }

    /// <summary>
    /// 判斷 ticker 的實際交易幣別。
    /// 許多英國掛牌 ETF（特別是 Vanguard UCITS ETFs）以 USD 計價。
    /// </summary>
    private static string GetTradingCurrency(string baseTicker, string fullSymbol)
    {
        // LSE 常見的 USD 計價 ETF
        // 多為 Vanguard UCITS ETFs 的累積／配息版本
        var usdDenominatedEtfs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Vanguard FTSE All-World
            "VWRA", "VWRD", "VWRL",
            // Vanguard S&P 500
            "VUAA", "VUSA",
            // Vanguard FTSE Developed World
            "VHVE", "VHVG",
            // Vanguard FTSE Developed Europe
            "VEUR", "VERX", "VEUA",
            // Vanguard FTSE Emerging Markets
            "VFEM", "VFEG",
            // Vanguard FTSE Japan
            "VJPA", "VJPN",
            // iShares Core MSCI World
            "SWDA", "IWDA",
            // iShares Core S&P 500
            "CSPX",
            // iShares MSCI EM
            "EIMI", "EMIM",
            // Xtrackers
            "XRSU", "EXUS",
            // HSBC China
            "HCHA",
            // iShares World Small Cap
            "WSML"
        };

        if (usdDenominatedEtfs.Contains(baseTicker))
        {
            return "USD";
        }

        // 預設以交易所 suffix 推導幣別
        var lowerSymbol = fullSymbol.ToLowerInvariant();
        return lowerSymbol switch
        {
            _ when lowerSymbol.EndsWith(".uk") => "GBP",
            _ when lowerSymbol.EndsWith(".de") => "EUR",
            _ when lowerSymbol.EndsWith(".fr") => "EUR",
            _ when lowerSymbol.EndsWith(".nl") => "EUR",
            _ when lowerSymbol.EndsWith(".jp") => "JPY",
            _ => "USD"
        };
    }

    /// <summary>
    /// 從 Stooq 取得歷史匯率。
    /// Symbol 格式：例如 "usdtwd" 代表 USD/TWD。
    /// </summary>
    public async Task<StooqExchangeRateResult?> GetExchangeRateAsync(
        string fromCurrency,
        string toCurrency,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var symbol = $"{fromCurrency.ToLowerInvariant()}{toCurrency.ToLowerInvariant()}";

            // 抓取以指定日期結尾的 10 天區間，處理週末／假日
            var startDate = date.AddDays(-10);

            var url = $"{BaseUrl}?s={symbol}&d1={startDate:yyyyMMdd}&d2={date:yyyyMMdd}&i=d";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogDebug("Stooq returned {Status} for exchange rate {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            // Stooq 可能在被 rate limited 時回傳 HTTP 200 + error.csv。
            return content.Contains("Exceeded the daily hits limit", StringComparison.OrdinalIgnoreCase) ? throw new StooqDailyHitsLimitExceededException(symbol) : ParseExchangeRate(content, fromCurrency.ToUpperInvariant(), toCurrency.ToUpperInvariant(), date);
        }
        catch (StooqDailyHitsLimitExceededException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching exchange rate {From}/{To} from Stooq", fromCurrency, toCurrency);
            return null;
        }
    }

    /// <summary>
    /// 解析 Stooq CSV，找出最接近目標日期（且不晚於目標日期）的匯率。
    /// </summary>
    private StooqExchangeRateResult? ParseExchangeRate(string csvContent, string fromCurrency, string toCurrency, DateOnly targetDate)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            return null;
        }

        // 檢查是否為 "No data" 回應
        if (lines.Any(l => l.Trim().Equals("No data", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        StooqExchangeRateResult? bestMatch = null;

        foreach (var line in lines.Skip(1)) // Skip header
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            var parts = trimmed.Split(',');
            if (parts.Length < 5)
                continue;

            if (!DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var lineDate))
                continue;

            if (lineDate > targetDate)
                continue;

            if (!decimal.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out var closeRate))
                continue;

            if (bestMatch == null || lineDate > bestMatch.ActualDate)
            {
                bestMatch = new StooqExchangeRateResult(closeRate, lineDate, fromCurrency, toCurrency);
            }
        }

        if (bestMatch != null)
        {
            logger.LogDebug("Got historical exchange rate {Rate} for {From}/{To} on {Date} from Stooq",
                bestMatch.Rate, fromCurrency, toCurrency, bestMatch.ActualDate);
        }

        return bestMatch;
    }
}

public sealed class StooqDailyHitsLimitExceededException(string symbol)
    : Exception($"Stooq daily hits limit exceeded (symbol={symbol})")
{
    public string Symbol { get; } = symbol;
}

public interface IStooqHistoricalPriceService
{
    Task<decimal?> GetMonthEndPriceAsync(string marketKey, int year, int month, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the closing price for a specific stock ticker on a given date.
    /// Returns the closing price for the nearest trading day on or before the specified date.
    /// </summary>
    Task<StooqPriceResult?> GetStockPriceAsync(string ticker, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get historical exchange rate from Stooq.
    /// Returns the closing rate for the nearest trading day on or before the specified date.
    /// </summary>
    Task<StooqExchangeRateResult?> GetExchangeRateAsync(string fromCurrency, string toCurrency, DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from Stooq price lookup
/// </summary>
public record StooqPriceResult(decimal Price, DateOnly ActualDate, string Currency);

/// <summary>
/// Result from Stooq exchange rate lookup
/// </summary>
public record StooqExchangeRateResult(decimal Rate, DateOnly ActualDate, string FromCurrency, string ToCurrency);

/// <summary>
/// Stooq market suffix mappings
/// </summary>
public static class StooqMarkets
{
    public const string US = "";        // No suffix for US stocks
    public const string UK = ".uk";     // London Stock Exchange
    public const string DE = ".de";     // Germany (Xetra)
    public const string FR = ".fr";     // France (Euronext Paris)
    public const string NL = ".nl";     // Netherlands (Euronext Amsterdam)
    public const string JP = ".jp";     // Japan
}
