using System.Globalization;
using System.Text;
using InvestmentTracker.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// Exchange rate provider using Sina Finance API
/// Supports major currency pairs like USD/TWD, GBP/USD, etc.
/// For pairs like GBP/TWD, calculates via cross rate (GBP/USD * USD/TWD)
/// </summary>
public interface IExchangeRateProvider
{
    Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default);
}

public class SinaExchangeRateProvider : IExchangeRateProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SinaExchangeRateProvider> _logger;
    private const string BaseUrl = "http://hq.sinajs.cn/list=";
    private const string Referer = "http://vip.stock.finance.sina.com.cn";

    // Direct pairs supported by Sina
    private static readonly HashSet<string> DirectPairs = new()
    {
        "usdtwd", "usdcny", "usdjpy", "usdchf", "usdcad", "usdhkd", "usdsgd",
        "gbpusd", "eurusd", "audusd", "nzdusd"
    };

    static SinaExchangeRateProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SinaExchangeRateProvider(HttpClient httpClient, ILogger<SinaExchangeRateProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default)
    {
        var from = fromCurrency.ToUpperInvariant();
        var to = toCurrency.ToUpperInvariant();

        // Same currency
        if (from == to)
        {
            return new ExchangeRateResponse
            {
                FromCurrency = from,
                ToCurrency = to,
                Rate = 1m,
                Source = "Same Currency",
                FetchedAt = DateTime.UtcNow
            };
        }

        var directPair = $"{from}{to}".ToLowerInvariant();

        // Try direct pair first
        if (DirectPairs.Contains(directPair))
        {
            var result = await FetchRateAsync(directPair, from, to, cancellationToken);
            if (result != null) return result;
        }

        // Try inverse pair
        var inversePair = $"{to}{from}".ToLowerInvariant();
        if (DirectPairs.Contains(inversePair))
        {
            var inverseResult = await FetchRateAsync(inversePair, to, from, cancellationToken);
            if (inverseResult != null && inverseResult.Rate > 0)
            {
                return new ExchangeRateResponse
                {
                    FromCurrency = from,
                    ToCurrency = to,
                    Rate = 1m / inverseResult.Rate,
                    Source = "Sina Finance (inverse)",
                    FetchedAt = DateTime.UtcNow
                };
            }
        }

        // For cross rates like GBP/TWD, calculate via USD
        if (to == "TWD" && from != "USD")
        {
            var toUsd = await GetCrossRateViaUsd(from, to, cancellationToken);
            if (toUsd != null) return toUsd;
        }

        _logger.LogWarning("Could not determine exchange rate for {From}/{To}", from, to);
        return null;
    }

    private async Task<ExchangeRateResponse?> GetCrossRateViaUsd(string from, string to, CancellationToken cancellationToken)
    {
        // Calculate cross rate: FROM/TO = FROM/USD * USD/TO
        // For GBP/TWD: GBP/USD * USD/TWD

        // Step 1: Get FROM/USD (e.g., GBP/USD)
        var fromUsdPair = $"{from}USD".ToLowerInvariant();
        decimal fromToUsd;

        if (DirectPairs.Contains(fromUsdPair))
        {
            var result = await FetchRateAsync(fromUsdPair, from, "USD", cancellationToken);
            if (result == null) return null;
            fromToUsd = result.Rate;
        }
        else
        {
            // Try inverse: USD/FROM
            var usdFromPair = $"USD{from}".ToLowerInvariant();
            if (!DirectPairs.Contains(usdFromPair)) return null;

            var inverseResult = await FetchRateAsync(usdFromPair, "USD", from, cancellationToken);
            if (inverseResult == null || inverseResult.Rate <= 0) return null;
            fromToUsd = 1m / inverseResult.Rate;
        }

        // Step 2: Get USD/TO (e.g., USD/TWD)
        var usdToPair = $"USD{to}".ToLowerInvariant();
        if (!DirectPairs.Contains(usdToPair)) return null;

        var usdToResult = await FetchRateAsync(usdToPair, "USD", to, cancellationToken);
        if (usdToResult == null) return null;

        // Cross rate
        var crossRate = fromToUsd * usdToResult.Rate;

        return new ExchangeRateResponse
        {
            FromCurrency = from,
            ToCurrency = to,
            Rate = Math.Round(crossRate, 4),
            Source = "Sina Finance (cross rate)",
            FetchedAt = DateTime.UtcNow
        };
    }

    private async Task<ExchangeRateResponse?> FetchRateAsync(string pair, string from, string to, CancellationToken cancellationToken)
    {
        var sinaSymbol = $"fx_s{pair}";
        var url = $"{BaseUrl}{sinaSymbol}";
        _logger.LogInformation("Fetching exchange rate from Sina: {Url}", url);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", Referer);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var gbkEncoding = Encoding.GetEncoding("GBK");
            var content = gbkEncoding.GetString(bytes);
            _logger.LogDebug("Sina response for {Pair}: {Content}", pair, content);

            return ParseExchangeRateResponse(content, from, to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch exchange rate from Sina for {Pair}", pair);
            return null;
        }
    }

    private ExchangeRateResponse? ParseExchangeRateResponse(string content, string fromCurrency, string toCurrency)
    {
        // Response format: var hq_str_fx_susdtwd="30.5234,30.5678,..."
        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Empty response from Sina for {From}/{To}", fromCurrency, toCurrency);
            return null;
        }

        var startIndex = content.IndexOf('"');
        var endIndex = content.LastIndexOf('"');

        if (startIndex < 0 || endIndex <= startIndex)
        {
            _logger.LogWarning("Invalid response format from Sina: {Content}", content);
            return null;
        }

        var data = content.Substring(startIndex + 1, endIndex - startIndex - 1);
        if (string.IsNullOrWhiteSpace(data))
        {
            _logger.LogWarning("No data found for {From}/{To}", fromCurrency, toCurrency);
            return null;
        }

        var fields = data.Split(',');

        // Forex data format: time,bid,ask,latest,volume,high,low,...
        // The current rate is typically the "latest" at index 3, but bid (index 1) works better
        if (fields.Length < 4)
        {
            _logger.LogWarning("Insufficient fields in Sina response for {From}/{To}: {Data}", fromCurrency, toCurrency, data);
            return null;
        }

        // Try to get the current/latest rate (index 3), fallback to bid (index 1)
        decimal rate = 0;
        if (!decimal.TryParse(fields[3], NumberStyles.Any, CultureInfo.InvariantCulture, out rate) || rate <= 0)
        {
            if (!decimal.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out rate) || rate <= 0)
            {
                _logger.LogWarning("Invalid rate in Sina response for {From}/{To}: {Data}", fromCurrency, toCurrency, data);
                return null;
            }
        }

        _logger.LogInformation("Successfully parsed exchange rate {From}/{To}: {Rate}", fromCurrency, toCurrency, rate);
        return new ExchangeRateResponse
        {
            FromCurrency = fromCurrency.ToUpperInvariant(),
            ToCurrency = toCurrency.ToUpperInvariant(),
            Rate = rate,
            Source = "Sina Finance",
            FetchedAt = DateTime.UtcNow
        };
    }
}
