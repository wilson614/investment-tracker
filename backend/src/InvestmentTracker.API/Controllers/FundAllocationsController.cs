using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.FundAllocation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Fund allocations CRUD API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FundAllocationsController(
    GetFundAllocationsUseCase getFundAllocationsUseCase,
    CreateFundAllocationUseCase createFundAllocationUseCase,
    UpdateFundAllocationUseCase updateFundAllocationUseCase,
    DeleteFundAllocationUseCase deleteFundAllocationUseCase) : ControllerBase
{
    /// <summary>
    /// Get all fund allocations for current user with summary.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AllocationSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AllocationSummary>> GetAll(CancellationToken cancellationToken)
    {
        var summary = await getFundAllocationsUseCase.ExecuteAsync(cancellationToken);
        return Ok(summary);
    }

    /// <summary>
    /// Create a fund allocation.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FundAllocationResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FundAllocationResponse>> Create(
        [FromBody] CreateFundAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var allocation = await createFundAllocationUseCase.ExecuteAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, allocation);
    }

    /// <summary>
    /// Update a fund allocation.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FundAllocationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FundAllocationResponse>> Update(
        Guid id,
        [FromBody] UpdateFundAllocationRequest request,
        CancellationToken cancellationToken)
    {
        var allocation = await updateFundAllocationUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(allocation);
    }

    /// <summary>
    /// Delete a fund allocation.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteFundAllocationUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}
