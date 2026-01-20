using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.Services;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.StockPrices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InvestmentTracker.Infrastructure.Services;

/// <summary>
/// 計算市場 YTD 基準報酬率的服務。
/// 使用 IndexPriceSnapshot（YearMonth=YYYYMM 的月末快照）
/// 以前一年度 12 月月末價格作為 YTD 基準
/// 若缺少基準價，會視需要從 Stooq/TWSE 自動補抓
/// </summary>
public class MarketYtdService(
    AppDbContext dbContext,
    IStockPriceService stockPriceService,
    IStooqHistoricalPriceService stooqService,
    ITwseDividendService dividendService,
    EtfClassificationService etfClassificationService,
    ITwseRateLimiter twseRateLimiter,
    HttpClient httpClient,
    ILogger<MarketYtdService> logger) : IMarketYtdService
{
    // 基準定義：MarketKey -> (Symbol, Name, Market)
    // 注意：「Taiwan 0050」與「Taiwan」（用於 CAPE 的 TWII 指數）不同
    private static readonly Dictionary<string, (string Symbol, string Name, StockMarket Market)> Benchmarks = new()
    {
        ["All Country"] = ("VWRA", "Vanguard FTSE All-World", StockMarket.UK),
        ["US Large"] = ("VUAA", "Vanguard S&P 500", StockMarket.UK),
        ["US Small"] = ("XRSU", "Xtrackers Russell 2000", StockMarket.UK),
        ["Developed Markets Large"] = ("VHVE", "Vanguard FTSE Developed World", StockMarket.UK),
        ["Developed Markets Small"] = ("WSML", "iShares MSCI World Small Cap", StockMarket.UK),
        ["Dev ex US Large"] = ("EXUS", "Vanguard FTSE Developed ex US", StockMarket.UK),
        ["Emerging Markets"] = ("VFEM", "Vanguard FTSE Emerging Markets", StockMarket.UK),
        ["Europe"] = ("VEUA", "Vanguard FTSE Developed Europe", StockMarket.UK),
        ["Japan"] = ("VJPA", "Vanguard FTSE Japan", StockMarket.UK),
        ["China"] = ("HCHA", "HSBC MSCI China UCITS", StockMarket.UK),
        ["Taiwan 0050"] = ("0050", "元大台灣50", StockMarket.TW),
    };

    public static IReadOnlyDictionary<string, (string Symbol, string Name, StockMarket Market)> SupportedBenchmarks => Benchmarks;

    public async Task<MarketYtdComparisonDto> GetYtdComparisonAsync(CancellationToken cancellationToken = default)
    {
        var year = DateTime.UtcNow.Year;
        var previousYear = year - 1;
        var yearEndYearMonth = $"{previousYear}12";  // Use previous year December as baseline

        // 從資料庫載入年末基準價（例如：2026 的 YTD 會用 202512）
        // 使用 GroupBy 處理可能的重複 key（並發寫入導致的 race condition）
        // 排除 NotAvailable，並僅取有實際價格的快照
        var yearEndPrices = (await dbContext.IndexPriceSnapshots
            .Where(s => s.YearMonth == yearEndYearMonth && Benchmarks.Keys.Contains(s.MarketKey) && !s.IsNotAvailable && s.Price.HasValue)
            .ToListAsync(cancellationToken))
            .GroupBy(s => s.MarketKey)
            .ToDictionary(g => g.Key, g => g.First().Price!.Value);

        // 自動補抓上一年度 12 月的缺漏基準價
        var missingMarkets = Benchmarks.Keys.Where(k => !yearEndPrices.ContainsKey(k)).ToList();
        if (missingMarkets.Count > 0)
        {
            logger.LogInformation("Missing {Year}/12 year-end prices for {Markets}, fetching from external sources...", previousYear, string.Join(", ", missingMarkets));
            await PopulateMissingYearEndPricesAsync(previousYear, missingMarkets, yearEndPrices, cancellationToken);
        }

        // 抓取所有基準的最新價格
        var benchmarkResults = new List<MarketYtdReturnDto>();

        foreach (var (marketKey, benchmark) in Benchmarks)
        {
            var result = await GetBenchmarkYtdAsync(marketKey, benchmark, yearEndPrices, cancellationToken);
            benchmarkResults.Add(result);
        }

        return new MarketYtdComparisonDto
        {
            Year = year,
            Benchmarks = benchmarkResults,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<MarketYtdComparisonDto> RefreshYtdComparisonAsync(CancellationToken cancellationToken = default)
    {
        // 直接委派給 GetYtdComparisonAsync（內含自動補抓邏輯）
        return await GetYtdComparisonAsync(cancellationToken);
    }

    private async Task<MarketYtdReturnDto> GetBenchmarkYtdAsync(
        string marketKey,
        (string Symbol, string Name, StockMarket Market) benchmark,
        Dictionary<string, decimal> yearEndPrices,
        CancellationToken cancellationToken)
    {
        var hasYearEndPrice = yearEndPrices.TryGetValue(marketKey, out var yearEndPrice);
        var year = DateTime.UtcNow.Year;

        try
        {
            var quote = await stockPriceService.GetQuoteAsync(benchmark.Market, benchmark.Symbol, cancellationToken);

            if (quote == null)
            {
                return new MarketYtdReturnDto
                {
                    MarketKey = marketKey,
                    Symbol = benchmark.Symbol,
                    Name = benchmark.Name,
                    Jan1Price = hasYearEndPrice ? yearEndPrice : null,
                    CurrentPrice = null,
                    YtdReturnPercent = null,
                    Error = "Unable to fetch current price"
                };
            }

            // 配息型 ETF 的股利調整（目前僅支援台股）
            decimal dividendsPaid = 0;
            var needsDividendAdjustment = benchmark.Market == StockMarket.TW &&
                etfClassificationService.NeedsDividendAdjustment(benchmark.Symbol);
            if (needsDividendAdjustment)
            {
                var dividends = await dividendService.GetDividendsAsync(benchmark.Symbol, year, cancellationToken);
                // Only count dividends that have already been paid (ex-date <= today)
                var today = DateTime.UtcNow.Date;
                dividendsPaid = dividends
                    .Where(d => d.ExDividendDate.Date <= today)
                    .Sum(d => d.DividendAmount);

                if (dividendsPaid > 0)
                {
                    logger.LogDebug("Found {Count} dividends for {Symbol} in {Year}, total: {Amount}",
                        dividends.Count, benchmark.Symbol, year, dividendsPaid);
                }
            }

            decimal? ytdPriceReturn = null;
            decimal? ytdTotalReturn = null;

            if (hasYearEndPrice && yearEndPrice > 0)
            {
                // Price return = ((Current - YearEnd) / YearEnd) * 100
                ytdPriceReturn = ((quote.Price - yearEndPrice) / yearEndPrice) * 100;

                // Total return = ((Current + Dividends - YearEnd) / YearEnd) * 100
                ytdTotalReturn = ((quote.Price + dividendsPaid - yearEndPrice) / yearEndPrice) * 100;
            }

            return new MarketYtdReturnDto
            {
                MarketKey = marketKey,
                Symbol = benchmark.Symbol,
                Name = benchmark.Name,
                Jan1Price = hasYearEndPrice ? yearEndPrice : null,
                CurrentPrice = quote.Price,
                DividendsPaid = dividendsPaid > 0 ? dividendsPaid : null,
                YtdReturnPercent = ytdPriceReturn,
                YtdTotalReturnPercent = ytdTotalReturn,
                FetchedAt = quote.FetchedAt,
                Error = hasYearEndPrice ? null : "Missing prior-year December reference price"
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to get YTD for {MarketKey} ({Symbol})", marketKey, benchmark.Symbol);
            return new MarketYtdReturnDto
            {
                MarketKey = marketKey,
                Symbol = benchmark.Symbol,
                Name = benchmark.Name,
                Jan1Price = hasYearEndPrice ? yearEndPrice : null,
                Error = $"Error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// 補抓缺漏的年末基準價：
    /// - 英國掛牌 ETF：Stooq
    /// - 台灣 0050：TWSE
    /// 並寫入資料庫，供後續使用。
    /// </summary>
    private async Task PopulateMissingYearEndPricesAsync(
        int year,
        List<string> missingMarkets,
        Dictionary<string, decimal> yearEndPrices,
        CancellationToken cancellationToken)
    {
        var yearMonth = $"{year}12";  // 以 YYYYMM 格式存放（例如：202512）

        foreach (var marketKey in missingMarkets)
        {
            try
            {
                decimal? yearEndPrice = null;

                if (marketKey == "Taiwan 0050")
                {
                    // 從 TWSE 抓取 0050 年末價格
                    yearEndPrice = await FetchTwse0050YearEndPriceAsync(year, cancellationToken);
                }
                else
                {
                    // 從 Stooq 抓取英國掛牌 ETF 的年末價格
                    yearEndPrice = await stooqService.GetMonthEndPriceAsync(marketKey, year, 12, cancellationToken);
                }

                if (yearEndPrice.HasValue)
                {
                    // 確認是否已存在（避免並發請求造成重複寫入）
                    var exists = await dbContext.IndexPriceSnapshots
                        .AnyAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth, cancellationToken);

                    if (!exists)
                    {
                        dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                        {
                            MarketKey = marketKey,
                            YearMonth = yearMonth,
                            Price = yearEndPrice.Value,
                            RecordedAt = DateTime.UtcNow
                        });
                        logger.LogInformation("Fetched and stored {Year}/12 year-end price for {MarketKey}: {Price}",
                            year, marketKey, yearEndPrice.Value);
                    }

                    yearEndPrices[marketKey] = yearEndPrice.Value;
                }
                else
                {
                    logger.LogWarning("Could not fetch year-end price for {MarketKey}", marketKey);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to fetch year-end price for {MarketKey}", marketKey);
            }
        }

        if (yearEndPrices.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// 從 TWSE 取得 0050 ETF 年末收盤價。
    /// </summary>
    private async Task<decimal?> FetchTwse0050YearEndPriceAsync(int year, CancellationToken cancellationToken)
    {
        try
        {
            // 發送請求前先等待 rate limit 的可用額度
            await twseRateLimiter.WaitForSlotAsync(cancellationToken);

            // TWSE 個股歷史資料 API
            var url = $"https://www.twse.com.tw/exchangeReport/STOCK_DAY?response=json&date={year}1201&stockNo=0050";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("TWSE returned {Status} for 0050 historical data", response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = System.Text.Json.JsonDocument.Parse(content);

            if (!json.RootElement.TryGetProperty("data", out var dataArray) ||
                dataArray.GetArrayLength() == 0)
            {
                logger.LogWarning("No 0050 historical data from TWSE for {Year}/12", year);
                return null;
            }

            // Get last trading day of December (last row)
            var lastRow = dataArray[dataArray.GetArrayLength() - 1];

            // Field index 6 is closing price (收盤價)
            var closeStr = lastRow[6].GetString();
            if (!string.IsNullOrEmpty(closeStr))
            {
                closeStr = closeStr.Replace(",", "");
                if (decimal.TryParse(closeStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var price))
                {
                    logger.LogDebug("Got 0050 year-end price {Price} for {Year}/12 from TWSE", price, year);
                    return price;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching 0050 year-end price from TWSE for {Year}", year);
            return null;
        }
    }
}
