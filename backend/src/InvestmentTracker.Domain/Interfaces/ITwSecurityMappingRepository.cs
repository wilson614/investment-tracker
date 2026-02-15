using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// 台股證券名稱/代號對照的 Repository 介面。
/// </summary>
public interface ITwSecurityMappingRepository
{
    /// <summary>
    /// 依股票代碼取得映射資料（Ticker 會以 Trim + UpperInvariant 比對）。
    /// </summary>
    Task<TwSecurityMapping?> GetByTickerAsync(string ticker, CancellationToken cancellationToken = default);

    /// <summary>
    /// 依證券名稱取得所有候選映射（名稱會先做 matching normalization）。
    /// </summary>
    Task<IReadOnlyList<TwSecurityMapping>> GetBySecurityNameAsync(string securityName, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批次依證券名稱查詢映射（輸入名稱會先做 matching normalization）。
    /// </summary>
    Task<IReadOnlyList<TwSecurityMapping>> GetBySecurityNamesAsync(IEnumerable<string> securityNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 Ticker 為鍵新增或更新映射。
    /// </summary>
    Task UpsertAsync(TwSecurityMapping mapping, CancellationToken cancellationToken = default);
}
