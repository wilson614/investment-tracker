using System.Text;
using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.StockPrices;

/// <summary>
/// 使用 Sina Finance API 的股價提供者（支援美股與英股）。
/// </summary>
public class SinaStockPriceProvider(HttpClient httpClient, ILogger<SinaStockPriceProvider> logger) : IStockPriceProvider
{
    private const string BaseUrl = "http://hq.sinajs.cn/list=";
    private const string Referer = "http://vip.stock.finance.sina.com.cn";

    static SinaStockPriceProvider()
    {
        // 註冊 GBK encoding 支援
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool SupportsMarket(StockMarket market) => market is StockMarket.US or StockMarket.UK;

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

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var gbkEncoding = Encoding.GetEncoding("GBK");
            var content = gbkEncoding.GetString(bytes);

            return ParseSinaResponse(content, market, symbol);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch stock price from Sina for {Market}:{Symbol}", market, symbol);
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
        // 回應格式：var hq_str_gb_aapl="Apple Inc,195.89,+1.23,..."
        // 或空值：var hq_str_gb_xxx="";

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Empty response from Sina for {Symbol}", symbol);
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
            logger.LogWarning("No data found for symbol {Symbol}", symbol);
            return null;
        }

        var fields = data.Split(',');

        // 記錄實際欄位內容以利除錯
        logger.LogDebug("Sina API response for {Symbol}: field count={Count}, fields={Fields}",
            symbol, fields.Length, string.Join("|", fields.Take(15)));

        if (fields.Length < 5)
        {
            logger.LogWarning("Insufficient fields in Sina response for {Symbol}: {FieldCount}", symbol, fields.Length);
            return null;
        }

        var name = fields[0];
        decimal.TryParse(fields[1], out var price);

        decimal? change = null;
        string? changePercent = null;

        // 英股（lse_）欄位格式與美股（gb_）不同
        // UK：0=name, 1=price, 2=high, 3=low, 4=open, 5=yesterday close, ...
        // US：0=name, 1=price, 2=change%, 3=timestamp, 4=change, ...
        if (market == StockMarket.UK)
        {
            // UK：嘗試讀取昨收，用於回退與漲跌計算
            decimal yesterdayClose = 0;
            if (fields.Length > 5)
            {
                decimal.TryParse(fields[5], out yesterdayClose);
            }

            switch (price)
            {
                // 若目前價格為 0，回退使用昨收
                case <= 0 when yesterdayClose > 0:
                    logger.LogInformation("Using yesterday's close {YesterdayClose} as fallback for {Symbol} (current price is 0)", yesterdayClose, symbol);
                    price = yesterdayClose;
                    // 使用回退價格時，沒有可用的漲跌資訊
                    break;
                case > 0 when yesterdayClose > 0:
                {
                    // 依昨收計算漲跌
                    change = price - yesterdayClose;
                    var pctValue = change.Value / yesterdayClose * 100;
                    var sign = pctValue >= 0 ? "+" : "";
                    changePercent = $"{sign}{pctValue:F2}%";
                    break;
                }
            }
        }
        else
        {
            // US：fields[4] 為當日漲跌值
            if (fields.Length > 4 && decimal.TryParse(fields[4], out var changeValue))
            {
                change = changeValue;
            }

            // US：fields[2] 為當日漲跌幅（例如 "-1.15" 代表 -1.15%）
            if (fields.Length > 2 && !string.IsNullOrEmpty(fields[2]))
            {
                if (decimal.TryParse(fields[2], out var percentValue))
                {
                    var sign = percentValue >= 0 ? "+" : "";
                    changePercent = $"{sign}{percentValue:F2}%";
                }
            }
        }

        // 最終驗證：價格必須為正數
        if (price <= 0)
        {
            logger.LogWarning("Invalid price in Sina response for {Symbol}: {Price}", symbol, fields[1]);
            return null;
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
