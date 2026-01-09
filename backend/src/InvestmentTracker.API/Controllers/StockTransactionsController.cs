using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StockTransactionsController : ControllerBase
{
    private readonly IStockTransactionRepository _transactionRepository;
    private readonly IStockSplitRepository _stockSplitRepository;
    private readonly StockSplitAdjustmentService _splitAdjustmentService;
    private readonly CreateStockTransactionUseCase _createUseCase;
    private readonly UpdateStockTransactionUseCase _updateUseCase;
    private readonly DeleteStockTransactionUseCase _deleteUseCase;

    public StockTransactionsController(
        IStockTransactionRepository transactionRepository,
        IStockSplitRepository stockSplitRepository,
        StockSplitAdjustmentService splitAdjustmentService,
        CreateStockTransactionUseCase createUseCase,
        UpdateStockTransactionUseCase updateUseCase,
        DeleteStockTransactionUseCase deleteUseCase)
    {
        _transactionRepository = transactionRepository;
        _stockSplitRepository = stockSplitRepository;
        _splitAdjustmentService = splitAdjustmentService;
        _createUseCase = createUseCase;
        _updateUseCase = updateUseCase;
        _deleteUseCase = deleteUseCase;
    }

    /// <summary>
    /// Get all transactions for a portfolio.
    /// Includes split-adjusted values for transparency (FR-052a).
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<StockTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StockTransactionDto>>> GetByPortfolio(
        [FromQuery] Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var transactions = await _transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await _stockSplitRepository.GetAllAsync(cancellationToken);
        var splitList = stockSplits.ToList();

        return Ok(transactions.Select(t =>
        {
            var adjusted = _splitAdjustmentService.GetAdjustedValues(t, splitList);
            return new StockTransactionDto
            {
                Id = t.Id,
                PortfolioId = t.PortfolioId,
                TransactionDate = t.TransactionDate,
                Ticker = t.Ticker,
                TransactionType = t.TransactionType,
                Shares = t.Shares,
                PricePerShare = t.PricePerShare,
                ExchangeRate = t.ExchangeRate,
                Fees = t.Fees,
                FundSource = t.FundSource,
                CurrencyLedgerId = t.CurrencyLedgerId,
                Notes = t.Notes,
                TotalCostSource = t.TotalCostSource,
                TotalCostHome = t.TotalCostHome,
                RealizedPnlHome = t.RealizedPnlHome,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                // Split adjustment fields
                AdjustedShares = adjusted.HasSplitAdjustment ? adjusted.AdjustedShares : null,
                AdjustedPricePerShare = adjusted.HasSplitAdjustment ? adjusted.AdjustedPrice : null,
                SplitRatio = adjusted.SplitRatio,
                HasSplitAdjustment = adjusted.HasSplitAdjustment
            };
        }));
    }

    /// <summary>
    /// Get a transaction by ID.
    /// Includes split-adjusted values for transparency (FR-052a).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockTransactionDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await _transactionRepository.GetByIdAsync(id, cancellationToken);

        if (transaction == null)
            return NotFound();

        var stockSplits = await _stockSplitRepository.GetAllAsync(cancellationToken);
        var adjusted = _splitAdjustmentService.GetAdjustedValues(transaction, stockSplits);

        return Ok(new StockTransactionDto
        {
            Id = transaction.Id,
            PortfolioId = transaction.PortfolioId,
            TransactionDate = transaction.TransactionDate,
            Ticker = transaction.Ticker,
            TransactionType = transaction.TransactionType,
            Shares = transaction.Shares,
            PricePerShare = transaction.PricePerShare,
            ExchangeRate = transaction.ExchangeRate,
            Fees = transaction.Fees,
            FundSource = transaction.FundSource,
            CurrencyLedgerId = transaction.CurrencyLedgerId,
            Notes = transaction.Notes,
            TotalCostSource = transaction.TotalCostSource,
            TotalCostHome = transaction.TotalCostHome,
            RealizedPnlHome = transaction.RealizedPnlHome,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt,
            // Split adjustment fields
            AdjustedShares = adjusted.HasSplitAdjustment ? adjusted.AdjustedShares : null,
            AdjustedPricePerShare = adjusted.HasSplitAdjustment ? adjusted.AdjustedPrice : null,
            SplitRatio = adjusted.SplitRatio,
            HasSplitAdjustment = adjusted.HasSplitAdjustment
        });
    }

    /// <summary>
    /// Create a new stock transaction.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<StockTransactionDto>> Create(
        [FromBody] CreateStockTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _createUseCase.ExecuteAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
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
    /// Update a stock transaction.
    /// </summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockTransactionDto>> Update(
        Guid id,
        [FromBody] UpdateStockTransactionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _updateUseCase.ExecuteAsync(id, request, cancellationToken);
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
    /// Delete (soft-delete) a stock transaction.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _deleteUseCase.ExecuteAsync(id, cancellationToken);
            return NoContent();
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
}
