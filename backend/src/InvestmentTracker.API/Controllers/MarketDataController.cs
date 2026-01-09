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
    private readonly AppDbContext _dbContext;

    public MarketDataController(
        ICapeDataService capeDataService,
        IMarketYtdService marketYtdService,
        AppDbContext dbContext)
    {
        _capeDataService = capeDataService;
        _marketYtdService = marketYtdService;
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
}

public record IndexPriceRequest(string MarketKey, string YearMonth, decimal Price);
