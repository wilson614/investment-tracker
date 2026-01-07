using System.Text;
using InvestmentTracker.Application.DTOs;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// Stock price provider using Sina Finance API (supports US and UK markets)
/// </summary>
public class SinaStockPriceProvider : IStockPriceProvider
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SinaStockPriceProvider> _logger;
    private const string BaseUrl = "http://hq.sinajs.cn/list=";
    private const string Referer = "http://vip.stock.finance.sina.com.cn";

    static SinaStockPriceProvider()
    {
        // Register GBK encoding support
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SinaStockPriceProvider(HttpClient httpClient, ILogger<SinaStockPriceProvider> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public bool SupportsMarket(StockMarket market)
    {
        return market == StockMarket.US || market == StockMarket.UK;
    }

    public async Task<StockQuoteResponse?> GetQuoteAsync(StockMarket market, string symbol, CancellationToken cancellationToken = default)
    {
        if (!SupportsMarket(market))
        {
            throw new ArgumentException($"Market {market} is not supported by SinaStockPriceProvider");
        }

        var sinaSymbol = GetSinaSymbol(market, symbol);
        var url = $"{BaseUrl}{sinaSymbol}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", Referer);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var gbkEncoding = Encoding.GetEncoding("GBK");
            var content = gbkEncoding.GetString(bytes);

            return ParseSinaResponse(content, market, symbol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch stock price from Sina for {Market}:{Symbol}", market, symbol);
            return null;
        }
    }

    private static string GetSinaSymbol(StockMarket market, string symbol)
    {
        var normalizedSymbol = symbol.ToLowerInvariant().Trim();
        return market switch
        {
            StockMarket.US => $"gb_{normalizedSymbol}",
            StockMarket.UK => $"lse_{normalizedSymbol}",
            _ => throw new ArgumentException($"Unsupported market: {market}")
        };
    }

    private StockQuoteResponse? ParseSinaResponse(string content, StockMarket market, string symbol)
    {
        // Response format: var hq_str_gb_aapl="Apple Inc,195.89,+1.23,..."
        // or empty: var hq_str_gb_xxx="";

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Empty response from Sina for {Symbol}", symbol);
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
            _logger.LogWarning("No data found for symbol {Symbol}", symbol);
            return null;
        }

        var fields = data.Split(',');

        // US stocks format (based on Sina API):
        // 0: Name, 1: Current Price, 2: Change %, 3: Timestamp, 4: Change, 5: Open, 6: High, 7: Low, ...
        if (fields.Length < 5)
        {
            _logger.LogWarning("Insufficient fields in Sina response for {Symbol}: {FieldCount}", symbol, fields.Length);
            return null;
        }

        var name = fields[0];
        if (!decimal.TryParse(fields[1], out var price) || price <= 0)
        {
            _logger.LogWarning("Invalid price in Sina response for {Symbol}: {Price}", symbol, fields[1]);
            return null;
        }

        decimal? change = null;
        string? changePercent = null;

        if (fields.Length > 4 && decimal.TryParse(fields[4], out var changeValue))
        {
            change = changeValue;
        }

        if (fields.Length > 2 && !string.IsNullOrEmpty(fields[2]))
        {
            changePercent = fields[2].Contains('%') ? fields[2] : $"{fields[2]}%";
        }

        return new StockQuoteResponse
        {
            Symbol = symbol.ToUpperInvariant(),
            Name = name,
            Price = price,
            Change = change,
            ChangePercent = changePercent,
            Market = market,
            Source = "Sina Finance",
            FetchedAt = DateTime.UtcNow
        };
    }
}
