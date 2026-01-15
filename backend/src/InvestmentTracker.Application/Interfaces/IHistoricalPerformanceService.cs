using InvestmentTracker.Application.DTOs;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// Service for calculating historical year performance.
/// </summary>
public interface IHistoricalPerformanceService
{
    /// <summary>
    /// Get available years for performance calculation.
    /// </summary>
    Task<AvailableYearsDto> GetAvailableYearsAsync(
        Guid portfolioId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Calculate performance for a specific year.
    /// </summary>
    Task<YearPerformanceDto> CalculateYearPerformanceAsync(
        Guid portfolioId,
        CalculateYearPerformanceRequest request,
        CancellationToken cancellationToken = default);
}
