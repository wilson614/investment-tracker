using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.MarketData;

/// <summary>
/// 從 Sina Finance 取得 ETF 即時價格的服務。
/// 支援英國掛牌 ETF（LSE），且多數以 USD 計價。
/// </summary>
public class SinaEtfPriceService(HttpClient httpClient, ILogger<SinaEtfPriceService> logger) : ISinaEtfPriceService
{
    private const string BaseUrl = "https://hq.sinajs.cn/list=";
    private const string Referer = "http://vip.stock.finance.sina.com.cn";

    // 英國掛牌 ETF 的 Sina symbol 對應（多為 USD 計價）
    private static readonly Dictionary<string, string> SinaSymbols = new()
    {
        ["All Country"] = "lse_vwra",             // Vanguard FTSE All-World UCITS ETF (Acc)
        ["US Large"] = "lse_vuaa",                // Vanguard S&P 500 UCITS ETF (Acc)
        ["US Small"] = "lse_xrsu",                // Xtrackers Russell 2000 UCITS ETF (Acc)
        ["Emerging Markets"] = "lse_vfem",        // Vanguard FTSE Emerging Markets UCITS ETF (Acc)
        ["Europe"] = "lse_veua",                  // Vanguard FTSE Developed Europe UCITS ETF (Acc)
        ["Japan"] = "lse_vjpa",                   // Vanguard FTSE Japan UCITS ETF (Acc)
        ["Developed Markets Large"] = "lse_vhve",  // Vanguard FTSE Developed World UCITS ETF (Acc)
        ["Developed Markets Small"] = "lse_wsml",  // iShares MSCI World Small Cap UCITS ETF (Acc)
        ["Dev ex US Large"] = "lse_exus",         // Vanguard FTSE Developed ex US UCITS ETF (Acc)
        ["China"] = "lse_hcha",                   // HSBC MSCI China UCITS ETF (Acc)
    };

    static SinaEtfPriceService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static IReadOnlyCollection<string> SupportedMarkets => SinaSymbols.Keys;

    public async Task<decimal?> GetCurrentPriceAsync(string marketKey, CancellationToken cancellationToken = default)
    {
        if (!SinaSymbols.TryGetValue(marketKey, out var symbol))
        {
            logger.LogDebug("No Sina symbol mapping for {Market}", marketKey);
            return null;
        }

        try
        {
            var url = $"{BaseUrl}{symbol}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Referer", Referer);

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Sina returned {Status} for {Symbol}", response.StatusCode, symbol);
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var content = Encoding.GetEncoding("GBK").GetString(bytes);

            return ParseSinaResponse(content, marketKey);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching ETF price from Sina for {Market}", marketKey);
            return null;
        }
    }

    /// <summary>
    /// 解析 Sina 對 LSE ETF 的回應。
    /// 格式：var hq_str_lse_vwra="vwra,172.02,172.28,172.14,171.94,172.96,74599,...";
    /// 欄位：symbol, price, high, open, low, previous_close, volume, ...
    /// </summary>
    private decimal? ParseSinaResponse(string content, string marketKey)
    {
        // 取出引號內的資料
        var startIdx = content.IndexOf('"');
        var endIdx = content.LastIndexOf('"');

        if (startIdx < 0 || endIdx <= startIdx)
        {
            logger.LogWarning("Invalid Sina response format for {Market}", marketKey);
            return null;
        }

        var data = content.Substring(startIdx + 1, endIdx - startIdx - 1);
        if (string.IsNullOrEmpty(data))
        {
            logger.LogWarning("Empty data from Sina for {Market}", marketKey);
            return null;
        }

        var parts = data.Split(',');
        if (parts.Length < 2)
        {
            logger.LogWarning("Insufficient data fields from Sina for {Market}", marketKey);
            return null;
        }

        // 欄位 1：目前價格；欄位 5：昨收
        if (decimal.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out var price))
        {
            // 若目前價格為 0，回退使用昨收
            if (price <= 0 && parts.Length > 5 &&
                decimal.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out var yesterdayClose) &&
                yesterdayClose > 0)
            {
                logger.LogInformation("Using yesterday's close {YesterdayClose} as fallback for {Market} (current price is 0)", yesterdayClose, marketKey);
                return yesterdayClose;
            }

            if (price > 0)
            {
                logger.LogDebug("Got ETF price {Price} USD for {Market} from Sina", price, marketKey);
                return price;
            }
        }

        logger.LogWarning("Could not parse price from Sina for {Market}: {Data}", marketKey, parts[1]);
        return null;
    }
}

public interface ISinaEtfPriceService
{
    Task<decimal?> GetCurrentPriceAsync(string marketKey, CancellationToken cancellationToken = default);
}
