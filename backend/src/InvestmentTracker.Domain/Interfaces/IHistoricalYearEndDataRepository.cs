using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// HistoricalYearEndData 快取相關操作的 Repository 介面。
/// 這是一個全域快取（非 per-user），用於保存年末股價與年末匯率。
/// </summary>
public interface IHistoricalYearEndDataRepository
{
    /// <summary>
    /// 依據資料類型、ticker 與年度取得年末快取資料。
    /// </summary>
    Task<HistoricalYearEndData?> GetAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得指定 ticker 與年度的年末股價快取。
    /// </summary>
    Task<HistoricalYearEndData?> GetStockPriceAsync(
        string ticker,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得指定幣別對與年度的年末匯率快取。
    /// </summary>
    Task<HistoricalYearEndData?> GetExchangeRateAsync(
        string currencyPair,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得指定年度的所有快取資料。
    /// </summary>
    Task<IReadOnlyList<HistoricalYearEndData>> GetByYearAsync(
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增一筆快取資料。若已存在則丟出例外（快取設計為不可變）。
    /// </summary>
    Task<HistoricalYearEndData> AddAsync(
        HistoricalYearEndData data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 檢查指定條件的快取資料是否存在。
    /// </summary>
    Task<bool> ExistsAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 刪除指定條件的快取資料。
    /// </summary>
    Task<bool> DeleteAsync(
        HistoricalDataType dataType,
        string ticker,
        int year,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 刪除指定 ticker 的所有年度快取資料。
    /// </summary>
    Task<int> DeleteByTickerAsync(
        string ticker,
        CancellationToken cancellationToken = default);
}
