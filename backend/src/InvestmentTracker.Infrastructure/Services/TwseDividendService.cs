using System.Text.Json;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 股利資料 DTO。
/// </summary>
public record DividendRecord(
    DateTime ExDividendDate,
    string StockNo,
    string StockName,
    decimal DividendAmount,
    string DividendType // "息"：現金股利
);

/// <summary>
/// 從 TWSE 取得除權息資料的服務。
/// 用於在計算 YTD 時納入配息調整。
/// </summary>
public interface ITwseDividendService
{
    /// <summary>
    /// 取得指定年度的配息資料。
    /// </summary>
    Task<List<DividendRecord>> GetDividendsAsync(
        string stockNo,
        int year,
        CancellationToken cancellationToken = default);
}

public class TwseDividendService(
    HttpClient httpClient,
    ITwseRateLimiter rateLimiter,
    ILogger<TwseDividendService> logger) : ITwseDividendService
{
    public async Task<List<DividendRecord>> GetDividendsAsync(
        string stockNo,
        int year,
        CancellationToken cancellationToken = default)
    {
        var results = new List<DividendRecord>();

        try
        {
            await rateLimiter.WaitForSlotAsync(cancellationToken);

            // TWSE 使用民國年：西元年需減 1911
            var startDate = $"{year}0101";
            var endDate = $"{year}1231";

            // TWT49U：歷史除權息計算結果
            var url = $"https://www.twse.com.tw/rwd/zh/exRight/TWT49U?startDate={startDate}&endDate={endDate}&stockNo={stockNo}&response=json";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TWSE dividend API returned {Status}", response.StatusCode);
                return results;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = JsonDocument.Parse(content);

            if (!json.RootElement.TryGetProperty("data", out var dataArray))
            {
                logger.LogDebug("No dividend data for {StockNo} in {Year}", stockNo, year);
                return results;
            }

            // 欄位：
            // [0]=日期, [1]=股票代號, [2]=名稱, [3]=除息前收盤價, [4]=除息參考價, [5]=權值+息值, [6]=權/息
            foreach (var row in dataArray.EnumerateArray())
            {
                var rowStockNo = row[1].GetString();
                if (rowStockNo != stockNo)
                    continue;

                var dividendType = row[6].GetString();
                // 僅處理現金股利（息），略過股票股利（權）
                if (dividendType != "息")
                    continue;

                var dateStr = row[0].GetString(); // 例如："114年01月17日"
                var stockName = row[2].GetString() ?? stockNo;
                var dividendStr = row[5].GetString();

                if (!TryParseRocDate(dateStr, out var exDate))
                {
                    logger.LogWarning("Failed to parse date: {Date}", dateStr);
                    continue;
                }

                if (!decimal.TryParse(dividendStr, out var dividendAmount))
                {
                    logger.LogWarning("Failed to parse dividend amount: {Amount}", dividendStr);
                    continue;
                }

                results.Add(new DividendRecord(
                    exDate,
                    stockNo,
                    stockName,
                    dividendAmount,
                    dividendType
                ));

                logger.LogDebug("Found dividend for {StockNo}: {Date} = {Amount}",
                    stockNo, exDate.ToString("yyyy-MM-dd"), dividendAmount);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching dividends for {StockNo} in {Year}", stockNo, year);
        }

        return results;
    }

    /// <summary>
    /// 將民國日期格式（例如："114年01月17日"）解析為 DateTime。
    /// </summary>
    private static bool TryParseRocDate(string? dateStr, out DateTime result)
    {
        result = default;
        if (string.IsNullOrEmpty(dateStr))
            return false;

        try
        {
            // 從 "114年01月17日" 拆出年／月／日
            var yearEnd = dateStr.IndexOf('年');
            var monthEnd = dateStr.IndexOf('月');
            var dayEnd = dateStr.IndexOf('日');

            if (yearEnd < 0 || monthEnd < 0 || dayEnd < 0)
                return false;

            var rocYear = int.Parse(dateStr[..yearEnd]);
            var month = int.Parse(dateStr[(yearEnd + 1)..monthEnd]);
            var day = int.Parse(dateStr[(monthEnd + 1)..dayEnd]);

            var year = rocYear + 1911; // 民國年轉西元年
            result = new DateTime(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
