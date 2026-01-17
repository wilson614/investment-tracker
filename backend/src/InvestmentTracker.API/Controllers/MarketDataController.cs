using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Infrastructure.MarketData;
using InvestmentTracker.Infrastructure.Persistence;
using InvestmentTracker.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.API.Controllers;

[ApiController]
[Route("api/market-data")]
[Authorize]
public class MarketDataController : ControllerBase
{
    private readonly ICapeDataService _capeDataService;
    private readonly IMarketYtdService _marketYtdService;
    private readonly EuronextQuoteService _euronextQuoteService;
    private readonly IStooqHistoricalPriceService _stooqService;
    private readonly IHistoricalYearEndDataService _historicalYearEndDataService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<MarketDataController> _logger;

    public MarketDataController(
        ICapeDataService capeDataService,
        IMarketYtdService marketYtdService,
        EuronextQuoteService euronextQuoteService,
        IStooqHistoricalPriceService stooqService,
        IHistoricalYearEndDataService historicalYearEndDataService,
        AppDbContext dbContext,
        ILogger<MarketDataController> logger)
    {
        _capeDataService = capeDataService;
        _marketYtdService = marketYtdService;
        _euronextQuoteService = euronextQuoteService;
        _stooqService = stooqService;
        _historicalYearEndDataService = historicalYearEndDataService;
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Get CAPE (Cyclically Adjusted P/E) data from Research Affiliates
    /// Data is cached for 24 hours
    /// </summary>
    [HttpGet("cape")]
    [ProducesResponseType(typeof(CapeDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CapeDataResponse>> GetCapeData(CancellationToken cancellationToken)
    {
        var data = await _capeDataService.GetCapeDataAsync(cancellationToken);

        if (data == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Unable to fetch CAPE data");
        }

        return Ok(data);
    }

    /// <summary>
    /// Force refresh CAPE data (clears cache)
    /// </summary>
    [HttpPost("cape/refresh")]
    [ProducesResponseType(typeof(CapeDataResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CapeDataResponse>> RefreshCapeData(CancellationToken cancellationToken)
    {
        var data = await _capeDataService.RefreshCapeDataAsync(cancellationToken);

        if (data == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "Unable to fetch CAPE data");
        }

        return Ok(data);
    }

    /// <summary>
    /// Get all stored index price snapshots for CAPE adjustment
    /// </summary>
    [HttpGet("index-prices")]
    [ProducesResponseType(typeof(List<IndexPriceSnapshot>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<IndexPriceSnapshot>>> GetIndexPrices(CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.IndexPriceSnapshots
            .OrderByDescending(s => s.YearMonth)
            .ThenBy(s => s.MarketKey)
            .ToListAsync(cancellationToken);

        return Ok(snapshots);
    }

    /// <summary>
    /// Add or update an index price snapshot for CAPE adjustment
    /// </summary>
    [HttpPost("index-prices")]
    [ProducesResponseType(typeof(IndexPriceSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<IndexPriceSnapshot>> UpsertIndexPrice(
        [FromBody] IndexPriceRequest request,
        CancellationToken cancellationToken)
    {
        // Validate market key
        if (!IndexPriceService.SupportedMarkets.Contains(request.MarketKey))
        {
            return BadRequest($"Unsupported market: {request.MarketKey}. Supported markets: {string.Join(", ", IndexPriceService.SupportedMarkets)}");
        }

        // Validate year-month format
        if (request.YearMonth.Length != 6 || !int.TryParse(request.YearMonth, out _))
        {
            return BadRequest("YearMonth must be in YYYYMM format (e.g., 202512)");
        }

        var existing = await _dbContext.IndexPriceSnapshots
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
            _dbContext.IndexPriceSnapshots.Add(existing);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(existing);
    }

    /// <summary>
    /// Get supported markets for CAPE adjustment
    /// </summary>
    [HttpGet("supported-markets")]
    [ProducesResponseType(typeof(IEnumerable<string>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<string>> GetSupportedMarkets()
    {
        return Ok(IndexPriceService.SupportedMarkets);
    }

    /// <summary>
    /// Get YTD (Year-to-Date) returns for benchmark ETFs
    /// Benchmarks: VWRA (All Country), VUAA (US Large), 0050 (Taiwan), VFEM (Emerging Markets)
    /// </summary>
    [HttpGet("ytd-comparison")]
    [ProducesResponseType(typeof(MarketYtdComparisonDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketYtdComparisonDto>> GetYtdComparison(CancellationToken cancellationToken)
    {
        var data = await _marketYtdService.GetYtdComparisonAsync(cancellationToken);
        return Ok(data);
    }

    /// <summary>
    /// Force refresh YTD comparison data
    /// </summary>
    [HttpPost("ytd-comparison/refresh")]
    [ProducesResponseType(typeof(MarketYtdComparisonDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<MarketYtdComparisonDto>> RefreshYtdComparison(CancellationToken cancellationToken)
    {
        var data = await _marketYtdService.RefreshYtdComparisonAsync(cancellationToken);
        return Ok(data);
    }

    /// <summary>
    /// Get supported benchmarks for YTD comparison
    /// </summary>
    [HttpGet("ytd-benchmarks")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public ActionResult GetYtdBenchmarks()
    {
        var benchmarks = MarketYtdService.SupportedBenchmarks.Select(b => new
        {
            MarketKey = b.Key,
            Symbol = b.Value.Symbol,
            Name = b.Value.Name
        });
        return Ok(benchmarks);
    }

    /// <summary>
    /// Get quote for a Euronext-listed stock (e.g., AGAC on Amsterdam)
    /// </summary>
    /// <param name="isin">ISIN code (e.g., IE000FHBZDZ8)</param>
    /// <param name="mic">Market Identifier Code (e.g., XAMS for Amsterdam)</param>
    /// <param name="homeCurrency">Target currency for exchange rate (default: TWD)</param>
    /// <param name="refresh">Force refresh (bypass cache)</param>
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

        var quote = await _euronextQuoteService.GetQuoteAsync(
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
    /// Get historical closing price for a stock on a specific date.
    /// Uses Stooq API for US/UK stocks.
    /// </summary>
    /// <param name="ticker">Stock ticker (e.g., AAPL, VWRA)</param>
    /// <param name="date">Target date (format: yyyy-MM-dd)</param>
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

        // For completed years, Dec 31 lookups are used as year-end prices.
        // Cache them globally (shared across all users) to reduce repeated Stooq calls.
        if (targetDate.Month == 12 && targetDate.Day == 31 && targetDate.Year < DateTime.UtcNow.Year)
        {
            var cachedResult = await _historicalYearEndDataService.GetOrFetchYearEndPriceAsync(
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

        var result = await _stooqService.GetStockPriceAsync(
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
    /// Get historical closing prices for multiple stocks on a specific date.
    /// Uses Stooq API for US/UK stocks.
    /// </summary>
    [HttpPost("historical-prices")]
    [ProducesResponseType(typeof(Dictionary<string, HistoricalPriceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<Dictionary<string, HistoricalPriceResponse>>> GetHistoricalPrices(
        [FromBody] HistoricalPricesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Tickers == null || request.Tickers.Length == 0)
        {
            return BadRequest("At least one ticker is required");
        }

        if (!DateOnly.TryParse(request.Date, out var targetDate))
        {
            return BadRequest("Date must be in yyyy-MM-dd format");
        }

        var results = new Dictionary<string, HistoricalPriceResponse>();

        var isCompletedYearEndLookup =
            targetDate.Month == 12 &&
            targetDate.Day == 31 &&
            targetDate.Year < DateTime.UtcNow.Year;

        // Fetch prices in parallel
        var tasks = request.Tickers.Select(async (string ticker) =>
        {
            var normalizedTicker = ticker.Trim().ToUpperInvariant();

            if (isCompletedYearEndLookup)
            {
                var cachedResult = await _historicalYearEndDataService.GetOrFetchYearEndPriceAsync(
                    normalizedTicker,
                    targetDate.Year,
                    cancellationToken);

                if (cachedResult == null)
                {
                    return (ValueTuple<string, decimal, string, DateOnly>?)null;
                }

                return (ticker, cachedResult.Price, cachedResult.Currency, DateOnly.FromDateTime(cachedResult.ActualDate));
            }

            var result = await _stooqService.GetStockPriceAsync(
                normalizedTicker,
                targetDate,
                cancellationToken);

            if (result == null)
            {
                return (ValueTuple<string, decimal, string, DateOnly>?)null;
            }

            return (ticker, result.Price, result.Currency, result.ActualDate);
        });

        var priceResults = await Task.WhenAll(tasks);

        foreach (var item in priceResults)
        {
            if (item is { } value)
            {
                var (ticker, price, currency, actualDate) = value;
                results[ticker] = new HistoricalPriceResponse(
                    price,
                    currency,
                    actualDate.ToString("yyyy-MM-dd"));
            }
        }

        return Ok(results);
    }

    /// <summary>
    /// Get historical exchange rate for a currency pair on a specific date.
    /// Uses Stooq forex data.
    /// </summary>
    /// <param name="from">Source currency (e.g., USD)</param>
    /// <param name="to">Target currency (e.g., TWD)</param>
    /// <param name="date">Target date (format: yyyy-MM-dd)</param>
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

        // For completed years, Dec 31 lookups are used as year-end rates.
        // Cache them globally (shared across all users) to reduce repeated Stooq calls.
        if (targetDate.Month == 12 && targetDate.Day == 31 && targetDate.Year < DateTime.UtcNow.Year)
        {
            var cachedResult = await _historicalYearEndDataService.GetOrFetchYearEndExchangeRateAsync(
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

        var result = await _stooqService.GetExchangeRateAsync(
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
    /// Get annual benchmark returns for a specific year.
    /// Uses cached IndexPriceSnapshot data. Auto-fetches from Stooq if missing.
    /// </summary>
    /// <param name="year">Year to calculate returns for (e.g., 2025)</param>
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

        var startYearMonth = $"{year - 1}12";  // Prior year December
        var endYearMonth = $"{year}12";        // Current year December
        var benchmarks = MarketYtdService.SupportedBenchmarks;

        // Get all cached index prices for both year-months
        var snapshots = await _dbContext.IndexPriceSnapshots
            .Where(s => s.YearMonth == startYearMonth || s.YearMonth == endYearMonth)
            .ToListAsync(cancellationToken);

        var startPrices = snapshots
            .Where(s => s.YearMonth == startYearMonth)
            .GroupBy(s => s.MarketKey)
            .ToDictionary(g => g.Key, g => g.First().Price);

        var endPrices = snapshots
            .Where(s => s.YearMonth == endYearMonth)
            .GroupBy(s => s.MarketKey)
            .ToDictionary(g => g.Key, g => g.First().Price);

        // Lazy-load missing prices from Stooq (except Taiwan 0050 which needs manual entry)
        var missingStartMarkets = benchmarks.Keys
            .Where(k => !startPrices.ContainsKey(k) && k != "Taiwan 0050")
            .ToList();
        var missingEndMarkets = benchmarks.Keys
            .Where(k => !endPrices.ContainsKey(k) && k != "Taiwan 0050")
            .ToList();

        // Fetch missing start year prices (prior year December)
        if (missingStartMarkets.Count > 0)
        {
            _logger.LogInformation("Lazy-loading {Count} missing benchmark prices for {YearMonth}",
                missingStartMarkets.Count, startYearMonth);
            await FetchAndCacheBenchmarkPricesAsync(missingStartMarkets, year - 1, startPrices, cancellationToken);
        }

        // Fetch missing end year prices (current year December) - only for completed years
        if (missingEndMarkets.Count > 0 && year < DateTime.UtcNow.Year)
        {
            _logger.LogInformation("Lazy-loading {Count} missing benchmark prices for {YearMonth}",
                missingEndMarkets.Count, endYearMonth);
            await FetchAndCacheBenchmarkPricesAsync(missingEndMarkets, year, endPrices, cancellationToken);
        }

        var returns = new Dictionary<string, decimal?>();

        foreach (var (marketKey, _) in benchmarks)
        {
            if (startPrices.TryGetValue(marketKey, out var startPrice) &&
                endPrices.TryGetValue(marketKey, out var endPrice) &&
                startPrice > 0)
            {
                var returnPercent = ((endPrice - startPrice) / startPrice) * 100;
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
    /// Helper method to fetch and cache missing benchmark prices from Stooq.
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
                var price = await _stooqService.GetMonthEndPriceAsync(marketKey, year, 12, cancellationToken);

                if (price != null)
                {
                    // Check if record already exists (race condition protection)
                    var exists = await _dbContext.IndexPriceSnapshots
                        .AnyAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth, cancellationToken);

                    if (!exists)
                    {
                        _dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                        {
                            MarketKey = marketKey,
                            YearMonth = yearMonth,
                            Price = price.Value,
                            RecordedAt = DateTime.UtcNow
                        });
                        await _dbContext.SaveChangesAsync(cancellationToken);
                        _logger.LogInformation("Cached benchmark price for {MarketKey} {YearMonth}: {Price}",
                            marketKey, yearMonth, price.Value);
                    }

                    pricesDict[marketKey] = price.Value;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch benchmark price for {MarketKey} {YearMonth}",
                    marketKey, yearMonth);
            }
        }
    }

    /// <summary>
    /// Manually save a year-end stock price when automatic fetching fails.
    /// This is for cases where Stooq API doesn't have the data (e.g., Taiwan stocks).
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
            var result = await _historicalYearEndDataService.SaveManualPriceAsync(
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
    /// Manually save a year-end exchange rate when automatic fetching fails.
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
            var result = await _historicalYearEndDataService.SaveManualExchangeRateAsync(
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
    /// Populate historical benchmark prices for a given year by fetching from Stooq/TWSE.
    /// This is used to seed IndexPriceSnapshot data for historical year returns calculation.
    /// </summary>
    /// <param name="year">Year to populate (e.g., 2024 will populate 202412 data)</param>
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

        var yearMonth = $"{year}12"; // December of the requested year
        var results = new Dictionary<string, PopulateBenchmarkResult>();
        var benchmarks = MarketYtdService.SupportedBenchmarks;

        foreach (var (marketKey, benchmarkInfo) in benchmarks)
        {
            try
            {
                // Check if already exists in database
                var existing = await _dbContext.IndexPriceSnapshots
                    .FirstOrDefaultAsync(s => s.MarketKey == marketKey && s.YearMonth == yearMonth, cancellationToken);

                if (existing != null)
                {
                    results[marketKey] = new PopulateBenchmarkResult(
                        existing.Price,
                        "Already exists in database",
                        true);
                    continue;
                }

                decimal? price = null;

                // Fetch from appropriate source based on market
                if (marketKey == "Taiwan 0050")
                {
                    // For Taiwan stocks, we'd need TWSE historical API which is complex
                    // Skip for now - can be manually entered
                    results[marketKey] = new PopulateBenchmarkResult(
                        null,
                        "Taiwan stocks require manual entry",
                        false);
                    continue;
                }
                else
                {
                    // Use Stooq for UK-listed ETFs
                    price = await _stooqService.GetMonthEndPriceAsync(
                        marketKey, year, 12, cancellationToken);
                }

                if (price != null)
                {
                    // Save to database
                    _dbContext.IndexPriceSnapshots.Add(new IndexPriceSnapshot
                    {
                        MarketKey = marketKey,
                        YearMonth = yearMonth,
                        Price = price.Value,
                        RecordedAt = DateTime.UtcNow
                    });
                    await _dbContext.SaveChangesAsync(cancellationToken);

                    results[marketKey] = new PopulateBenchmarkResult(
                        price.Value,
                        "Fetched from Stooq and saved",
                        true);
                }
                else
                {
                    results[marketKey] = new PopulateBenchmarkResult(
                        null,
                        "Failed to fetch from external API",
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
/// Response for populate benchmark prices operation.
/// </summary>
public record PopulateBenchmarkPricesResponse(
    int Year,
    string YearMonth,
    Dictionary<string, PopulateBenchmarkResult> Results,
    int SuccessCount,
    int FailCount);

/// <summary>
/// Result for individual benchmark price population.
/// </summary>
public record PopulateBenchmarkResult(decimal? Price, string Message, bool Success);

/// <summary>
/// Response for annual benchmark returns.
/// </summary>
public record BenchmarkReturnsResponse(
    int Year,
    Dictionary<string, decimal?> Returns,
    bool HasStartPrices,
    bool HasEndPrices);

public record IndexPriceRequest(string MarketKey, string YearMonth, decimal Price);

/// <summary>
/// Request for fetching historical prices for multiple stocks.
/// </summary>
public record HistoricalPricesRequest(string[] Tickers, string Date);

/// <summary>
/// Response for historical price lookup.
/// </summary>
public record HistoricalPriceResponse(decimal Price, string Currency, string ActualDate);

/// <summary>
/// Request for fetching Euronext quote.
/// </summary>
public record EuronextQuoteRequest(string Isin, string Mic, string? HomeCurrency);

/// <summary>
/// Response for Euronext quote.
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
/// Response for historical exchange rate lookup.
/// </summary>
public record HistoricalExchangeRateResponse(decimal Rate, string FromCurrency, string ToCurrency, string ActualDate);

/// <summary>
/// Request for manually saving a year-end stock price.
/// </summary>
public record ManualYearEndPriceRequest(
    string Ticker,
    int Year,
    decimal Price,
    string? Currency = "TWD",
    DateTime? ActualDate = null);

/// <summary>
/// Request for manually saving a year-end exchange rate.
/// </summary>
public record ManualYearEndExchangeRateRequest(
    string FromCurrency,
    string ToCurrency,
    int Year,
    decimal Rate,
    DateTime? ActualDate = null);
