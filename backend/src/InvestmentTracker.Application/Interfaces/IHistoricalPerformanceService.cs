using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// 歷史年度績效計算服務介面。
/// </summary>
public interface IHistoricalPerformanceService
{
    /// <summary>
    /// 取得可計算績效的年份清單。
    /// </summary>
    Task<AvailableYearsDto> GetAvailableYearsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 計算指定年度的績效。
    /// </summary>
    Task<YearPerformanceDto> CalculateYearPerformanceAsync(
        Guid portfolioId,
        CalculateYearPerformanceRequest request,
        CancellationToken cancellationToken = default);
}
