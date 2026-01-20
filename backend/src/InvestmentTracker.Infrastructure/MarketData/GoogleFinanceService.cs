using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 Google Finance 取得指數即時價格的服務。
/// 透過抓取 Google Finance 網站來取得即時價格。
/// </summary>
public partial class GoogleFinanceService(
    HttpClient httpClient,
    ILogger<GoogleFinanceService> logger) : IGoogleFinanceService
{
    // Google Finance symbol 格式：SYMBOL:EXCHANGE
    private static readonly Dictionary<string, string> GoogleFinanceSymbols = new()
    {
        ["All Country"] = "GEISAC:INDEXFTSE",    // FTSE Global All Cap Index
        ["US Large"] = ".INX:INDEXSP",            // S&P 500
        ["Taiwan"] = "TWII:TPE",                  // 台灣加權指數
    };

    public async Task<decimal?> GetCurrentPriceAsync(string marketKey, CancellationToken cancellationToken = default)
    {
        if (!GoogleFinanceSymbols.TryGetValue(marketKey, out var symbol))
        {
            logger.LogDebug("No Google Finance symbol mapping for {Market}", marketKey);
            return null;
        }

        try
        {
            var url = $"https://www.google.com/finance/quote/{symbol}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Google Finance returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            // Extract price from data-last-price attribute
            var match = DataLastPriceRegex().Match(html);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var price))
            {
                logger.LogDebug("Got price {Price} for {Market} from Google Finance", price, marketKey);
                return price;
            }

            logger.LogWarning("Could not parse price from Google Finance for {Symbol}", symbol);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching price from Google Finance for {Market}", marketKey);
            return null;
        }
    }

    public static IReadOnlyCollection<string> SupportedMarkets => GoogleFinanceSymbols.Keys;

    [GeneratedRegex(@"data-last-price=""([0-9.]+)""")]
    private static partial Regex DataLastPriceRegex();
}
