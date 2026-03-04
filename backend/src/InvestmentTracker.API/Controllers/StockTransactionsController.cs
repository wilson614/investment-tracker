using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.API.Middleware;
using InvestmentTracker.Application.UseCases.StockTransactions;
using InvestmentTracker.Domain.Exceptions;
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
    IPortfolioRepository portfolioRepository,
    ICurrentUserService currentUserService,
    StockSplitAdjustmentService splitAdjustmentService,
    CreateStockTransactionUseCase createUseCase,
    UpdateStockTransactionUseCase updateUseCase,
    DeleteStockTransactionUseCase deleteUseCase,
    IPreviewStockImportUseCase previewStockImportUseCase,
    IExecuteStockImportUseCase executeStockImportUseCase,
    IQueryStockImportSessionUseCase queryStockImportSessionUseCase) : ControllerBase
{
    /// <summary>
    /// 取得指定投資組合的所有交易。
    /// 包含分割調整後的欄位，便於追蹤（FR-052a）。
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<StockTransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<StockTransactionDto>>> GetByPortfolio(
        [FromQuery] Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var portfolio = await portfolioRepository.GetByIdAsync(portfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", portfolioId);

        if (portfolio.UserId != userId)
            throw new AccessDeniedException();

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
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockTransactionDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var transaction = await transactionRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("Transaction", id);

        var portfolio = await portfolioRepository.GetByIdAsync(transaction.PortfolioId, cancellationToken)
            ?? throw new EntityNotFoundException("Portfolio", transaction.PortfolioId);

        if (portfolio.UserId != userId)
            throw new AccessDeniedException();

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
    /// 預覽股票 CSV 匯入結果（legacy_csv / broker_statement）。
    /// </summary>
    /// <remarks>
    /// <para>用途：解析 CSV 並回傳可執行的 session snapshot；此端點不會寫入交易資料。</para>
    /// <para><b>成功回應範例（200）</b></para>
    /// <code>
    /// {
    ///   "sessionId": "9f45a39b-1cc7-4f3f-b2c4-73a8f15f2d01",
    ///   "detectedFormat": "broker_statement",
    ///   "selectedFormat": "broker_statement",
    ///   "summary": {
    ///     "totalRows": 2,
    ///     "validRows": 1,
    ///     "requiresActionRows": 1,
    ///     "invalidRows": 0
    ///   },
    ///   "rows": [
    ///     {
    ///       "rowNumber": 1,
    ///       "tradeDate": "2026-01-22T00:00:00Z",
    ///       "ticker": "2330",
    ///       "tradeSide": "buy",
    ///       "confirmedTradeSide": "buy",
    ///       "quantity": 1000,
    ///       "unitPrice": 625,
    ///       "fees": 1425,
    ///       "taxes": 0,
    ///       "status": "valid",
    ///       "actionsRequired": []
    ///     }
    ///   ],
    ///   "errors": []
    /// }
    /// </code>
    /// <para><b>失敗回應範例（400，模型驗證失敗）</b></para>
    /// <code>
    /// {
    ///   "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    ///   "title": "One or more validation errors occurred.",
    ///   "status": 400,
    ///   "errors": {
    ///     "SelectedFormat": [
    ///       "SelectedFormat must be either 'legacy_csv' or 'broker_statement'."
    ///     ]
    ///   }
    /// }
    /// </code>
    /// </remarks>
    [HttpPost("import/preview")]
    [ProducesResponseType(typeof(StockImportPreviewResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockImportPreviewResponseDto>> PreviewImport(
        [FromBody] PreviewStockImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await previewStockImportUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 執行股票 CSV 匯入（最小可用垂直切片）。
    /// </summary>
    /// <remarks>
    /// <para>用途：依 preview session 執行逐列匯入，回傳 committed 或 rejected。</para>
    /// <para><b>成功回應範例（200）</b></para>
    /// <code>
    /// {
    ///   "status": "committed",
    ///   "summary": {
    ///     "totalRows": 2,
    ///     "insertedRows": 2,
    ///     "failedRows": 0,
    ///     "errorCount": 0
    ///   },
    ///   "results": [
    ///     {
    ///       "rowNumber": 1,
    ///       "success": true,
    ///       "transactionId": "7fd4efab-1f8d-4754-9028-0d4708b18873",
    ///       "message": "Created",
    ///       "confirmedTradeSide": "buy"
    ///     }
    ///   ],
    ///   "errors": []
    /// }
    /// </code>
    /// <para><b>失敗回應範例（400，Session 已失效）</b></para>
    /// <code>
    /// {
    ///   "error": "Import session not found, expired, or already consumed.",
    ///   "statusCode": 400,
    ///   "timestamp": "2026-02-16T09:30:00Z"
    /// }
    /// </code>
    /// <para><b>失敗回應範例（400，模型驗證失敗）</b></para>
    /// <code>
    /// {
    ///   "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
    ///   "title": "One or more validation errors occurred.",
    ///   "status": 400,
    ///   "errors": {
    ///     "Rows": [
    ///       "Rows.RowNumber contains duplicates: 1"
    ///     ]
    ///   }
    /// }
    /// </code>
    /// </remarks>
    [HttpPost("import/execute")]
    [ProducesResponseType(typeof(StockImportExecuteResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockImportExecuteResponseDto>> ExecuteImport(
        [FromBody] ExecuteStockImportRequest request,
        CancellationToken cancellationToken)
    {
        var result = await executeStockImportUseCase.ExecuteAsync(request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 查詢指定匯入 Session 的執行狀態與最後結果。
    /// </summary>
    [HttpGet("import/status/{sessionId:guid}")]
    [ProducesResponseType(typeof(StockImportExecuteStatusResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<StockImportExecuteStatusResponseDto>> GetImportStatus(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var result = await queryStockImportSessionUseCase.ExecuteAsync(sessionId, cancellationToken);
        return Ok(result);
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
