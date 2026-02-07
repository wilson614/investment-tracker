using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供股票交易（Stock Transaction）查詢與維護 API。
/// </summary>
[Authorize]
[ApiController]
[Route("api/[controller]")]
public class StockTransactionsController(
    IStockTransactionRepository transactionRepository,
    IStockSplitRepository stockSplitRepository,
    StockSplitAdjustmentService splitAdjustmentService,
    CreateStockTransactionUseCase createUseCase,
    UpdateStockTransactionUseCase updateUseCase,
    DeleteStockTransactionUseCase deleteUseCase) : ControllerBase
{
    /// <summary>
    /// 取得指定投資組合的所有交易。
    /// 包含分割調整後的欄位，便於追蹤（FR-052a）。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<StockTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<StockTransactionDto>>> GetByPortfolio(
        [FromQuery] Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var transactions = await transactionRepository.GetByPortfolioIdAsync(portfolioId, cancellationToken);
        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);
        var splitList = stockSplits.ToList();

        return Ok(transactions.Select(t =>
        {
            var adjusted = splitAdjustmentService.GetAdjustedValues(t, splitList);
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
                CurrencyLedgerId = t.CurrencyLedgerId,
                Notes = t.Notes,
                TotalCostSource = t.TotalCostSource,
                TotalCostHome = t.TotalCostHome,
                HasExchangeRate = t.HasExchangeRate,
                RealizedPnlHome = t.RealizedPnlHome,
                CreatedAt = t.CreatedAt,
                UpdatedAt = t.UpdatedAt,
                // 分割調整欄位
                AdjustedShares = adjusted.HasSplitAdjustment ? adjusted.AdjustedShares : null,
                AdjustedPricePerShare = adjusted.HasSplitAdjustment ? adjusted.AdjustedPrice : null,
                SplitRatio = adjusted.SplitRatio,
                HasSplitAdjustment = adjusted.HasSplitAdjustment,
                Market = t.Market,
                Currency = t.Currency
            };
        }));
    }

    /// <summary>
    /// 依交易 ID 取得單筆交易。
    /// 包含分割調整後的欄位，便於追蹤（FR-052a）。
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockTransactionDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var transaction = await transactionRepository.GetByIdAsync(id, cancellationToken);

        if (transaction == null)
            return NotFound();

        var stockSplits = await stockSplitRepository.GetAllAsync(cancellationToken);
        var adjusted = splitAdjustmentService.GetAdjustedValues(transaction, stockSplits);

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
            CurrencyLedgerId = transaction.CurrencyLedgerId,
            Notes = transaction.Notes,
            TotalCostSource = transaction.TotalCostSource,
            TotalCostHome = transaction.TotalCostHome,
            HasExchangeRate = transaction.HasExchangeRate,
            RealizedPnlHome = transaction.RealizedPnlHome,
            CreatedAt = transaction.CreatedAt,
            UpdatedAt = transaction.UpdatedAt,
            // 分割調整欄位
            AdjustedShares = adjusted.HasSplitAdjustment ? adjusted.AdjustedShares : null,
            AdjustedPricePerShare = adjusted.HasSplitAdjustment ? adjusted.AdjustedPrice : null,
            SplitRatio = adjusted.SplitRatio,
            HasSplitAdjustment = adjusted.HasSplitAdjustment,
            Market = transaction.Market,
            Currency = transaction.Currency
        });
    }

    /// <summary>
    /// 建立新的股票交易。
    /// </summary>
    /// <remarks>
    /// 異常由 ExceptionHandlingMiddleware 統一處理：
    /// - EntityNotFoundException → 404 Not Found
    /// - AccessDeniedException → 403 Forbidden
    /// - BusinessRuleException → 400 Bad Request
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StockTransactionDto>> Create(
        [FromBody] CreateStockTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createUseCase.ExecuteAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新股票交易。
    /// </summary>
    /// <remarks>
    /// 異常由 ExceptionHandlingMiddleware 統一處理。
    /// </remarks>
    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(StockTransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StockTransactionDto>> Update(
        Guid id,
        [FromBody] UpdateStockTransactionRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateUseCase.ExecuteAsync(id, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 刪除（soft-delete）股票交易。
    /// </summary>
    /// <remarks>
    /// 異常由 ExceptionHandlingMiddleware 統一處理。
    /// </remarks>
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
