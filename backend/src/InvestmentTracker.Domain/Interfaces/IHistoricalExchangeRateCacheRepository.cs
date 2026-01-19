using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// HistoricalExchangeRateCache 相關操作的 Repository 介面。
/// 這是一個全域快取（非 per-user），用於保存「交易日期」的匯率資料。
/// </summary>
public interface IHistoricalExchangeRateCacheRepository
{
    /// <summary>
    /// 依據幣別對與日期取得快取的匯率資料。
    /// </summary>
    Task<HistoricalExchangeRateCache?> GetAsync(
        string currencyPair,
        DateTime requestedDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 依據 from/to 幣別與日期取得快取的匯率資料。
    /// </summary>
    Task<HistoricalExchangeRateCache?> GetAsync(
        string fromCurrency,
        string toCurrency,
        DateTime requestedDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得指定幣別對的所有快取資料。
    /// </summary>
    Task<IReadOnlyList<HistoricalExchangeRateCache>> GetByCurrencyPairAsync(
        string currencyPair,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增一筆快取資料。若已存在則丟出例外（快取設計為不可變）。
    /// </summary>
    Task<HistoricalExchangeRateCache> AddAsync(
        HistoricalExchangeRateCache data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 檢查指定條件的快取資料是否存在。
    /// </summary>
    Task<bool> ExistsAsync(
        string currencyPair,
        DateTime requestedDate,
        CancellationToken cancellationToken = default);
}
