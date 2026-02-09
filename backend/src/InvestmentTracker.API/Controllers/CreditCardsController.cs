using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.CreditCards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Credit cards CRUD API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api/credit-cards")]
public class CreditCardsController(
    GetCreditCardsUseCase getCreditCardsUseCase,
    GetCreditCardUseCase getCreditCardUseCase,
    CreateCreditCardUseCase createCreditCardUseCase,
    UpdateCreditCardUseCase updateCreditCardUseCase) : ControllerBase
{
    /// <summary>
    /// Get all credit cards for current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CreditCardResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CreditCardResponse>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        var creditCards = await getCreditCardsUseCase.ExecuteAsync(cancellationToken);
        return Ok(creditCards);
    }

    /// <summary>
    /// Get credit card by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CreditCardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CreditCardResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var creditCard = await getCreditCardUseCase.ExecuteAsync(id, cancellationToken);
        return Ok(creditCard);
    }

    /// <summary>
    /// Create a credit card.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreditCardResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CreditCardResponse>> Create(
        [FromBody] CreateCreditCardRequest request,
        CancellationToken cancellationToken)
    {
        var creditCard = await createCreditCardUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = creditCard.Id }, creditCard);
    }

    /// <summary>
    /// Update a credit card.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CreditCardResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CreditCardResponse>> Update(
        Guid id,
        [FromBody] UpdateCreditCardRequest request,
        CancellationToken cancellationToken)
    {
        var creditCard = await updateCreditCardUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(creditCard);
    }

}
