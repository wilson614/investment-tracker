using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// 月度淨值快取與查詢服務。
/// 會以「每月第一天」作為 Month key，並以月底（或當月截至今日）作為估值日期。
/// </summary>
public interface IMonthlySnapshotService
{
    Task<MonthlyNetWorthHistoryDto> GetMonthlyNetWorthAsync(
        Guid portfolioId,
        DateOnly? fromMonth = null,
        DateOnly? toMonth = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 使指定月份（含）之後的月度快取失效。
    /// </summary>
    Task InvalidateFromMonthAsync(
        Guid portfolioId,
        DateOnly fromMonth,
        CancellationToken cancellationToken = default);
}
