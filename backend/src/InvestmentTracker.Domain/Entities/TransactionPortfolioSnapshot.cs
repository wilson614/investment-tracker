using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 現金流事件（Cash Flow event）當下的投資組合估值快照。
/// 用於計算年度 TWR（Time-Weighted Return）與 Modified Dietz。
/// </summary>
public class TransactionPortfolioSnapshot : BaseEntity
{
    public Guid PortfolioId { get; private set; }

    /// <summary>
    /// 對應的交易 ID。
    /// - StockTransaction CF 模式：StockTransaction.Id
    /// - Ledger CF 模式：CurrencyTransaction.Id
    /// </summary>
    public Guid TransactionId { get; private set; }

    /// <summary>
    /// 事件日期（UTC，僅取 Date 部分）。
    /// </summary>
    public DateTime SnapshotDate { get; private set; }

    public decimal PortfolioValueBeforeHome { get; private set; }
    public decimal PortfolioValueAfterHome { get; private set; }

    public decimal PortfolioValueBeforeSource { get; private set; }
    public decimal PortfolioValueAfterSource { get; private set; }

    // 導覽屬性
    public Portfolio Portfolio { get; private set; } = null!;

    private TransactionPortfolioSnapshot() { }

    public TransactionPortfolioSnapshot(
        Guid portfolioId,
        Guid transactionId,
        DateTime snapshotDate,
        decimal portfolioValueBeforeHome,
        decimal portfolioValueAfterHome,
        decimal portfolioValueBeforeSource,
        decimal portfolioValueAfterSource)
    {
        if (portfolioId == Guid.Empty)
            throw new ArgumentException("Portfolio ID is required", nameof(portfolioId));

        if (transactionId == Guid.Empty)
            throw new ArgumentException("Transaction ID is required", nameof(transactionId));

        PortfolioId = portfolioId;
        TransactionId = transactionId;
        SnapshotDate = DateTime.SpecifyKind(snapshotDate.Date, DateTimeKind.Utc);

        PortfolioValueBeforeHome = Math.Round(portfolioValueBeforeHome, 4);
        PortfolioValueAfterHome = Math.Round(portfolioValueAfterHome, 4);
        PortfolioValueBeforeSource = Math.Round(portfolioValueBeforeSource, 4);
        PortfolioValueAfterSource = Math.Round(portfolioValueAfterSource, 4);
    }
}
