using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// BenchmarkAnnualReturn 快取資料的 Repository 介面。
/// 這是一個全域快取（非 per-user），用於保存歷史年度的 Total Return。
/// </summary>
public interface IBenchmarkAnnualReturnRepository
{
    Task<BenchmarkAnnualReturn?> GetAsync(
        string symbol,
        int year,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
        string symbol,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增一筆快取資料。
    /// 若已存在則回傳既有資料（避免並發寫入造成錯誤）。
    /// </summary>
    Task<BenchmarkAnnualReturn> AddAsync(
        BenchmarkAnnualReturn data,
        CancellationToken cancellationToken = default);
}
