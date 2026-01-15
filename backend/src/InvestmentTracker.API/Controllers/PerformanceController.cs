using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Controller for portfolio performance analysis endpoints.
/// </summary>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/performance")]
[Authorize]
public class PerformanceController : ControllerBase
{
    private readonly IHistoricalPerformanceService _performanceService;
    private readonly ILogger<PerformanceController> _logger;

    public PerformanceController(
        IHistoricalPerformanceService performanceService,
        ILogger<PerformanceController> logger)
    {
        _performanceService = performanceService;
        _logger = logger;
    }

    /// <summary>
    /// Get available years for performance calculation.
    /// Returns list of years with transaction data.
    /// </summary>
    [HttpGet("years")]
    [ProducesResponseType(typeof(AvailableYearsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AvailableYearsDto>> GetAvailableYears(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _performanceService.GetAvailableYearsAsync(portfolioId, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Calculate performance for a specific year.
    /// </summary>
    [HttpPost("year")]
    [ProducesResponseType(typeof(YearPerformanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<YearPerformanceDto>> CalculateYearPerformance(
        Guid portfolioId,
        [FromBody] CalculateYearPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _performanceService.CalculateYearPerformanceAsync(
                portfolioId, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
