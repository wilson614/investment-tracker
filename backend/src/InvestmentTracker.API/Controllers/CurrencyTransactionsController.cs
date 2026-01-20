using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.CurrencyTransactions;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供外幣交易（Currency Transaction）查詢與維護 API。
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CurrencyTransactionsController(
    CreateCurrencyTransactionUseCase createUseCase,
    UpdateCurrencyTransactionUseCase updateUseCase,
    DeleteCurrencyTransactionUseCase deleteUseCase,
    ICurrencyTransactionRepository transactionRepository) : ControllerBase
{
    /// <summary>
    /// 取得指定外幣帳本的所有交易。
    /// </summary>
    [HttpGet("ledger/{ledgerId:guid}")]
    [ProducesResponseType(typeof(IEnumerable<CurrencyTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CurrencyTransactionDto>>> GetByLedger(
        Guid ledgerId,
        CancellationToken cancellationToken)
    {
        var transactions = await transactionRepository.GetByLedgerIdOrderedAsync(ledgerId, cancellationToken);

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
    /// 建立新的外幣交易。
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
            var transaction = await createUseCase.ExecuteAsync(request, cancellationToken);
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
    /// 更新外幣交易。
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
            var transaction = await updateUseCase.ExecuteAsync(id, request, cancellationToken);
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
    /// 刪除外幣交易。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var deleted = await deleteUseCase.ExecuteAsync(id, cancellationToken);
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
