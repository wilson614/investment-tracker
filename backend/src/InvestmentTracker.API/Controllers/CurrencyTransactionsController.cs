using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.CurrencyTransactions;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CurrencyTransactionsController : ControllerBase
{
    private readonly CreateCurrencyTransactionUseCase _createUseCase;
    private readonly UpdateCurrencyTransactionUseCase _updateUseCase;
    private readonly DeleteCurrencyTransactionUseCase _deleteUseCase;
    private readonly ICurrencyTransactionRepository _transactionRepository;

    public CurrencyTransactionsController(
        CreateCurrencyTransactionUseCase createUseCase,
        UpdateCurrencyTransactionUseCase updateUseCase,
        DeleteCurrencyTransactionUseCase deleteUseCase,
        ICurrencyTransactionRepository transactionRepository)
    {
        _createUseCase = createUseCase;
        _updateUseCase = updateUseCase;
        _deleteUseCase = deleteUseCase;
        _transactionRepository = transactionRepository;
    }

    /// <summary>
    /// Get all transactions for a currency ledger.
    /// </summary>
    [HttpGet("ledger/{ledgerId:guid}")]
    [ProducesResponseType(typeof(IEnumerable<CurrencyTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CurrencyTransactionDto>>> GetByLedger(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        var transactions = await _transactionRepository.GetByLedgerIdOrderedAsync(
            ledgerId, cancellationToken);

        return Ok(transactions.Select(t => new CurrencyTransactionDto
        {
            Id = t.Id,
            CurrencyLedgerId = t.CurrencyLedgerId,
            TransactionDate = t.TransactionDate,
            TransactionType = t.TransactionType,
            ForeignAmount = t.ForeignAmount,
            HomeAmount = t.HomeAmount,
            ExchangeRate = t.ExchangeRate,
            RelatedStockTransactionId = t.RelatedStockTransactionId,
            Notes = t.Notes,
            CreatedAt = t.CreatedAt,
            UpdatedAt = t.UpdatedAt
        }));
    }

    /// <summary>
    /// Create a new currency transaction.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CurrencyTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CurrencyTransactionDto>> Create(
        [FromBody] CreateCurrencyTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _createUseCase.ExecuteAsync(request, cancellationToken);
            return CreatedAtAction(
                nameof(GetByLedger),
                new { ledgerId = transaction.CurrencyLedgerId },
                transaction);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Update a currency transaction.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CurrencyTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrencyTransactionDto>> Update(
        Guid id,
        [FromBody] UpdateCurrencyTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var transaction = await _updateUseCase.ExecuteAsync(id, request, cancellationToken);
            if (transaction == null)
                return NotFound();
            return Ok(transaction);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Delete a currency transaction.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await _deleteUseCase.ExecuteAsync(id, cancellationToken);
            if (!deleted)
                return NotFound();
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
