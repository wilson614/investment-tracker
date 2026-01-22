using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// Euronext 股票代碼對應的 Repository 介面。
/// </summary>
public interface IEuronextSymbolMappingRepository
{
    /// <summary>
    /// 依 ticker 取得對應資料。
    /// </summary>
    Task<EuronextSymbolMapping?> GetByTickerAsync(string ticker, CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或更新對應資料。
    /// </summary>
    Task UpsertAsync(EuronextSymbolMapping mapping, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得所有對應資料。
    /// </summary>
    Task<IReadOnlyList<EuronextSymbolMapping>> GetAllAsync(CancellationToken cancellationToken = default);
}
