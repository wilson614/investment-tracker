using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.StockSplits;
using InvestmentTracker.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供股票分割（Stock Split）資料查詢與維護 API。
/// </summary>
/// <remarks>
/// 異常由 ExceptionHandlingMiddleware 統一處理：
/// - EntityNotFoundException → 404 Not Found
/// - BusinessRuleException → 400 Bad Request
/// </remarks>
[Authorize]
[ApiController]
[Route("api/stock-splits")]
public class StockSplitsController(
    GetStockSplitsUseCase getStockSplitsUseCase,
    CreateStockSplitUseCase createStockSplitUseCase,
    UpdateStockSplitUseCase updateStockSplitUseCase,
    DeleteStockSplitUseCase deleteStockSplitUseCase) : ControllerBase
{
    /// <summary>
    /// 取得所有股票分割資料。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<StockSplitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StockSplitDto>>> GetAll(CancellationToken cancellationToken)
    {
        var splits = await getStockSplitsUseCase.GetAllAsync(cancellationToken);
        return Ok(splits);
    }

    /// <summary>
    /// 依股票分割 ID 取得股票分割資料。
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StockSplitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockSplitDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var split = await getStockSplitsUseCase.GetByIdAsync(id, cancellationToken);
        if (split == null)
            return NotFound();

        return Ok(split);
    }

    /// <summary>
    /// 依股票代號與市場查詢股票分割資料。
    /// </summary>
    [HttpGet("by-symbol")]
    [ProducesResponseType(typeof(IEnumerable<StockSplitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StockSplitDto>>> GetBySymbol(
        [FromQuery] string symbol,
        [FromQuery] StockMarket market,
        CancellationToken cancellationToken)
    {
        var splits = await getStockSplitsUseCase.GetBySymbolAsync(symbol, market, cancellationToken);
        return Ok(splits);
    }

    /// <summary>
    /// 建立新的股票分割紀錄。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StockSplitDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockSplitDto>> Create(
        [FromBody] CreateStockSplitRequest request,
        CancellationToken cancellationToken)
    {
        var split = await createStockSplitUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = split.Id }, split);
    }

    /// <summary>
    /// 更新既有的股票分割紀錄。
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(StockSplitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockSplitDto>> Update(
        Guid id,
        [FromBody] UpdateStockSplitRequest request,
        CancellationToken cancellationToken)
    {
        var split = await updateStockSplitUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(split);
    }

    /// <summary>
    /// 刪除股票分割紀錄。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deleteStockSplitUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}
