using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.Performance;
using InvestmentTracker.Application.UseCases.Portfolio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供投資組合（Portfolio）查詢、摘要與維護 API。
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PortfoliosController(
    GetPortfoliosUseCase getPortfoliosUseCase,
    GetPortfolioSummaryUseCase getPortfolioSummaryUseCase,
    CalculateXirrUseCase calculateXirrUseCase,
    CalculateAggregateXirrUseCase calculateAggregateXirrUseCase,
    GetAggregateAvailableYearsUseCase getAggregateAvailableYearsUseCase,
    CalculateAggregateYearPerformanceUseCase calculateAggregateYearPerformanceUseCase,
    CreatePortfolioUseCase createPortfolioUseCase,
    UpdatePortfolioUseCase updatePortfolioUseCase,
    DeletePortfolioUseCase deletePortfolioUseCase) : ControllerBase
{
    /// <summary>
    /// 取得目前使用者的所有投資組合。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PortfolioDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PortfolioDto>>> GetAll(CancellationToken cancellationToken)
    {
        var portfolios = await getPortfoliosUseCase.GetAllAsync(cancellationToken);
        return Ok(portfolios);
    }

    /// <summary>
    /// 依投資組合 ID 取得投資組合資料。
    /// </summary>
    /// <remarks>
    /// 異常由 ExceptionHandlingMiddleware 統一處理：
    /// - EntityNotFoundException → 404 Not Found
    /// - AccessDeniedException → 403 Forbidden
    /// </remarks>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PortfolioDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await getPortfoliosUseCase.GetByIdAsync(id, cancellationToken);
        return Ok(portfolio);
    }

    /// <summary>
    /// 取得投資組合摘要（含持倉計算）。
    /// </summary>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType(typeof(PortfolioSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioSummaryDto>> GetSummary(
        Guid id,
        CancellationToken cancellationToken)
    {
        var summary = await getPortfolioSummaryUseCase.ExecuteAsync(id, null, cancellationToken);
        return Ok(summary);
    }

    /// <summary>
    /// 取得投資組合摘要（含持倉計算與即時價格）。
    /// </summary>
    [HttpPost("{id:guid}/summary")]
    [ProducesResponseType(typeof(PortfolioSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioSummaryDto>> GetSummaryWithPrices(
        Guid id,
        [FromBody] CalculatePerformanceRequest? performanceRequest,
        CancellationToken cancellationToken)
    {
        var summary = await getPortfolioSummaryUseCase.ExecuteAsync(id, performanceRequest, cancellationToken);
        return Ok(summary);
    }

    /// <summary>
    /// 計算投資組合的 XIRR（Extended Internal Rate of Return）。
    /// </summary>
    [HttpPost("{id:guid}/xirr")]
    [ProducesResponseType(typeof(XirrResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<XirrResultDto>> CalculateXirr(
        Guid id,
        [FromBody] CalculateXirrRequest request,
        CancellationToken cancellationToken)
    {
        var result = await calculateXirrUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 計算目前使用者所有投資組合合併後的 XIRR。
    /// </summary>
    [HttpPost("aggregate/xirr")]
    [ProducesResponseType(typeof(XirrResultDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<XirrResultDto>> CalculateAggregateXirr(
        [FromBody] CalculateXirrRequest request,
        CancellationToken cancellationToken)
    {
        var result = await calculateAggregateXirrUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 取得目前使用者所有投資組合可用於績效計算的年度清單。
    /// </summary>
    [HttpGet("aggregate/performance/years")]
    [ProducesResponseType(typeof(AvailableYearsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AvailableYearsDto>> GetAggregateAvailableYears(
        CancellationToken cancellationToken)
    {
        var result = await getAggregateAvailableYearsUseCase.ExecuteAsync(cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 計算目前使用者所有投資組合合併後的指定年度績效。
    /// </summary>
    [HttpPost("aggregate/performance/year")]
    [ProducesResponseType(typeof(YearPerformanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<YearPerformanceDto>> CalculateAggregateYearPerformance(
        [FromBody] CalculateYearPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await calculateAggregateYearPerformanceUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 計算投資組合單一持倉（ticker）的 XIRR。
    /// </summary>
    [HttpPost("{id:guid}/positions/{ticker}/xirr")]
    [ProducesResponseType(typeof(XirrResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<XirrResultDto>> CalculatePositionXirr(
        Guid id,
        string ticker,
        [FromBody] CalculatePositionXirrRequest request,
        CancellationToken cancellationToken)
    {
        var result = await calculateXirrUseCase.ExecuteForPositionAsync(id, ticker, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 建立新的投資組合。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PortfolioDto>> Create(
        [FromBody] CreatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var dto = await createPortfolioUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// 更新投資組合。
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PortfolioDto>> Update(
        Guid id,
        [FromBody] UpdatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var dto = await updatePortfolioUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(dto);
    }

    /// <summary>
    /// 刪除（停用）投資組合。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await deletePortfolioUseCase.ExecuteAsync(id, cancellationToken);
        return NoContent();
    }
}
