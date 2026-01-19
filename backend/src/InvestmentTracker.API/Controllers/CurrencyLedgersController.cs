using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.CurrencyLedger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供外幣帳本（Currency Ledger）查詢與維護 API。
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class CurrencyLedgersController(
    GetCurrencyLedgerSummaryUseCase getSummaryUseCase,
    CreateCurrencyLedgerUseCase createUseCase,
    UpdateCurrencyLedgerUseCase updateUseCase,
    DeleteCurrencyLedgerUseCase deleteUseCase) : ControllerBase
{
    private readonly GetCurrencyLedgerSummaryUseCase _getSummaryUseCase = getSummaryUseCase;
    private readonly CreateCurrencyLedgerUseCase _createUseCase = createUseCase;
    private readonly UpdateCurrencyLedgerUseCase _updateUseCase = updateUseCase;
    private readonly DeleteCurrencyLedgerUseCase _deleteUseCase = deleteUseCase;

    /// <summary>
    /// 取得目前使用者的所有外幣帳本。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<CurrencyLedgerSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CurrencyLedgerSummaryDto>>> GetAll(CancellationToken cancellationToken)
    {
        try
        {
            var ledgers = await _getSummaryUseCase.GetAllForUserAsync(cancellationToken);
            return Ok(ledgers);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 依外幣帳本 ID 取得外幣帳本摘要。
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CurrencyLedgerSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrencyLedgerSummaryDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _getSummaryUseCase.ExecuteAsync(id, cancellationToken);
            if (summary == null)
                return NotFound();
            return Ok(summary);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 建立新的外幣帳本。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(CurrencyLedgerDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CurrencyLedgerDto>> Create(
        [FromBody] CreateCurrencyLedgerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var ledger = await _createUseCase.ExecuteAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = ledger.Id }, ledger);
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
    /// 更新外幣帳本。
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(CurrencyLedgerDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CurrencyLedgerDto>> Update(
        Guid id,
        [FromBody] UpdateCurrencyLedgerRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var ledger = await _updateUseCase.ExecuteAsync(id, request, cancellationToken);
            if (ledger == null)
                return NotFound();
            return Ok(ledger);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 刪除（停用）外幣帳本。
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
