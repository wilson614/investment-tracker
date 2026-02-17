using System.Globalization;
using System.Text.Json;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// 使用 TWSE（台灣證券交易所）API 的股價提供者。
/// 內含 rate limiting，避免被封鎖（5 秒內最多 3 次請求）。
/// </summary>
public class TwseStockPriceProvider(
    HttpClient httpClient,
    ITwseRateLimiter rateLimiter,
    ILogger<TwseStockPriceProvider> logger) : IStockPriceProvider
{
    private const string BaseUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp";

    public bool SupportsMarket(StockMarket market) => market == StockMarket.TW;

    public async Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default)
    {
        if (!SupportsMarket(market))
        {
            throw new ArgumentException($"Market {market} is not supported by TwseStockPriceProvider");
        }

        var normalizedSymbol = symbol.Trim();

        var tseResult = await TryFetchQuoteAsync(normalizedSymbol, "tse", cancellationToken);
        if (tseResult.Status == QuoteFetchStatus.Success)
        {
            return tseResult.Quote;
        }

        if (tseResult.Status == QuoteFetchStatus.UpstreamError)
        {
            logger.LogWarning(
                "Skip OTC fallback because TSE upstream error occurred for {Symbol}",
                normalizedSymbol);
            return null;
        }

        // Only fallback to OTC when TSE explicitly indicates no data.
        var otcResult = await TryFetchQuoteAsync(normalizedSymbol, "otc", cancellationToken);
        return otcResult.Status == QuoteFetchStatus.Success ? otcResult.Quote : null;
    }

    private async Task<QuoteFetchResult> TryFetchQuoteAsync(string symbol, string exchange, CancellationToken cancellationToken)
    {
        var exCh = $"{exchange}_{symbol}.tw";
        var url = $"{BaseUrl}?ex_ch={exCh}&json=1&delay=0";

        try
        {
            // 發送請求前先等待 rate limit 的可用額度
            await rateLimiter.WaitForSlotAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "TWSE quote upstream returned non-success status {StatusCode} for {Exchange}:{Symbol}",
                    response.StatusCode,
                    exchange,
                    symbol);
                return QuoteFetchResult.UpstreamError();
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseTwseResponse(content, symbol, exchange);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch stock price from TWSE for {Exchange}:{Symbol}", exchange, symbol);
            return QuoteFetchResult.UpstreamError();
        }
    }

    private QuoteFetchResult ParseTwseResponse(string content, string symbol, string exchange)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("msgArray", out var msgArray) || msgArray.GetArrayLength() == 0)
            {
                logger.LogDebug("No data found for {Exchange}:{Symbol}", exchange, symbol);
                return QuoteFetchResult.NoData();
            }

            var stock = msgArray[0];

            if (!HasValidQuotePayload(stock))
            {
                logger.LogDebug("No valid quote payload for {Exchange}:{Symbol}", exchange, symbol);
                return QuoteFetchResult.NoData();
            }

            // 欄位解析
            // z：最新成交價、n：名稱、y：昨收
            // o：開盤、h：最高、l：最低、v：成交量

            var priceStr = stock.TryGetProperty("z", out var zProp) ? zProp.GetString() : null;
            var name = stock.TryGetProperty("n", out var nProp) ? nProp.GetString() : symbol;
            var yesterdayStr = stock.TryGetProperty("y", out var yProp) ? yProp.GetString() : null;

            decimal? yesterdayClose = null;
            if (!string.IsNullOrEmpty(yesterdayStr) &&
                decimal.TryParse(yesterdayStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var yClose))
            {
                yesterdayClose = yClose;
            }

            decimal price;
            // z 可能為 "-"（尚未成交）；此時回退使用昨收價
            if (string.IsNullOrEmpty(priceStr) || priceStr == "-")
            {
                if (yesterdayClose is null or <= 0)
                {
                    logger.LogDebug("No trade price available for {Exchange}:{Symbol}", exchange, symbol);
                    return QuoteFetchResult.NoData();
                }

                price = yesterdayClose.Value;
                logger.LogDebug(
                    "No trade price available for {Exchange}:{Symbol}, using previous close {Price} as fallback",
                    exchange,
                    symbol,
                    price);
            }
            else if (!decimal.TryParse(priceStr, NumberStyles.Any, CultureInfo.InvariantCulture, out price))
            {
                logger.LogWarning("Invalid price format for {Exchange}:{Symbol}: {Price}", exchange, symbol, priceStr);
                return QuoteFetchResult.UpstreamError();
            }

            decimal? change = null;
            string? changePercent = null;

            if (yesterdayClose is not null && yesterdayClose > 0)
            {
                change = price - yesterdayClose;
                var pctValue = change.Value / yesterdayClose.Value * 100;
                var sign = pctValue >= 0 ? "+" : "";
                changePercent = $"{sign}{pctValue:F2}%";
            }

            return QuoteFetchResult.Success(new StockQuoteResponse
            {
                Symbol = symbol.ToUpperInvariant(),
                Name = name ?? symbol,
                Price = price,
                Change = change,
                ChangePercent = changePercent,
                Market = StockMarket.TW,
                Source = GetSourceName(exchange),
                FetchedAt = DateTime.UtcNow
            });
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse TWSE response for {Exchange}:{Symbol}", exchange, symbol);
            return QuoteFetchResult.UpstreamError();
        }
    }

    private static bool HasValidQuotePayload(JsonElement stock)
    {
        // 當查無標的時，TWSE 會回傳精簡資料：{"tv":"-","s":"-","c":"","z":"-"}
        // 這類 payload 沒有關鍵欄位（n/y），應視為無資料，交由 fallback 來源處理。

        var hasName = stock.TryGetProperty("n", out var nameProp) &&
                      !string.IsNullOrWhiteSpace(nameProp.GetString());

        var hasPreviousClose = stock.TryGetProperty("y", out var prevCloseProp) &&
                               !string.IsNullOrWhiteSpace(prevCloseProp.GetString()) &&
                               !string.Equals(prevCloseProp.GetString(), "-", StringComparison.Ordinal);

        var hasTradePrice = stock.TryGetProperty("z", out var tradePriceProp) &&
                            !string.IsNullOrWhiteSpace(tradePriceProp.GetString()) &&
                            !string.Equals(tradePriceProp.GetString(), "-", StringComparison.Ordinal);

        return hasName || hasPreviousClose || hasTradePrice;
    }

    private static string GetSourceName(string exchange)
        => string.Equals(exchange, "tse", StringComparison.OrdinalIgnoreCase) ? "TWSE" : "TPEx";

    private enum QuoteFetchStatus
    {
        Success,
        NoData,
        UpstreamError
    }

    private sealed record QuoteFetchResult(QuoteFetchStatus Status, StockQuoteResponse? Quote)
    {
        public static QuoteFetchResult Success(StockQuoteResponse quote)
            => new(QuoteFetchStatus.Success, quote);

        public static QuoteFetchResult NoData()
            => new(QuoteFetchStatus.NoData, null);

        public static QuoteFetchResult UpstreamError()
            => new(QuoteFetchStatus.UpstreamError, null);
    }
}
