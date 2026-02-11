using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Application.UseCases.CurrencyLedger;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
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
    DeleteCurrencyLedgerUseCase deleteUseCase,
    ICurrencyLedgerRepository currencyLedgerRepository,
    ICurrencyTransactionRepository currencyTransactionRepository,
    CurrencyLedgerService currencyLedgerService,
    ITransactionDateExchangeRateService txDateFxService,
    ICurrentUserService currentUserService) : ControllerBase
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
    /// 預覽指定金額與日期的買股匯率（LIFO/市場/混合）。
    /// </summary>
    [HttpGet("{id:guid}/exchange-rate-preview")]
    [ProducesResponseType(typeof(ExchangeRatePreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ExchangeRatePreviewResponse>> GetExchangeRatePreview(
        Guid id,
        [FromQuery] decimal amount,
        [FromQuery] DateTime date,
        CancellationToken cancellationToken)
    {
        if (amount <= 0)
            return BadRequest("amount must be greater than 0");

        var currentUserId = currentUserService.UserId;
        if (!currentUserId.HasValue)
            return NotFound();

        var ledger = await currencyLedgerRepository.GetByIdAsync(id, cancellationToken);
        if (ledger == null || ledger.UserId != currentUserId.Value)
            return NotFound();

        var ledgerTransactions = await currencyTransactionRepository.GetByLedgerIdOrderedAsync(id, cancellationToken);
        var balance = currencyLedgerService.CalculateBalance(ledgerTransactions);
        var lifoRate = currencyLedgerService.CalculateExchangeRateForPurchase(ledgerTransactions, date, amount);

        decimal? marketRate = null;
        if (ledger.CurrencyCode != ledger.HomeCurrency)
        {
            var fxResult = await txDateFxService.GetOrFetchAsync(
                ledger.CurrencyCode,
                ledger.HomeCurrency,
                date,
                cancellationToken);
            marketRate = fxResult?.Rate;
        }

        string source;
        decimal rate;
        decimal? lifoPortion = null;
        decimal? marketPortion = null;

        if (lifoRate > 0 && balance >= amount)
        {
            source = "lifo";
            rate = lifoRate;
            lifoPortion = amount;
            marketPortion = 0m;
        }
        else if (lifoRate > 0 && balance > 0 && balance < amount)
        {
            if (!marketRate.HasValue)
            {
                return UnprocessableEntity(new
                {
                    error = "ExchangeRateUnavailable",
                    message = "無法計算匯率。請先在帳本中建立換匯紀錄。"
                });
            }

            source = "blended";
            rate = currencyLedgerService.CalculateExchangeRateWithMargin(
                ledgerTransactions,
                date,
                amount,
                balance,
                marketRate.Value);
            lifoPortion = balance;
            marketPortion = amount - balance;
        }
        else if ((lifoRate <= 0 || balance <= 0) && marketRate.HasValue)
        {
            source = "market";
            rate = marketRate.Value;
            lifoPortion = 0m;
            marketPortion = amount;
        }
        else
        {
            return UnprocessableEntity(new
            {
                error = "ExchangeRateUnavailable",
                message = "無法計算匯率。請先在帳本中建立換匯紀錄。"
            });
        }

        var response = new ExchangeRatePreviewResponse
        {
            Rate = rate,
            Source = source,
            LifoRate = lifoRate > 0 ? lifoRate : null,
            MarketRate = marketRate,
            LifoPortion = lifoPortion,
            MarketPortion = marketPortion
        };

        return Ok(response);
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
