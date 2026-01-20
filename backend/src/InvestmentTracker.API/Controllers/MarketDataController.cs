using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供市場資料（CAPE、YTD 指標、歷史價格/匯率、基準報酬等）相關 API。
/// </summary>
[ApiController]
[Route("api/market-data")]
[Authorize]
public class MarketDataController(
    ICapeDataService capeDataService,
    IMarketYtdService marketYtdService,
    EuronextQuoteService euronextQuoteService,
    IStooqHistoricalPriceService stooqService,
    ITwseStockHistoricalPriceService twseService,
    IHistoricalYearEndDataService historicalYearEndDataService,
    ITransactionDateExchangeRateService txDateFxService,
    AppDbContext dbContext,
    ILogger<MarketDataController> logger) : ControllerBase
{
    /// <summary>
    /// 取得 Research Affiliates 提供的 CAPE（Cyclically Adjusted P/E）資料。
    /// 資料會快取 24 小時。
    /// </summary>
    [HttpGet("cape")]
    [ProducesResponseType(typeof(CapeDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CapeDataResponse>> GetCapeData(CancellationToken cancellationToken)
    {
        var data = await capeDataService.GetCapeDataAsync(cancellationToken);

        if (data == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Unable to fetch CAPE data");
        }

        return Ok(data);
    }

    /// <summary>
    /// 強制更新 CAPE 資料（清除快取並重新抓取）。
    /// </summary>
    [HttpPost("cape/refresh")]
    [ProducesResponseType(typeof(CapeDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CapeDataResponse>> RefreshCapeData(CancellationToken cancellationToken)
    {
        var data = await capeDataService.RefreshCapeDataAsync(cancellationToken);

        if (data == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Unable to fetch CAPE data");
        }

        return Ok(data);
    }

    /// <summary>
    /// 取得用於 CAPE 調整的所有指數價格快照（IndexPriceSnapshot）。
    /// </summary>
    [HttpGet("index-prices")]
    [ProducesResponseType(typeof(List<IndexPriceSnapshot>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<IndexPriceSnapshot>>> GetIndexPrices(CancellationToken cancellationToken)
    {
        var snapshots = await dbContext.IndexPriceSnapshots
            .OrderByDescending(s => s.YearMonth)
            .ThenBy(s => s.MarketKey)
            .ToListAsync(cancellationToken);

        return Ok(snapshots);
    }

    /// <summary>
    /// 新增或更新用於 CAPE 調整的指數價格快照。
    /// </summary>
    [HttpPost("index-prices")]
    [ProducesResponseType(typeof(IndexPriceSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<IndexPriceSnapshot>> UpsertIndexPrice(
        [FromBody] IndexPriceRequest request,
        CancellationToken cancellationToken)
    {
        // 驗證 market key
        if (!IndexPriceService.SupportedMarkets.Contains(request.MarketKey))
        {
            return BadRequest($"Unsupported market: {request.MarketKey}. Supported markets: {string.Join(", ", IndexPriceService.SupportedMarkets)}");
        }

        // 驗證 year-month 格式
        if (request.YearMonth.Length != 6 || !int.TryParse(request.YearMonth, out _))
        {
            return BadRequest("YearMonth must be in YYYYMM format (e.g., 202512)");
        }

        var existing = await dbContext.IndexPriceSnapshots
            .FirstOrDefaultAsync(
                s => s.MarketKey == request.MarketKey && s.YearMonth == request.YearMonth,
                cancellationToken);

        if (existing != null)
        {
            existing.Price = request.Price;
            existing.RecordedAt = DateTime.UtcNow;
        }
        else
        {
            existing = new IndexPriceSnapshot
            {
                MarketKey = request.MarketKey,
                YearMonth = request.YearMonth,
                Price = request.Price,
                RecordedAt = DateTime.UtcNow
            };
            dbContext.IndexPriceSnapshots.Add(existing);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(existing);
    }

    /// <summary>
    /// 取得 CAPE 調整支援的市場清單。
    /// </summary>
    [HttpGet("supported-markets")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<string>> GetSupportedMarkets()
    {
        return Ok(IndexPriceService.SupportedMarkets);
    }

    /// <summary>
    /// 取得基準 ETF 的 YTD（Year-to-Date）報酬比較。
    /// 基準包含：VWRA（All Country）、VUAA（US Large）、0050（Taiwan）、VFEM（Emerging Markets）。
    /// </summary>
    [HttpGet("ytd-comparison")]
    [ProducesResponseType(typeof(MarketYtdComparisonDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketYtdComparisonDto>> GetYtdComparison(CancellationToken cancellationToken)
    {
        var data = await marketYtdService.GetYtdComparisonAsync(cancellationToken);
        return Ok(data);
    }

    /// <summary>
    /// 強制更新 YTD 比較資料。
    /// </summary>
    [HttpPost("ytd-comparison/refresh")]
    [ProducesResponseType(typeof(MarketYtdComparisonDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketYtdComparisonDto>> RefreshYtdComparison(CancellationToken cancellationToken)
    {
        var data = await marketYtdService.RefreshYtdComparisonAsync(cancellationToken);
        return Ok(data);
    }

    /// <summary>
    /// 取得 YTD 比較支援的基準清單。
    /// </summary>
    [HttpGet("ytd-benchmarks")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public ActionResult GetYtdBenchmarks()
    {
        var benchmarks = MarketYtdService.SupportedBenchmarks.Select(b => new
        {
            MarketKey = b.Key,
            b.Value.Symbol,
            b.Value.Name
        });
        return Ok(benchmarks);
    }

    /// <summary>
    /// 取得 Euronext 上市股票的報價（例如：阿姆斯特丹市場的 AGAC）。
    /// </summary>
    /// <param name="isin">ISIN code（例如：IE000FHBZDZ8）</param>
    /// <param name="mic">Market Identifier Code（例如：阿姆斯特丹為 XAMS）</param>
    /// <param name="homeCurrency">匯率換算目標幣別（預設：TWD）</param>
    /// <param name="refresh">是否強制更新（略過快取）</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("euronext/quote")]
    [ProducesResponseType(typeof(EuronextQuoteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EuronextQuoteResponse>> GetEuronextQuote(
        [FromQuery] string isin,
        [FromQuery] string mic,
        [FromQuery] string? homeCurrency = "TWD",
        [FromQuery] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(isin) || string.IsNullOrWhiteSpace(mic))
        {
            return BadRequest("ISIN and MIC are required");
        }

        var quote = await euronextQuoteService.GetQuoteAsync(
            isin.Trim().ToUpperInvariant(),
            mic.Trim().ToUpperInvariant(),
            homeCurrency ?? "TWD",
            refresh,
            cancellationToken);

        if (quote == null)
        {
            return NotFound($"No quote found for {isin}-{mic}");
        }

        return Ok(new EuronextQuoteResponse(
            quote.Price,
            quote.Currency,
            quote.MarketTime,
            quote.Name,
            quote.ExchangeRate,
            quote.FromCache,
            quote.ChangePercent,
            quote.Change));
    }

    /// <summary>
    /// 取得指定日期的單一股票歷史收盤價。
    /// US/UK 股票使用 Stooq API。
    /// </summary>
    /// <param name="ticker">股票代號（例如：AAPL、VWRA）</param>
    /// <param name="date">目標日期（格式：yyyy-MM-dd）</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("historical-price")]
    [ProducesResponseType(typeof(HistoricalPriceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HistoricalPriceResponse>> GetHistoricalPrice(
        [FromQuery] string ticker,
        [FromQuery] string date,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ticker))
        {
            return BadRequest("Ticker is required");
        }

        if (!DateOnly.TryParse(date, out var targetDate))
        {
            return BadRequest("Date must be in yyyy-MM-dd format");
        }

        var normalizedTicker = ticker.Trim().ToUpperInvariant();

        // 對於已結束的年度，使用 12/31 作為年終價格查詢日期。
        // 這些資料會以「全域快取」（跨使用者共享）方式保存，以降低重複呼叫 Stooq。
        if (targetDate is { Month: 12, Day: 31 } && targetDate.Year < DateTime.UtcNow.Year)
        {
            var cachedResult = await historicalYearEndDataService.GetOrFetchYearEndPriceAsync(
                normalizedTicker,
                targetDate.Year,
                cancellationToken);

            if (cachedResult == null)
            {
                return NotFound($"No historical price found for {ticker} on {date}");
            }

            return Ok(new HistoricalPriceResponse(
                cachedResult.Price,
                cachedResult.Currency,
                cachedResult.ActualDate.ToString("yyyy-MM-dd")));
        }

        var result = await stooqService.GetStockPriceAsync(
            normalizedTicker,
            targetDate,
            cancellationToken);

        if (result == null)
        {
            return NotFound($"No historical price found for {ticker} on {date}");
        }

        return Ok(new HistoricalPriceResponse(
            result.Price,
            result.Currency,
            result.ActualDate.ToString("yyyy-MM-dd")));
    }

    /// <summary>
    /// 取得指定日期的多檔股票歷史收盤價。
    /// US/UK 股票使用 Stooq API。
    /// </summary>
    [HttpPost("historical-prices")]
    [ProducesResponseType(typeof(Dictionary<string, HistoricalPriceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Dictionary<string, HistoricalPriceResponse>>> GetHistoricalPrices(
        [FromBody] HistoricalPricesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Tickers.Length == 0)
        {
            return BadRequest("At least one ticker is required");
        }

        if (!DateOnly.TryParse(request.Date, out var targetDate))
        {
            return BadRequest("Date must be in yyyy-MM-dd format");
        }

        Dictionary<string, HistoricalPriceResponse> results = [];

        var isCompletedYearEndLookup =
            targetDate is { Month: 12, Day: 31 } &&
            targetDate.Year < DateTime.UtcNow.Year;

        // Process tickers sequentially to avoid DbContext threading issues
        // DbContext is not thread-safe and cannot be used in parallel operations
        foreach (var ticker in request.Tickers)
        {
            var normalizedTicker = ticker.Trim().ToUpperInvariant();

            try
            {
                if (isCompletedYearEndLookup)
                {
                    var cachedResult = await historicalYearEndDataService.GetOrFetchYearEndPriceAsync(
                        normalizedTicker,
                        targetDate.Year,
                        cancellationToken);

                    if (cachedResult != null)
                    {
                        results[ticker] = new HistoricalPriceResponse(
                            cachedResult.Price,
                            cachedResult.Currency,
                            cachedResult.ActualDate.ToString("yyyy-MM-dd"));
                    }
                }
                else
                {
                    var result = await stooqService.GetStockPriceAsync(
                        normalizedTicker,
                        targetDate,
                        cancellationToken);

                    if (result != null)
                    {
                        results[ticker] = new HistoricalPriceResponse(
                            result.Price,
                            result.Currency,
                            result.ActualDate.ToString("yyyy-MM-dd"));
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch historical price for {Ticker}", ticker);
                // Continue with other tickers
            }
        }

        return Ok(results);
    }

    /// <summary>
    /// 取得指定日期的歷史匯率（幣別對）。
    /// 使用 Stooq 的 forex 資料。
    /// </summary>
    /// <param name="from">來源幣別（例如：USD）</param>
    /// <param name="to">目標幣別（例如：TWD）</param>
    /// <param name="date">目標日期（格式：yyyy-MM-dd）</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("historical-exchange-rate")]
    [ProducesResponseType(typeof(HistoricalExchangeRateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HistoricalExchangeRateResponse>> GetHistoricalExchangeRate(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] string date,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return BadRequest("From and To currencies are required");
        }

        if (!DateOnly.TryParse(date, out var targetDate))
        {
            return BadRequest("Date must be in yyyy-MM-dd format");
        }

        var normalizedFrom = from.Trim().ToUpperInvariant();
        var normalizedTo = to.Trim().ToUpperInvariant();

        // 對於已結束的年度，使用 12/31 作為年終匯率查詢日期。
        // 這些資料會以「全域快取」（跨使用者共享）方式保存，以降低重複呼叫 Stooq。
        if (targetDate is { Month: 12, Day: 31 } && targetDate.Year < DateTime.UtcNow.Year)
        {
            var cachedResult = await historicalYearEndDataService.GetOrFetchYearEndExchangeRateAsync(
                normalizedFrom,
                normalizedTo,
                targetDate.Year,
                cancellationToken);

            if (cachedResult == null)
            {
                return NotFound($"No exchange rate found for {from}/{to} on {date}");
            }

            return Ok(new HistoricalExchangeRateResponse(
                cachedResult.Rate,
                normalizedFrom,
                normalizedTo,
                cachedResult.ActualDate.ToString("yyyy-MM-dd")));
        }

        var result = await stooqService.GetExchangeRateAsync(
            normalizedFrom,
            normalizedTo,
            targetDate,
            cancellationToken);

        if (result == null)
        {
            return NotFound($"No exchange rate found for {from}/{to} on {date}");
        }

        return Ok(new HistoricalExchangeRateResponse(
            result.Rate,
            result.FromCurrency,
            result.ToCurrency,
            result.ActualDate.ToString("yyyy-MM-dd")));
    }

    /// <summary>
    /// 取得指定年度的基準報酬（Benchmark Returns）。
    /// 會使用已快取的 IndexPriceSnapshot 資料；若缺少則會嘗試自動抓取（Stooq/TWSE）。
    /// </summary>
    /// <param name="year">要計算報酬的年份（例如：2025）</param>
    /// <param name="cancellationToken"></param>
    [HttpGet("benchmark-returns")]
    [ProducesResponseType(typeof(BenchmarkReturnsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<BenchmarkReturnsResponse>> GetBenchmarkReturns(
        [FromQuery] int year,
        CancellationToken cancellationToken = default)
    {
        if (year < 2000 || year > DateTime.UtcNow.Year)
        {
            return BadRequest("Invalid year");
        }

        var startYearMonth = $"{year - 1}12";  // 前一年 12 月
        var endYearMonth = $"{year}12";        // 當年 12 月
        var benchmarks = MarketYtdService.SupportedBenchmarks;

        // 取得兩個 year-month 的所有快取指數價格
        // 排除 NotAvailable 的項目
        var snapshots = await dbContext.IndexPriceSnapshots
            .Where(s => (s.YearMonth == startYearMonth || s.YearMonth == endYearMonth) && !s.IsNotAvailable && s.Price.HasValue)
            .ToListAsync(cancellationToken);

        var startPrices = snapshots
            .Where(s => s.YearMonth == startYearMonth)
            .GroupBy(s => s.MarketKey)
            .ToDictionary(g => g.Key, g => g.First().Price!.Value);

        var endPrices = snapshots
            .Where(s => s.YearMonth == endYearMonth)
            .GroupBy(s => s.MarketKey)
            .ToDictionary(g => g.Key, g => g.First().Price!.Value);

        // 取得 NotAvailable 標記，用於略過抓取
        var notAvailableMarkers = await dbContext.IndexPriceSnapshots
            .Where(s => (s.YearMonth == startYearMonth || s.YearMonth == endYearMonth) && s.IsNotAvailable)
            .Select(s => new { s.MarketKey, s.YearMonth })
            .ToListAsync(cancellationToken);

        var notAvailableStart = notAvailableMarkers.Where(m => m.YearMonth == startYearMonth).Select(m => m.MarketKey).ToHashSet();
        var notAvailableEnd = notAvailableMarkers.Where(m => m.YearMonth == endYearMonth).Select(m => m.MarketKey).ToHashSet();

        // 延遲載入缺少的價格（外部 API）
        // 同時略過已存在 NotAvailable 標記的市場
        var missingStartMarkets = benchmarks.Keys
            .Where(k => !startPrices.ContainsKey(k) && !notAvailableStart.Contains(k))
            .ToList();
        var missingEndMarkets = benchmarks.Keys
            .Where(k => !endPrices.ContainsKey(k) && !notAvailableEnd.Contains(k))
            .ToList();

        // 抓取缺少的起始年價格（前一年 12 月）
        if (missingStartMarkets.Count > 0)
        {
            logger.LogInformation("Lazy-loading {Count} missing benchmark prices for {YearMonth}",
                missingStartMarkets.Count, startYearMonth);
            await FetchAndCacheBenchmarkPricesAsync(missingStartMarkets, year - 1, startPrices, cancellationToken);
        }

        // 抓取缺少的結束年價格（當年 12 月）——僅針對已結束的年度
        if (missingEndMarkets.Count > 0 && year < DateTime.UtcNow.Year)
        {
            logger.LogInformation("Lazy-loading {Count} missing benchmark prices for {YearMonth}",
                missingEndMarkets.Count, endYearMonth);
            await FetchAndCacheBenchmarkPricesAsync(missingEndMarkets, year, endPrices, cancellationToken);
        }

        // 取得台灣 0050 的股票分割資料，用於調整歷史價格
        var taiwanSplits = await dbContext.StockSplits
            .Where(s => s.Symbol == "0050" && s.Market == StockMarket.TW)
            .OrderBy(s => s.SplitDate)
            .ToListAsync(cancellationToken);

        Dictionary<string, decimal?> returns = [];

        foreach (var (marketKey, _) in benchmarks)
        {
            if (startPrices.TryGetValue(marketKey, out var startPrice) &&
                endPrices.TryGetValue(marketKey, out var endPrice) &&
                startPrice > 0)
            {
                // 台灣 0050：若年度中發生股票分割，需以分割比例調整起始價格
                if (marketKey == "Taiwan 0050" && taiwanSplits.Count != 0)
                {
                    // 計算 year-1/12/31 之後、year/12/31（含）之前的累積分割比例
                    var startDate = new DateTime(year - 1, 12, 31, 0, 0, 0, DateTimeKind.Utc);
                    var endDate = new DateTime(year, 12, 31, 0, 0, 0, DateTimeKind.Utc);

                    var splitsDuringYear = taiwanSplits
                        .Where(s => s.SplitDate > startDate && s.SplitDate <= endDate)
                        .ToList();

                    if (splitsDuringYear.Count != 0)
                    {
                        var cumulativeRatio = splitsDuringYear.Aggregate(1.0m, (acc, s) => acc * s.SplitRatio);
                        // 將起始價格調整到分割後的等值
                        startPrice = startPrice / cumulativeRatio;
                        logger.LogDebug("Adjusted Taiwan 0050 start price by split ratio {Ratio}: {Original} -> {Adjusted}",
                            cumulativeRatio, startPrices[marketKey], startPrice);
                    }
                }

                var returnPercent = (endPrice - startPrice) / startPrice * 100;
                returns[marketKey] = Math.Round(returnPercent, 2);
            }
            else
            {
                returns[marketKey] = null;
            }
        }

        return Ok(new BenchmarkReturnsResponse(
            year,
            returns,
            startPrices.Count > 0,
            endPrices.Count > 0));
    }

    /// <summary>
    /// 輔助方法：向 Stooq/TWSE 抓取缺少的基準價格並寫入快取。
    /// </summary>
    private async Task FetchAndCacheBenchmarkPricesAsync(
        List<string> marketKeys,
        int year,
        Dictionary<string, decimal> pricesDict,
        CancellationToken cancellationToken)
    {
        var yearMonth = $"{year}12";

        foreach (var marketKey in marketKeys)
        {
            try
            {
                // 檢查資料是否已存在（避免競態條件）
                var exists = await dbContext.IndexPriceSnapshots
                    .AnyAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth, cancellationToken);

                if (exists)
                {
                    // 已快取（有效或 NotAvailable），略過 API 呼叫
                    continue;
                }

                decimal? price;
                if (marketKey == "Taiwan 0050")
                {
                    // 台灣 ETF 使用 TWSE API
                    var twseResult = await twseService.GetYearEndPriceAsync("0050", year, cancellationToken);
                    price = twseResult?.Price;
                }
                else
                {
                    price = await stooqService.GetMonthEndPriceAsync(marketKey, year, 12, cancellationToken);
                }

                if (price != null)
                {
                    // 快取有效價格
                    dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                    {
                        MarketKey = marketKey,
                        YearMonth = yearMonth,
                        Price = price.Value,
                        IsNotAvailable = false,
                        RecordedAt = DateTime.UtcNow
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Cached benchmark price for {MarketKey} {YearMonth}: {Price}",
                        marketKey, yearMonth, price.Value);

                    pricesDict[marketKey] = price.Value;
                }
                else
                {
                    // 保存 NotAvailable 標記（負向快取）
                    dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                    {
                        MarketKey = marketKey,
                        YearMonth = yearMonth,
                        Price = null,
                        IsNotAvailable = true,
                        RecordedAt = DateTime.UtcNow
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Cached NotAvailable marker for {MarketKey} {YearMonth}",
                        marketKey, yearMonth);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch benchmark price for {MarketKey} {YearMonth}",
                    marketKey, yearMonth);
            }
        }
    }

    /// <summary>
    /// 當自動抓取失敗時，手動保存年終股票價格。
    /// 用於 Stooq 沒有資料的情境（例如：台股）。
    /// </summary>
    [HttpPost("year-end-price")]
    [ProducesResponseType(typeof(YearEndPriceResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<YearEndPriceResult>> SaveManualYearEndPrice(
        [FromBody] ManualYearEndPriceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Ticker))
        {
            return BadRequest("Ticker is required");
        }

        if (request.Year < 2000 || request.Year > DateTime.UtcNow.Year)
        {
            return BadRequest("Invalid year");
        }

        if (request.Price <= 0)
        {
            return BadRequest("Price must be positive");
        }

        try
        {
            var result = await historicalYearEndDataService.SaveManualPriceAsync(
                request.Ticker,
                request.Year,
                request.Price,
                request.Currency ?? "TWD",
                request.ActualDate ?? new DateTime(request.Year, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// 當自動抓取失敗時，手動保存年終匯率。
    /// </summary>
    [HttpPost("year-end-exchange-rate")]
    [ProducesResponseType(typeof(YearEndExchangeRateResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<YearEndExchangeRateResult>> SaveManualYearEndExchangeRate(
        [FromBody] ManualYearEndExchangeRateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FromCurrency) || string.IsNullOrWhiteSpace(request.ToCurrency))
        {
            return BadRequest("FromCurrency and ToCurrency are required");
        }

        if (request.Year < 2000 || request.Year > DateTime.UtcNow.Year)
        {
            return BadRequest("Invalid year");
        }

        if (request.Rate <= 0)
        {
            return BadRequest("Rate must be positive");
        }

        try
        {
            var result = await historicalYearEndDataService.SaveManualExchangeRateAsync(
                request.FromCurrency,
                request.ToCurrency,
                request.Year,
                request.Rate,
                request.ActualDate ?? new DateTime(request.Year, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                cancellationToken);

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// 取得指定交易日期的匯率。
    /// 自動抓取流程：cache → Stooq → persist。
    /// </summary>
    [HttpGet("transaction-date-exchange-rate")]
    [ProducesResponseType(typeof(TransactionDateExchangeRateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TransactionDateExchangeRateResponse>> GetTransactionDateExchangeRate(
        [FromQuery] string from,
        [FromQuery] string to,
        [FromQuery] DateTime date,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            return BadRequest("from and to currency codes are required");
        }

        if (date > DateTime.UtcNow.Date)
        {
            return BadRequest("Date cannot be in the future");
        }

        var result = await txDateFxService.GetOrFetchAsync(from, to, date, cancellationToken);

        if (result == null)
        {
            return NotFound($"Exchange rate not available for {from}/{to} on {date:yyyy-MM-dd}. Please submit manually.");
        }

        return Ok(new TransactionDateExchangeRateResponse(
            result.Rate,
            result.CurrencyPair,
            result.RequestedDate,
            result.ActualDate,
            result.Source,
            result.FromCache));
    }

    /// <summary>
    /// 當自動抓取失敗時，手動保存指定交易日期的匯率。
    /// </summary>
    [HttpPost("transaction-date-exchange-rate")]
    [ProducesResponseType(typeof(TransactionDateExchangeRateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TransactionDateExchangeRateResponse>> SaveManualTransactionDateExchangeRate(
        [FromBody] ManualTransactionDateExchangeRateRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FromCurrency) || string.IsNullOrWhiteSpace(request.ToCurrency))
        {
            return BadRequest("FromCurrency and ToCurrency are required");
        }

        if (request.TransactionDate > DateTime.UtcNow.Date)
        {
            return BadRequest("TransactionDate cannot be in the future");
        }

        if (request.Rate <= 0)
        {
            return BadRequest("Rate must be positive");
        }

        try
        {
            var result = await txDateFxService.SaveManualAsync(
                request.FromCurrency,
                request.ToCurrency,
                request.TransactionDate,
                request.Rate,
                cancellationToken);

            return Ok(new TransactionDateExchangeRateResponse(
                result.Rate,
                result.CurrencyPair,
                result.RequestedDate,
                result.ActualDate,
                result.Source,
                result.FromCache));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ex.Message);
        }
    }

    /// <summary>
    /// 依指定年份抓取 Stooq/TWSE 資料，批次填入歷史基準價格。
    /// 用於預先建立 IndexPriceSnapshot，以便後續計算年度報酬。
    /// </summary>
    /// <param name="year">要填入的年份（例如：2024 會填入 202412 資料）</param>
    /// <param name="cancellationToken"></param>
    [HttpPost("populate-benchmark-prices")]
    [ProducesResponseType(typeof(PopulateBenchmarkPricesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PopulateBenchmarkPricesResponse>> PopulateBenchmarkPrices(
        [FromQuery] int year,
        CancellationToken cancellationToken = default)
    {
        if (year < 2000 || year > DateTime.UtcNow.Year)
        {
            return BadRequest("Invalid year");
        }

        var yearMonth = $"{year}12"; // 要處理年份的 12 月
        Dictionary<string, PopulateBenchmarkResult> results = [];
        var benchmarks = MarketYtdService.SupportedBenchmarks;

        foreach (var (marketKey, _) in benchmarks)
        {
            try
            {
                // 檢查 DB 是否已存在
                var existing = await dbContext.IndexPriceSnapshots
                    .FirstOrDefaultAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth, cancellationToken);

                if (existing != null)
                {
                    results[marketKey] = new PopulateBenchmarkResult(
                        existing.Price,
                        "Already exists in database",
                        true);
                    continue;
                }

                decimal? price;
                var source = "Stooq";

                // 依市場選擇抓取來源
                if (marketKey == "Taiwan 0050")
                {
                    // 台灣 ETF 使用 TWSE API
                    var twseResult = await twseService.GetYearEndPriceAsync("0050", year, cancellationToken);
                    price = twseResult?.Price;
                    source = "TWSE";
                }
                else
                {
                    // 使用 Stooq 取得月末價格
                    price = await stooqService.GetMonthEndPriceAsync(
                        marketKey, year, 12, cancellationToken);
                }

                if (price != null)
                {
                    // 寫入 DB
                    dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                    {
                        MarketKey = marketKey,
                        YearMonth = yearMonth,
                        Price = price.Value,
                        RecordedAt = DateTime.UtcNow
                    });
                    await dbContext.SaveChangesAsync(cancellationToken);

                    results[marketKey] = new PopulateBenchmarkResult(
                        price.Value,
                        $"Fetched from {source} and saved",
                        true);
                }
                else
                {
                    results[marketKey] = new PopulateBenchmarkResult(
                        null,
                        $"Failed to fetch from {source}",
                        false);
                }
            }
            catch (Exception ex)
            {
                results[marketKey] = new PopulateBenchmarkResult(
                    null,
                    $"Error: {ex.Message}",
                    false);
            }
        }

        var successCount = results.Count(r => r.Value.Success);
        return Ok(new PopulateBenchmarkPricesResponse(
            year,
            yearMonth,
            results,
            successCount,
            results.Count - successCount));
    }
}

/// <summary>
/// 批次填入歷史基準價格的回應。
/// </summary>
public record PopulateBenchmarkPricesResponse(
    int Year,
    string YearMonth,
    Dictionary<string, PopulateBenchmarkResult> Results,
    int SuccessCount,
    int FailCount);

/// <summary>
/// 單一基準價格填入結果。
/// </summary>
public record PopulateBenchmarkResult(decimal? Price, string Message, bool Success);

/// <summary>
/// 年度基準報酬查詢回應。
/// </summary>
public record BenchmarkReturnsResponse(
    int Year,
    Dictionary<string, decimal?> Returns,
    bool HasStartPrices,
    bool HasEndPrices);

public record IndexPriceRequest(string MarketKey, string YearMonth, decimal Price);

/// <summary>
/// 多檔股票歷史價格查詢請求。
/// </summary>
public record HistoricalPricesRequest(string[] Tickers, string Date);

/// <summary>
/// 歷史價格查詢回應。
/// </summary>
public record HistoricalPriceResponse(decimal Price, string Currency, string ActualDate);

/// <summary>
/// Euronext 報價查詢請求。
/// </summary>
public record EuronextQuoteRequest(string Isin, string Mic, string? HomeCurrency);

/// <summary>
/// Euronext 報價查詢回應。
/// </summary>
public record EuronextQuoteResponse(
    decimal Price,
    string Currency,
    DateTime? MarketTime,
    string? Name,
    decimal? ExchangeRate,
    bool FromCache,
    string? ChangePercent = null,
    decimal? Change = null);

/// <summary>
/// 歷史匯率查詢回應。
/// </summary>
public record HistoricalExchangeRateResponse(decimal Rate, string FromCurrency, string ToCurrency, string ActualDate);

/// <summary>
/// 手動保存年終股票價格的請求。
/// </summary>
public record ManualYearEndPriceRequest(
    string Ticker,
    int Year,
    decimal Price,
    string? Currency = "TWD",
    DateTime? ActualDate = null);

/// <summary>
/// 手動保存年終匯率的請求。
/// </summary>
public record ManualYearEndExchangeRateRequest(
    string FromCurrency,
    string ToCurrency,
    int Year,
    decimal Rate,
    DateTime? ActualDate = null);

/// <summary>
/// 手動保存交易日期匯率的請求。
/// </summary>
public record ManualTransactionDateExchangeRateRequest(
    string FromCurrency,
    string ToCurrency,
    DateTime TransactionDate,
    decimal Rate);

/// <summary>
/// 交易日期匯率查詢回應。
/// </summary>
public record TransactionDateExchangeRateResponse(
    decimal Rate,
    string CurrencyPair,
    DateTime RequestedDate,
    DateTime ActualDate,
    string Source,
    bool FromCache);
