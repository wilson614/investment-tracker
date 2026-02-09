using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.Installments;
using InvestmentTracker.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Installments API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api")]
public class InstallmentsController(
    GetInstallmentsUseCase getInstallmentsUseCase,
    GetAllUserInstallmentsUseCase getAllUserInstallmentsUseCase,
    CreateInstallmentUseCase createInstallmentUseCase,
    UpdateInstallmentUseCase updateInstallmentUseCase,
    GetUpcomingPaymentsUseCase getUpcomingPaymentsUseCase) : ControllerBase
{
    /// <summary>
    /// Get all installments for a credit card.
    /// </summary>
    [HttpGet("credit-cards/{cardId:guid}/installments")]
    [ProducesResponseType(typeof(IEnumerable<InstallmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<InstallmentResponse>>> GetByCreditCard(
        Guid cardId,
        [FromQuery] InstallmentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var installments = await getInstallmentsUseCase.ExecuteAsync(cardId, status, cancellationToken);
        return Ok(installments);
    }

    /// <summary>
    /// Get all installments for current user.
    /// </summary>
    [HttpGet("installments")]
    [ProducesResponseType(typeof(IEnumerable<InstallmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<InstallmentResponse>>> GetAll(
        [FromQuery] InstallmentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var installments = await getAllUserInstallmentsUseCase.ExecuteAsync(cancellationToken);

        var filteredInstallments = status.HasValue
            ? installments.Where(i => i.Status == status.Value.ToString()).ToList()
            : installments;

        return Ok(filteredInstallments);
    }

    /// <summary>
    /// Get installment by ID.
    /// </summary>
    [HttpGet("installments/{id:guid}")]
    [ProducesResponseType(typeof(InstallmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstallmentResponse>> GetById(Guid id, CancellationToken cancellationToken = default)
    {
        var installments = await getAllUserInstallmentsUseCase.ExecuteAsync(cancellationToken);
        var installment = installments.FirstOrDefault(i => i.Id == id);

        if (installment == null)
            return NotFound();

        return Ok(installment);
    }

    /// <summary>
    /// Create a new installment purchase.
    /// </summary>
    [HttpPost("credit-cards/{cardId:guid}/installments")]
    [ProducesResponseType(typeof(InstallmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstallmentResponse>> Create(
        Guid cardId,
        [FromBody] CreateInstallmentRequest request,
        CancellationToken cancellationToken)
    {
        var createRequest = request with { CreditCardId = cardId };
        var installment = await createInstallmentUseCase.ExecuteAsync(createRequest, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = installment.Id }, installment);
    }

    /// <summary>
    /// Update an installment.
    /// </summary>
    [HttpPut("installments/{id:guid}")]
    [ProducesResponseType(typeof(InstallmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<InstallmentResponse>> Update(
        Guid id,
        [FromBody] UpdateInstallmentRequest request,
        CancellationToken cancellationToken)
    {
        var installment = await updateInstallmentUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(installment);
    }

    /// <summary>
    /// Get upcoming installment payments for next N months.
    /// </summary>
    [HttpGet("installments/upcoming")]
    [ProducesResponseType(typeof(UpcomingPaymentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<UpcomingPaymentsResponse>> GetUpcoming(
        [FromQuery] int months = 3,
        CancellationToken cancellationToken = default)
    {
        var monthSummaries = await getUpcomingPaymentsUseCase.ExecuteAsync(months, cancellationToken);
        return Ok(new UpcomingPaymentsResponse(monthSummaries));
    }

    public record UpcomingPaymentsResponse(IReadOnlyList<UpcomingPaymentMonthSummary> Months);
}
