using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.AvailableFunds;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Available funds summary API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api/available-funds")]
public class AvailableFundsController(GetAvailableFundsSummaryUseCase getAvailableFundsSummaryUseCase) : ControllerBase
{
    /// <summary>
    /// Get available funds summary for current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AvailableFundsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AvailableFundsSummaryResponse>> Get(CancellationToken cancellationToken)
    {
        var summary = await getAvailableFundsSummaryUseCase.ExecuteAsync(cancellationToken);
        return Ok(summary);
    }
}
