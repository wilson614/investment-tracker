using System.Text.Json;
using InvestmentTracker.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// Stock price provider using TWSE (Taiwan Stock Exchange) API
/// Includes rate limiting to avoid being blocked (3 requests per 5 seconds)
/// </summary>
public class TwseStockPriceProvider : IStockPriceProvider
{
    private readonly HttpClient _httpClient;
    private readonly ITwseRateLimiter _rateLimiter;
    private readonly ILogger<TwseStockPriceProvider> _logger;
    private const string BaseUrl = "https://mis.twse.com.tw/stock/api/getStockInfo.jsp";

    public TwseStockPriceProvider(
        HttpClient httpClient,
        ITwseRateLimiter rateLimiter,
        ILogger<TwseStockPriceProvider> logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public bool SupportsMarket(StockMarket market)
    {
        return market == StockMarket.TW;
    }

    public async Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default)
    {
        if (!SupportsMarket(market))
        {
            throw new ArgumentException($"Market {market} is not supported by TwseStockPriceProvider");
        }

        var normalizedSymbol = symbol.Trim();

        // Try TSE (上市) first, then OTC (上櫃)
        var quote = await TryFetchQuoteAsync(normalizedSymbol, "tse", cancellationToken);
        if (quote == null)
        {
            quote = await TryFetchQuoteAsync(normalizedSymbol, "otc", cancellationToken);
        }

        return quote;
    }

    private async Task<StockQuoteResponse?> TryFetchQuoteAsync(string symbol, string exchange, CancellationToken cancellationToken)
    {
        var exCh = $"{exchange}_{symbol}.tw";
        var url = $"{BaseUrl}?ex_ch={exCh}&json=1&delay=0";

        try
        {
            // Wait for rate limit slot before making request
            await _rateLimiter.WaitForSlotAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseTwseResponse(content, symbol, exchange);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch stock price from TWSE for {Exchange}:{Symbol}", exchange, symbol);
            return null;
        }
    }

    private StockQuoteResponse? ParseTwseResponse(string content, string symbol, string exchange)
    {
        try
        {
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            if (!root.TryGetProperty("msgArray", out var msgArray) || msgArray.GetArrayLength() == 0)
            {
                _logger.LogDebug("No data found for {Exchange}:{Symbol}", exchange, symbol);
                return null;
            }

            var stock = msgArray[0];

            // Parse fields
            // z: Latest trade price, n: Name, y: Yesterday's close
            // o: Open, h: High, l: Low, v: Volume

            var priceStr = stock.TryGetProperty("z", out var zProp) ? zProp.GetString() : null;
            var name = stock.TryGetProperty("n", out var nProp) ? nProp.GetString() : symbol;
            var yesterdayStr = stock.TryGetProperty("y", out var yProp) ? yProp.GetString() : null;

            // z might be "-" if no trade yet
            if (string.IsNullOrEmpty(priceStr) || priceStr == "-")
            {
                _logger.LogDebug("No trade price available for {Symbol}", symbol);
                return null;
            }

            if (!decimal.TryParse(priceStr, out var price))
            {
                _logger.LogWarning("Invalid price format for {Symbol}: {Price}", symbol, priceStr);
                return null;
            }

            decimal? change = null;
            string? changePercent = null;

            if (!string.IsNullOrEmpty(yesterdayStr) && decimal.TryParse(yesterdayStr, out var yesterday) && yesterday > 0)
            {
                change = price - yesterday;
                var pctValue = (change.Value / yesterday) * 100;
                var sign = pctValue >= 0 ? "+" : "";
                changePercent = $"{sign}{pctValue:F2}%";
            }

            return new StockQuoteResponse
            {
                Symbol = symbol.ToUpperInvariant(),
                Name = name ?? symbol,
                Price = price,
                Change = change,
                ChangePercent = changePercent,
                Market = StockMarket.TW,
                Source = exchange == "tse" ? "TWSE" : "TPEx",
                FetchedAt = DateTime.UtcNow
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse TWSE response for {Symbol}", symbol);
            return null;
        }
    }
}
