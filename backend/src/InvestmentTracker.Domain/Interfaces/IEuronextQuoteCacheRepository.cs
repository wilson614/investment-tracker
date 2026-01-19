using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Euronext 報價快取相關操作的 Repository 介面。
/// </summary>
public interface IEuronextQuoteCacheRepository
{
    /// <summary>
    /// 依據 ISIN 與 MIC 取得快取的報價資料。
    /// </summary>
    Task<EuronextQuoteCache?> GetByIsinAndMicAsync(string isin, string mic, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或更新報價快取資料。
    /// </summary>
    Task UpsertAsync(EuronextQuoteCache quoteCache, CancellationToken cancellationToken = default);

    /// <summary>
    /// 將報價標記為過期。
    /// </summary>
    Task MarkAsStaleAsync(string isin, string mic, CancellationToken cancellationToken = default);
}
