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
    private readonly AppDbContext _dbContext;

    public MarketDataController(
        ICapeDataService capeDataService,
        IMarketYtdService marketYtdService,
        EuronextQuoteService euronextQuoteService,
        AppDbContext dbContext)
    {
        _capeDataService = capeDataService;
        _marketYtdService = marketYtdService;
        _euronextQuoteService = euronextQuoteService;
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
            quote.FromCache));
    }
}

public record IndexPriceRequest(string MarketKey, string YearMonth, decimal Price);

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
    bool FromCache);
