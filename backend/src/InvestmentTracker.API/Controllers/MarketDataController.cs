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
    private readonly AppDbContext _dbContext;

    public MarketDataController(
        ICapeDataService capeDataService,
        IMarketYtdService marketYtdService,
        EuronextQuoteService euronextQuoteService,
        IStooqHistoricalPriceService stooqService,
        AppDbContext dbContext)
    {
        _capeDataService = capeDataService;
        _marketYtdService = marketYtdService;
        _euronextQuoteService = euronextQuoteService;
        _stooqService = stooqService;
        _dbContext = dbContext;
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

        var result = await _stooqService.GetStockPriceAsync(
            ticker.Trim().ToUpperInvariant(),
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

        // Fetch prices in parallel
        var tasks = request.Tickers.Select(async ticker =>
        {
            var result = await _stooqService.GetStockPriceAsync(
                ticker.Trim().ToUpperInvariant(),
                targetDate,
                cancellationToken);

            return (ticker, result);
        });

        var priceResults = await Task.WhenAll(tasks);

        foreach (var (ticker, result) in priceResults)
        {
            if (result != null)
            {
                results[ticker] = new HistoricalPriceResponse(
                    result.Price,
                    result.Currency,
                    result.ActualDate.ToString("yyyy-MM-dd"));
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

        var result = await _stooqService.GetExchangeRateAsync(
            from.Trim().ToUpperInvariant(),
            to.Trim().ToUpperInvariant(),
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
    /// Uses cached IndexPriceSnapshot data instead of hitting external APIs.
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

        var returns = new Dictionary<string, decimal?>();
        var benchmarks = MarketYtdService.SupportedBenchmarks;

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
}

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
