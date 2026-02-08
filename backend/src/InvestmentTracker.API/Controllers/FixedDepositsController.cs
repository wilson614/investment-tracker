using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.FixedDeposits;
using InvestmentTracker.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Fixed deposits CRUD API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api/fixed-deposits")]
public class FixedDepositsController(
    GetFixedDepositsUseCase getFixedDepositsUseCase,
    GetFixedDepositUseCase getFixedDepositUseCase,
    CreateFixedDepositUseCase createFixedDepositUseCase,
    UpdateFixedDepositUseCase updateFixedDepositUseCase,
    CloseFixedDepositUseCase closeFixedDepositUseCase) : ControllerBase
{
    /// <summary>
    /// Get all fixed deposits for current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<FixedDepositResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<FixedDepositResponse>>> GetAll(
        [FromQuery] FixedDepositStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var fixedDeposits = await getFixedDepositsUseCase.ExecuteAsync(status, cancellationToken);
        return Ok(fixedDeposits);
    }

    /// <summary>
    /// Get fixed deposit by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(FixedDepositResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FixedDepositResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var fixedDeposit = await getFixedDepositUseCase.ExecuteAsync(id, cancellationToken);
        return Ok(fixedDeposit);
    }

    /// <summary>
    /// Create a fixed deposit.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(FixedDepositResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FixedDepositResponse>> Create(
        [FromBody] CreateFixedDepositRequest request,
        CancellationToken cancellationToken)
    {
        var fixedDeposit = await createFixedDepositUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = fixedDeposit.Id }, fixedDeposit);
    }

    /// <summary>
    /// Update a fixed deposit.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(FixedDepositResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FixedDepositResponse>> Update(
        Guid id,
        [FromBody] UpdateFixedDepositRequest request,
        CancellationToken cancellationToken)
    {
        var fixedDeposit = await updateFixedDepositUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(fixedDeposit);
    }

    /// <summary>
    /// Close a fixed deposit.
    /// </summary>
    [HttpPost("{id:guid}/close")]
    [ProducesResponseType(typeof(FixedDepositResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FixedDepositResponse>> Close(
        Guid id,
        [FromBody] CloseFixedDepositRequest request,
        CancellationToken cancellationToken)
    {
        var fixedDeposit = await closeFixedDepositUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(fixedDeposit);
    }
}
