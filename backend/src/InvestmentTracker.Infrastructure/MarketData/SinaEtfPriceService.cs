using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// Service for fetching real-time ETF prices from Sina Finance
/// Supports UK-listed ETFs (LSE) with USD denomination
/// </summary>
public class SinaEtfPriceService : ISinaEtfPriceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<SinaEtfPriceService> _logger;

    private const string BaseUrl = "https://hq.sinajs.cn/list=";
    private const string Referer = "http://vip.stock.finance.sina.com.cn";

    // Sina symbol mappings for UK-listed ETFs (USD denominated)
    private static readonly Dictionary<string, string> SinaSymbols = new()
    {
        ["All Country"] = "lse_vwra",  // Vanguard FTSE All-World UCITS ETF (Acc)
        ["US Large"] = "lse_vuaa",      // Vanguard S&P 500 UCITS ETF (Acc)
    };

    static SinaEtfPriceService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SinaEtfPriceService(HttpClient httpClient, ILogger<SinaEtfPriceService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public static IReadOnlyCollection<string> SupportedMarkets => SinaSymbols.Keys;

    public async Task<decimal?> GetCurrentPriceAsync(string marketKey, CancellationToken cancellationToken = default)
    {
        if (!SinaSymbols.TryGetValue(marketKey, out var symbol))
        {
            _logger.LogDebug("No Sina symbol mapping for {Market}", marketKey);
            return null;
        }

        try
        {
            var url = $"{BaseUrl}{symbol}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", Referer);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sina returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var content = Encoding.GetEncoding("GBK").GetString(bytes);

            return ParseSinaResponse(content, marketKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching ETF price from Sina for {Market}", marketKey);
            return null;
        }
    }

    /// <summary>
    /// Parse Sina response for LSE ETFs
    /// Format: var hq_str_lse_vwra="vwra,172.02,172.28,172.14,171.94,172.96,74599,...";
    /// Fields: symbol, price, high, open, low, previous_close, volume, ...
    /// </summary>
    private decimal? ParseSinaResponse(string content, string marketKey)
    {
        // Extract the quoted data
        var startIdx = content.IndexOf('"');
        var endIdx = content.LastIndexOf('"');

        if (startIdx < 0 || endIdx <= startIdx)
        {
            _logger.LogWarning("Invalid Sina response format for {Market}", marketKey);
            return null;
        }

        var data = content.Substring(startIdx + 1, endIdx - startIdx - 1);
        if (string.IsNullOrEmpty(data))
        {
            _logger.LogWarning("Empty data from Sina for {Market}", marketKey);
            return null;
        }

        var parts = data.Split(',');
        if (parts.Length < 2)
        {
            _logger.LogWarning("Insufficient data fields from Sina for {Market}", marketKey);
            return null;
        }

        // Field 1 is the current price
        if (decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
        {
            _logger.LogDebug("Got ETF price {Price} USD for {Market} from Sina", price, marketKey);
            return price;
        }

        _logger.LogWarning("Could not parse price from Sina for {Market}: {Data}", marketKey, parts[1]);
        return null;
    }
}

public interface ISinaEtfPriceService
{
    Task<decimal?> GetCurrentPriceAsync(string marketKey, CancellationToken cancellationToken = default);
}
