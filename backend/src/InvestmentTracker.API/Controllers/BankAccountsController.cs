using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.BankAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// Bank accounts CRUD API.
/// </summary>
/// <remarks>
/// Exceptions are handled by ExceptionHandlingMiddleware:
/// - EntityNotFoundException -> 404 Not Found
/// - AccessDeniedException -> 403 Forbidden
/// - BusinessRuleException -> 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api/bank-accounts")]
public class BankAccountsController(
    GetBankAccountsUseCase getBankAccountsUseCase,
    GetBankAccountUseCase getBankAccountUseCase,
    CreateBankAccountUseCase createBankAccountUseCase,
    UpdateBankAccountUseCase updateBankAccountUseCase,
    DeleteBankAccountUseCase deleteBankAccountUseCase) : ControllerBase
{
    /// <summary>
    /// Get all bank accounts for current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<BankAccountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<BankAccountResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var accounts = await getBankAccountsUseCase.ExecuteAsync(cancellationToken);
        return Ok(accounts);
    }

    /// <summary>
    /// Get bank account by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BankAccountResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var account = await getBankAccountUseCase.ExecuteAsync(id, cancellationToken);
        return Ok(account);
    }

    /// <summary>
    /// Create a bank account.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BankAccountResponse>> Create(
        [FromBody] CreateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await createBankAccountUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = account.Id }, account);
    }

    /// <summary>
    /// Update a bank account.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(BankAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<BankAccountResponse>> Update(
        Guid id,
        [FromBody] UpdateBankAccountRequest request,
        CancellationToken cancellationToken)
    {
        var account = await updateBankAccountUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(account);
    }

    /// <summary>
    /// Delete (soft delete) a bank account.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteBankAccountUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}
