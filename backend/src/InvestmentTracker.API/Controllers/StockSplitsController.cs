using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.StockSplits;
using InvestmentTracker.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

[Authorize]
[ApiController]
[Route("api/stock-splits")]
public class StockSplitsController : ControllerBase
{
    private readonly GetStockSplitsUseCase _getStockSplitsUseCase;
    private readonly CreateStockSplitUseCase _createStockSplitUseCase;
    private readonly UpdateStockSplitUseCase _updateStockSplitUseCase;
    private readonly DeleteStockSplitUseCase _deleteStockSplitUseCase;

    public StockSplitsController(
        GetStockSplitsUseCase getStockSplitsUseCase,
        CreateStockSplitUseCase createStockSplitUseCase,
        UpdateStockSplitUseCase updateStockSplitUseCase,
        DeleteStockSplitUseCase deleteStockSplitUseCase)
    {
        _getStockSplitsUseCase = getStockSplitsUseCase;
        _createStockSplitUseCase = createStockSplitUseCase;
        _updateStockSplitUseCase = updateStockSplitUseCase;
        _deleteStockSplitUseCase = deleteStockSplitUseCase;
    }

    /// <summary>
    /// Get all stock splits.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<StockSplitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StockSplitDto>>> GetAll(CancellationToken cancellationToken)
    {
        var splits = await _getStockSplitsUseCase.GetAllAsync(cancellationToken);
        return Ok(splits);
    }

    /// <summary>
    /// Get a stock split by ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StockSplitDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockSplitDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var split = await _getStockSplitsUseCase.GetByIdAsync(id, cancellationToken);
        if (split == null)
            return NotFound();

        return Ok(split);
    }

    /// <summary>
    /// Get stock splits for a specific symbol and market.
    /// </summary>
    [HttpGet("by-symbol")]
    [ProducesResponseType(typeof(IEnumerable<StockSplitDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StockSplitDto>>> GetBySymbol(
        [FromQuery] string symbol,
        [FromQuery] StockMarket market,
        CancellationToken cancellationToken)
    {
        var splits = await _getStockSplitsUseCase.GetBySymbolAsync(symbol, market, cancellationToken);
        return Ok(splits);
    }

    /// <summary>
    /// Create a new stock split record.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StockSplitDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockSplitDto>> Create(
        [FromBody] CreateStockSplitRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var split = await _createStockSplitUseCase.ExecuteAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = split.Id }, split);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Update an existing stock split record.
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
        try
        {
            var split = await _updateStockSplitUseCase.ExecuteAsync(id, request, cancellationToken);
            return Ok(split);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Delete a stock split record.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _deleteStockSplitUseCase.ExecuteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }
}
