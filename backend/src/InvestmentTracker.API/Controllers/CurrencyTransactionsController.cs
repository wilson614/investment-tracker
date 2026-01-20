using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.CurrencyTransactions;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供外幣交易（Currency Transaction）查詢與維護 API。
/// </summary>
/// <remarks>
/// 異常由 ExceptionHandlingMiddleware 統一處理：
/// - EntityNotFoundException → 404 Not Found
/// - AccessDeniedException → 403 Forbidden
/// - BusinessRuleException → 400 Bad Request
/// </remarks>
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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrencyTransactionDto>> Create(
        [FromBody] CreateCurrencyTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var transaction = await createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(
            nameof(GetByLedger),
            new { ledgerId = transaction.CurrencyLedgerId },
            transaction);
    }

    /// <summary>
    /// 更新外幣交易。
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CurrencyTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrencyTransactionDto>> Update(
        Guid id,
        [FromBody] UpdateCurrencyTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var transaction = await updateUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(transaction);
    }

    /// <summary>
    /// 刪除外幣交易。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}
