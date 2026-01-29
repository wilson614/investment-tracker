using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.Assets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Assets summary API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api/assets")]
public class AssetsController(GetTotalAssetsSummaryUseCase getTotalAssetsSummaryUseCase) : ControllerBase
{
    /// <summary>
    /// Get total assets summary for current user.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(TotalAssetsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TotalAssetsSummaryResponse>> GetSummary(CancellationToken cancellationToken)
    {
        var summary = await getTotalAssetsSummaryUseCase.ExecuteAsync(cancellationToken);
        return Ok(summary);
    }
}
