using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Application.Interfaces;

/// <summary>
/// 交易時投資組合估值快照服務。
/// 用於在現金流事件（CF event）發生時，寫入/重建 TransactionPortfolioSnapshot。
/// </summary>
public interface ITransactionPortfolioSnapshotService
{
    /// <summary>
    /// 取得指定投資組合在一段期間內的快照（依日期排序）。
    /// </summary>
    Task<IReadOnlyList<TransactionPortfolioSnapshot>> GetSnapshotsAsync(
        Guid portfolioId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 針對指定交易（StockTransaction 或 CurrencyTransaction）寫入快照。
    /// 若已存在則覆寫。
    /// </summary>
    Task UpsertSnapshotAsync(
        Guid portfolioId,
        Guid transactionId,
        DateTime transactionDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 刪除指定交易對應的快照。
    /// </summary>
    Task DeleteSnapshotAsync(
        Guid portfolioId,
        Guid transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 針對指定投資組合，回填（補齊）期間內缺漏的快照。
    /// </summary>
    Task BackfillSnapshotsAsync(
        Guid portfolioId,
        DateTime fromDate,
        DateTime toDate,
        CancellationToken cancellationToken = default);
}
