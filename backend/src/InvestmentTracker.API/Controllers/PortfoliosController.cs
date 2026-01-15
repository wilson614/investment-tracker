using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.Portfolio;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class PortfoliosController : ControllerBase
{
    private readonly IPortfolioRepository _portfolioRepository;
    private readonly GetPortfolioSummaryUseCase _getPortfolioSummaryUseCase;
    private readonly CalculateXirrUseCase _calculateXirrUseCase;
    private readonly ICurrentUserService _currentUserService;

    public PortfoliosController(
        IPortfolioRepository portfolioRepository,
        GetPortfolioSummaryUseCase getPortfolioSummaryUseCase,
        CalculateXirrUseCase calculateXirrUseCase,
        ICurrentUserService currentUserService)
    {
        _portfolioRepository = portfolioRepository;
        _getPortfolioSummaryUseCase = getPortfolioSummaryUseCase;
        _calculateXirrUseCase = calculateXirrUseCase;
        _currentUserService = currentUserService;
    }

    /// <summary>
    /// Get all portfolios for the current user.
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
    /// Get a portfolio by ID.
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
    /// Get portfolio summary with calculated positions.
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
            var summary = await _getPortfolioSummaryUseCase.ExecuteAsync(
                id, null, cancellationToken);
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
    /// Get portfolio summary with calculated positions and current prices.
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
            var summary = await _getPortfolioSummaryUseCase.ExecuteAsync(
                id, performanceRequest, cancellationToken);
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
    /// Calculate XIRR (Extended Internal Rate of Return) for a portfolio.
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
    /// Calculate XIRR for a single position (ticker).
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
    /// Create a new portfolio.
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
    /// Update a portfolio.
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
    /// Delete (deactivate) a portfolio.
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
