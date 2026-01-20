using System.Globalization;
using System.Text;
using InvestmentTracker.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// 使用 Sina Finance API 的匯率提供者。
/// 支援常見幣別對（例如 USD/TWD、GBP/USD 等）。
/// 對於 GBP/TWD 這類 cross rate，會以 USD 作為中介（GBP/USD * USD/TWD）。
/// </summary>
public interface IExchangeRateProvider
{
    Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default);
}

public class SinaExchangeRateProvider(HttpClient httpClient, ILogger<SinaExchangeRateProvider> logger) : IExchangeRateProvider
{
    private const string BaseUrl = "http://hq.sinajs.cn/list=";
    private const string Referer = "http://vip.stock.finance.sina.com.cn";

    // Sina 支援的 direct pair
    private static readonly HashSet<string> DirectPairs =
    [
        "usdtwd", "usdcny", "usdjpy", "usdchf", "usdcad", "usdhkd", "usdsgd",
        "gbpusd", "eurusd", "audusd", "nzdusd",
    ];

    static SinaExchangeRateProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<ExchangeRateResponse?> GetExchangeRateAsync(string fromCurrency, string toCurrency, CancellationToken cancellationToken = default)
    {
        var from = fromCurrency.ToUpperInvariant();
        var to = toCurrency.ToUpperInvariant();

        // 同幣別
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

        // 先嘗試 direct pair
        if (DirectPairs.Contains(directPair))
        {
            var result = await FetchRateAsync(directPair, from, to, cancellationToken);
            if (result != null) return result;
        }

        // 再嘗試 inverse pair
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

        // 對於 GBP/TWD 這類 cross rate，以 USD 作為中介計算
        if (to == "TWD" && from != "USD")
        {
            var toUsd = await GetCrossRateViaUsd(from, to, cancellationToken);
            if (toUsd != null) return toUsd;
        }

        logger.LogWarning("Could not determine exchange rate for {From}/{To}", from, to);
        return null;
    }

    private async Task<ExchangeRateResponse?> GetCrossRateViaUsd(string from, string to, CancellationToken cancellationToken)
    {
        // 計算 cross rate：FROM/TO = FROM/USD * USD/TO
        // 例如 GBP/TWD：GBP/USD * USD/TWD

        // Step 1：取得 FROM/USD（例如 GBP/USD）
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
            // 嘗試 inverse：USD/FROM
            var usdFromPair = $"USD{from}".ToLowerInvariant();
            if (!DirectPairs.Contains(usdFromPair)) return null;

            var inverseResult = await FetchRateAsync(usdFromPair, "USD", from, cancellationToken);
            if (inverseResult == null || inverseResult.Rate <= 0) return null;
            fromToUsd = 1m / inverseResult.Rate;
        }

        // Step 2：取得 USD/TO（例如 USD/TWD）
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
        logger.LogInformation("Fetching exchange rate from Sina: {Url}", url);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", Referer);

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var gbkEncoding = Encoding.GetEncoding("GBK");
            var content = gbkEncoding.GetString(bytes);
            logger.LogDebug("Sina response for {Pair}: {Content}", pair, content);

            return ParseExchangeRateResponse(content, from, to);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch exchange rate from Sina for {Pair}", pair);
            return null;
        }
    }

    private ExchangeRateResponse? ParseExchangeRateResponse(string content, string fromCurrency, string toCurrency)
    {
        // 回應格式：var hq_str_fx_susdtwd="30.5234,30.5678,..."
        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Empty response from Sina for {From}/{To}", fromCurrency, toCurrency);
            return null;
        }

        var startIndex = content.IndexOf('"');
        var endIndex = content.LastIndexOf('"');

        if (startIndex < 0 || endIndex <= startIndex)
        {
            logger.LogWarning("Invalid response format from Sina: {Content}", content);
            return null;
        }

        var data = content.Substring(startIndex + 1, endIndex - startIndex - 1);
        if (string.IsNullOrWhiteSpace(data))
        {
            logger.LogWarning("No data found for {From}/{To}", fromCurrency, toCurrency);
            return null;
        }

        var fields = data.Split(',');

        // Forex 資料格式：time,bid,ask,latest,volume,high,low,...
        // 目前匯率通常為 index 3（latest），但實務上 index 1（bid）更穩定
        if (fields.Length < 4)
        {
            logger.LogWarning("Insufficient fields in Sina response for {From}/{To}: {Data}", fromCurrency, toCurrency, data);
            return null;
        }

        // 優先使用 current/latest（index 3），失敗則回退使用 bid（index 1）
        decimal rate = 0;
        if (!decimal.TryParse(fields[3], NumberStyles.Any, CultureInfo.InvariantCulture, out rate) || rate <= 0)
        {
            if (!decimal.TryParse(fields[1], NumberStyles.Any, CultureInfo.InvariantCulture, out rate) || rate <= 0)
            {
                logger.LogWarning("Invalid rate in Sina response for {From}/{To}: {Data}", fromCurrency, toCurrency, data);
                return null;
            }
        }

        logger.LogInformation("Successfully parsed exchange rate {From}/{To}: {Rate}", fromCurrency, toCurrency, rate);
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
