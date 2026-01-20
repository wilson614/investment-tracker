using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.CurrencyLedger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供外幣帳本（Currency Ledger）查詢與維護 API。
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
public class CurrencyLedgersController(
    GetCurrencyLedgerSummaryUseCase getSummaryUseCase,
    CreateCurrencyLedgerUseCase createUseCase,
    UpdateCurrencyLedgerUseCase updateUseCase,
    DeleteCurrencyLedgerUseCase deleteUseCase) : ControllerBase
{
    /// <summary>
    /// 取得目前使用者的所有外幣帳本。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CurrencyLedgerSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<CurrencyLedgerSummaryDto>>> GetAll(CancellationToken cancellationToken)
    {
        var ledgers = await getSummaryUseCase.GetAllForUserAsync(cancellationToken);
        return Ok(ledgers);
    }

    /// <summary>
    /// 依外幣帳本 ID 取得外幣帳本摘要。
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CurrencyLedgerSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrencyLedgerSummaryDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var summary = await getSummaryUseCase.ExecuteAsync(id, cancellationToken);
        return Ok(summary);
    }

    /// <summary>
    /// 建立新的外幣帳本。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CurrencyLedgerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrencyLedgerDto>> Create(
        [FromBody] CreateCurrencyLedgerRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = ledger.Id }, ledger);
    }

    /// <summary>
    /// 更新外幣帳本。
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CurrencyLedgerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CurrencyLedgerDto>> Update(
        Guid id,
        [FromBody] UpdateCurrencyLedgerRequest request,
        CancellationToken cancellationToken)
    {
        var ledger = await updateUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(ledger);
    }

    /// <summary>
    /// 刪除（停用）外幣帳本。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}
