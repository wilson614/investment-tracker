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
        ["All Country"] = "vwra.uk",                // Vanguard FTSE All-World UCITS ETF (Acc)
        ["US Large"] = "vuaa.uk",                   // Vanguard S&P 500 UCITS ETF (Acc)
        ["US Small"] = "xrsu.uk",                   // Xtrackers Russell 2000 UCITS ETF (Acc)
        ["Developed Markets Large"] = "vhve.uk",   // Vanguard FTSE Developed World UCITS ETF (Acc)
        ["Developed Markets Small"] = "wsml.uk",   // iShares MSCI World Small Cap UCITS ETF (Acc)
        ["Dev ex US Large"] = "exus.uk",           // Vanguard FTSE Developed ex US UCITS ETF (Acc)
        ["Emerging Markets"] = "vfem.uk",          // Vanguard FTSE Emerging Markets UCITS ETF (Acc)
        ["Europe"] = "veua.uk",                    // Vanguard FTSE Developed Europe UCITS ETF (Acc)
        ["Japan"] = "vjpa.uk",                     // Vanguard FTSE Japan UCITS ETF (Acc)
        ["China"] = "hcha.uk",                     // HSBC MSCI China A UCITS ETF (Acc)
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

    /// <summary>
    /// Get historical price for a stock ticker on a specific date.
    /// Tries multiple market suffixes to find the stock.
    /// </summary>
    public async Task<StooqPriceResult?> GetStockPriceAsync(
        string ticker,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        // Normalize ticker - remove any existing suffix
        var baseTicker = ticker.Split('.')[0].ToUpperInvariant();

        // Determine which market suffixes to try based on ticker pattern
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

        _logger.LogWarning("Could not find historical price for {Ticker} on {Date} from Stooq", ticker, date);
        return null;
    }

    /// <summary>
    /// Determine which market suffixes to try based on ticker pattern
    /// </summary>
    private static string[] GetMarketSuffixes(string ticker)
    {
        var upperTicker = ticker.ToUpperInvariant();

        // If ticker already has a suffix, use it directly
        if (upperTicker.Contains('.'))
        {
            var suffix = "." + upperTicker.Split('.')[^1].ToLowerInvariant();
            return new[] { suffix };
        }

        // UK-listed ETFs (common patterns)
        if (upperTicker is "VWRA" or "VWRD" or "VUAA" or "VUSA" or "VHVE" or "VFEM" or
            "VEVE" or "VJPA" or "VEUA" or "WSML" or "XRSU" or "EXUS" or "HCHA" or
            "SWDA" or "IWDA" or "EIMI" or "EMIM" or "CSPX")
        {
            return new[] { StooqMarkets.UK };
        }

        // Euronext tickers (Amsterdam, Paris)
        if (upperTicker.StartsWith("VWCE") || upperTicker.StartsWith("V3AA"))
        {
            return new[] { StooqMarkets.DE, StooqMarkets.NL };
        }

        // Default: try US first, then UK
        return new[] { StooqMarkets.US, StooqMarkets.UK };
    }

    /// <summary>
    /// Try to get price for a specific symbol from Stooq
    /// </summary>
    private async Task<StooqPriceResult?> TryGetStockPriceAsync(
        string symbol,
        DateOnly date,
        CancellationToken cancellationToken)
    {
        try
        {
            // Fetch a 10-day window ending on the specified date to handle weekends/holidays
            var endDate = date;
            var startDate = date.AddDays(-10);

            var url = $"{BaseUrl}?s={symbol}&d1={startDate:yyyyMMdd}&d2={endDate:yyyyMMdd}&i=d";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Stooq returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseStockPrice(content, symbol, date);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error fetching {Symbol} from Stooq", symbol);
            return null;
        }
    }

    /// <summary>
    /// Parse Stooq CSV and find the closest price to the target date
    /// </summary>
    private StooqPriceResult? ParseStockPrice(string csvContent, string symbol, DateOnly targetDate)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            return null;
        }

        // Parse all data lines and find the one closest to (but not after) target date
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

            // Determine currency from symbol
            // IMPORTANT: Many UK-listed ETFs are USD-denominated (Vanguard UCITS ETFs)
            var baseTicker = symbol.Split('.')[0].ToUpperInvariant();
            var currency = GetTradingCurrency(baseTicker, symbol);

            // UK ETFs quoted in GBX (pence) need conversion to GBP
            // Only applies to GBP-denominated securities
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
            _logger.LogDebug("Got historical price {Price} {Currency} for {Symbol} on {Date} from Stooq",
                bestMatch.Price, bestMatch.Currency, symbol, bestMatch.ActualDate);
        }

        return bestMatch;
    }

    /// <summary>
    /// Determine the actual trading currency for a ticker.
    /// Many UK-listed ETFs are USD-denominated (especially Vanguard UCITS ETFs).
    /// </summary>
    private static string GetTradingCurrency(string baseTicker, string fullSymbol)
    {
        // USD-denominated ETFs commonly traded on LSE
        // These are all Accumulating/Distributing versions of Vanguard UCITS ETFs
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
            "WSML",
        };

        if (usdDenominatedEtfs.Contains(baseTicker))
        {
            return "USD";
        }

        // Default to currency based on exchange suffix
        var lowerSymbol = fullSymbol.ToLowerInvariant();
        return lowerSymbol switch
        {
            var s when s.EndsWith(".uk") => "GBP",
            var s when s.EndsWith(".de") => "EUR",
            var s when s.EndsWith(".fr") => "EUR",
            var s when s.EndsWith(".nl") => "EUR",
            var s when s.EndsWith(".jp") => "JPY",
            _ => "USD"
        };
    }

    /// <summary>
    /// Get historical exchange rate from Stooq.
    /// Symbol format: e.g., "usdtwd" for USD to TWD
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

            // Fetch a 10-day window ending on the specified date to handle weekends/holidays
            var endDate = date;
            var startDate = date.AddDays(-10);

            var url = $"{BaseUrl}?s={symbol}&d1={startDate:yyyyMMdd}&d2={endDate:yyyyMMdd}&i=d";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Stooq returned {Status} for exchange rate {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseExchangeRate(content, fromCurrency.ToUpperInvariant(), toCurrency.ToUpperInvariant(), date);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching exchange rate {From}/{To} from Stooq", fromCurrency, toCurrency);
            return null;
        }
    }

    /// <summary>
    /// Parse Stooq CSV and find the closest exchange rate to the target date
    /// </summary>
    private StooqExchangeRateResult? ParseExchangeRate(string csvContent, string fromCurrency, string toCurrency, DateOnly targetDate)
    {
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length < 2)
        {
            return null;
        }

        // Check for "No data" response
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
            _logger.LogDebug("Got historical exchange rate {Rate} for {From}/{To} on {Date} from Stooq",
                bestMatch.Rate, fromCurrency, toCurrency, bestMatch.ActualDate);
        }

        return bestMatch;
    }
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
