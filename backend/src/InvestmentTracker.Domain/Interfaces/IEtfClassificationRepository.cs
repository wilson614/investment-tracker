using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Interfaces;

/// <summary>
/// ETF 分類相關操作的 Repository 介面。
/// </summary>
public interface IEtfClassificationRepository
{
    /// <summary>
    /// 依據代號與市場取得 ETF 分類。
    /// </summary>
    Task<EtfClassification?> GetBySymbolAndMarketAsync(string symbol, string market, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得所有 ETF 分類。
    /// </summary>
    Task<IReadOnlyList<EtfClassification>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 新增或更新 ETF 分類。
    /// </summary>
    Task UpsertAsync(EtfClassification classification, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取得多個代號的 ETF 分類。
    /// </summary>
    Task<IReadOnlyList<EtfClassification>> GetBySymbolsAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);
}
