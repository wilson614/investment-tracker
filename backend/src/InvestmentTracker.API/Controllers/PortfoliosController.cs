using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.Portfolio;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
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
    IPortfolioRepository portfolioRepository,
    GetPortfolioSummaryUseCase getPortfolioSummaryUseCase,
    CalculateXirrUseCase calculateXirrUseCase,
    ICurrentUserService currentUserService) : ControllerBase
{
    private readonly IPortfolioRepository _portfolioRepository = portfolioRepository;
    private readonly GetPortfolioSummaryUseCase _getPortfolioSummaryUseCase = getPortfolioSummaryUseCase;
    private readonly CalculateXirrUseCase _calculateXirrUseCase = calculateXirrUseCase;
    private readonly ICurrentUserService _currentUserService = currentUserService;

    /// <summary>
    /// 取得目前使用者的所有投資組合。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<PortfolioDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<PortfolioDto>>> GetAll(CancellationToken cancellationToken)
    {
        var portfolios = await _portfolioRepository.GetByUserIdAsync(
            _currentUserService.UserId!.Value, cancellationToken);

        return Ok(portfolios.Select(p => new PortfolioDto
        {
            Id = p.Id,
            Description = p.Description,
            BaseCurrency = p.BaseCurrency,
            HomeCurrency = p.HomeCurrency,
            IsActive = p.IsActive,
            PortfolioType = p.PortfolioType,
            DisplayName = p.DisplayName,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        }));
    }

    /// <summary>
    /// 依投資組合 ID 取得投資組合資料。
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetByIdAsync(id, cancellationToken);

        if (portfolio == null)
            return NotFound();

        if (portfolio.UserId != _currentUserService.UserId)
            return Forbid();

        return Ok(new PortfolioDto
        {
            Id = portfolio.Id,
            Description = portfolio.Description,
            BaseCurrency = portfolio.BaseCurrency,
            HomeCurrency = portfolio.HomeCurrency,
            IsActive = portfolio.IsActive,
            PortfolioType = portfolio.PortfolioType,
            DisplayName = portfolio.DisplayName,
            CreatedAt = portfolio.CreatedAt,
            UpdatedAt = portfolio.UpdatedAt
        });
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
        try
        {
            var summary = await _getPortfolioSummaryUseCase.ExecuteAsync(id, null, cancellationToken);
            return Ok(summary);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
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
        try
        {
            var summary = await _getPortfolioSummaryUseCase.ExecuteAsync(id, performanceRequest, cancellationToken);
            return Ok(summary);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
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
        try
        {
            var result = await _calculateXirrUseCase.ExecuteAsync(id, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
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
        try
        {
            var result = await _calculateXirrUseCase.ExecuteForPositionAsync(id, ticker, request, cancellationToken);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
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
        var portfolio = new Portfolio(
            _currentUserService.UserId!.Value,
            request.BaseCurrency,
            request.HomeCurrency,
            request.PortfolioType,
            request.DisplayName);

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            portfolio.SetDescription(request.Description);
        }

        await _portfolioRepository.AddAsync(portfolio, cancellationToken);

        var dto = new PortfolioDto
        {
            Id = portfolio.Id,
            Description = portfolio.Description,
            BaseCurrency = portfolio.BaseCurrency,
            HomeCurrency = portfolio.HomeCurrency,
            IsActive = portfolio.IsActive,
            PortfolioType = portfolio.PortfolioType,
            DisplayName = portfolio.DisplayName,
            CreatedAt = portfolio.CreatedAt,
            UpdatedAt = portfolio.UpdatedAt
        };

        return CreatedAtAction(nameof(GetById), new { id = portfolio.Id }, dto);
    }

    /// <summary>
    /// 更新投資組合。
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(PortfolioDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PortfolioDto>> Update(
        Guid id,
        [FromBody] UpdatePortfolioRequest request,
        CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetByIdAsync(id, cancellationToken);

        if (portfolio == null)
            return NotFound();

        if (portfolio.UserId != _currentUserService.UserId)
            return Forbid();

        portfolio.SetDescription(request.Description);

        await _portfolioRepository.UpdateAsync(portfolio, cancellationToken);

        return Ok(new PortfolioDto
        {
            Id = portfolio.Id,
            Description = portfolio.Description,
            BaseCurrency = portfolio.BaseCurrency,
            HomeCurrency = portfolio.HomeCurrency,
            IsActive = portfolio.IsActive,
            PortfolioType = portfolio.PortfolioType,
            DisplayName = portfolio.DisplayName,
            CreatedAt = portfolio.CreatedAt,
            UpdatedAt = portfolio.UpdatedAt
        });
    }

    /// <summary>
    /// 刪除（停用）投資組合。
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var portfolio = await _portfolioRepository.GetByIdAsync(id, cancellationToken);

        if (portfolio == null)
            return NotFound();

        if (portfolio.UserId != _currentUserService.UserId)
            return Forbid();

        await _portfolioRepository.DeleteAsync(id, cancellationToken);

        return NoContent();
    }
}
