using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InvestmentTracker.API.Controllers;

/// <summary>
/// 提供投資組合績效分析（Performance）相關 API。
/// </summary>
/// <remarks>
/// 異常由 ExceptionHandlingMiddleware 統一處理：
/// - EntityNotFoundException → 404 Not Found
/// - AccessDeniedException → 403 Forbidden
/// </remarks>
[ApiController]
[Route("api/portfolios/{portfolioId:guid}/performance")]
[Authorize]
public class PerformanceController(
    IHistoricalPerformanceService performanceService,
    IMonthlySnapshotService monthlySnapshotService) : ControllerBase
{
    /// <summary>
    /// 取得可用於績效計算的年度清單（有交易資料的年份）。
    /// </summary>
    [HttpGet("years")]
    [ProducesResponseType(typeof(AvailableYearsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AvailableYearsDto>> GetAvailableYears(
        Guid portfolioId,
        CancellationToken cancellationToken)
    {
        var result = await performanceService.GetAvailableYearsAsync(portfolioId, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 計算指定年度的投資組合績效。
    /// </summary>
    [HttpPost("year")]
    [ProducesResponseType(typeof(YearPerformanceDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<YearPerformanceDto>> CalculateYearPerformance(
        Guid portfolioId,
        [FromBody] CalculateYearPerformanceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await performanceService.CalculateYearPerformanceAsync(
            portfolioId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// 取得投資組合月度淨值歷史資料（用於 Dashboard 折線圖）。
    /// </summary>
    [HttpGet("monthly")]
    [ProducesResponseType(typeof(MonthlyNetWorthHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<MonthlyNetWorthHistoryDto>> GetMonthlyNetWorth(
        Guid portfolioId,
        [FromQuery] string? fromMonth,
        [FromQuery] string? toMonth,
        CancellationToken cancellationToken)
    {
        DateOnly? from = ParseMonth(fromMonth);
        DateOnly? to = ParseMonth(toMonth);

        var result = await monthlySnapshotService.GetMonthlyNetWorthAsync(
            portfolioId, from, to, cancellationToken);

        return Ok(result);
    }

    private static DateOnly? ParseMonth(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!DateOnly.TryParseExact(value.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return null;

        return new DateOnly(parsed.Year, parsed.Month, 1);
    }
}
