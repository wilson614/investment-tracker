using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Services;

/// <summary>
/// 現金流事件（Cash Flow events）來源策略。
/// </summary>
public interface IReturnCashFlowStrategy
{
    /// <summary>
    /// 判斷此策略是否適用於指定投資組合。
    /// </summary>
    bool IsApplicable(Portfolio portfolio, IReadOnlyList<StockTransaction> stockTransactions, IReadOnlyList<CurrencyLedger> ledgers);

    /// <summary>
    /// 取得此投資組合在指定期間內的現金流事件（依日期排序）。
    /// </summary>
    IReadOnlyList<ReturnCashFlowEvent> GetCashFlowEvents(
        Portfolio portfolio,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<StockTransaction> stockTransactions,
        IReadOnlyList<CurrencyLedger> ledgers,
        IReadOnlyList<CurrencyTransaction> currencyTransactions);

    /// <summary>
    /// 取得策略名稱（用於除錯/記錄）。
    /// </summary>
    string Name { get; }
}

/// <summary>
/// 現金流事件：用於快照寫入與報酬率計算。
/// Amount 正負號約定：投入/入金為正；提領/出金為負。
/// </summary>
public record ReturnCashFlowEvent(
    Guid PortfolioId,
    Guid TransactionId,
    DateTime TransactionDate,
    decimal Amount,
    string CurrencyCode,
    ReturnCashFlowEventSource Source);

public enum ReturnCashFlowEventSource
{
    StockTransaction = 1,
    CurrencyLedger = 2
}

/// <summary>
/// 預設策略：StockTransaction Buy/Sell 視為外部現金流。
/// - Buy：投入（正）
/// - Sell：提領（負）
/// </summary>
public class StockTransactionCashFlowStrategy : IReturnCashFlowStrategy
{
    public string Name => "StockTransaction";

    public bool IsApplicable(
        Portfolio portfolio,
        IReadOnlyList<StockTransaction> stockTransactions,
        IReadOnlyList<CurrencyLedger> ledgers)
        => true;

    public IReadOnlyList<ReturnCashFlowEvent> GetCashFlowEvents(
        Portfolio portfolio,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<StockTransaction> stockTransactions,
        IReadOnlyList<CurrencyLedger> ledgers,
        IReadOnlyList<CurrencyTransaction> currencyTransactions)
    {
        return stockTransactions
            .Where(t => t.PortfolioId == portfolio.Id
                        && !t.IsDeleted
                        && t.TransactionDate.Date >= fromDate.Date
                        && t.TransactionDate.Date <= toDate.Date
                        && t.TransactionType is TransactionType.Buy or TransactionType.Sell)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .Select(t =>
            {
                var amount = t.TransactionType == TransactionType.Buy
                    ? t.TotalCostSource
                    : -(t.Shares * t.PricePerShare - t.Fees);

                return new ReturnCashFlowEvent(
                    PortfolioId: t.PortfolioId,
                    TransactionId: t.Id,
                    TransactionDate: t.TransactionDate,
                    Amount: amount,
                    CurrencyCode: t.Currency.ToString(),
                    Source: ReturnCashFlowEventSource.StockTransaction);
            })
            .ToList();
    }
}

/// <summary>
/// Ledger 策略：僅納入「明確外部現金流」事件，並明確排除 internal events。
/// </summary>
public class CurrencyLedgerCashFlowStrategy : IReturnCashFlowStrategy
{
    private const string TwdCurrencyCode = "TWD";
    private const string StockTopUpNotePrefix = "補足買入";

    public string Name => "CurrencyLedger";

    public bool IsApplicable(
        Portfolio portfolio,
        IReadOnlyList<StockTransaction> stockTransactions,
        IReadOnlyList<CurrencyLedger> ledgers)
    {
        return ledgers.Any(l => l.Id == portfolio.BoundCurrencyLedgerId && l.IsActive);
    }

    public IReadOnlyList<ReturnCashFlowEvent> GetCashFlowEvents(
        Portfolio portfolio,
        DateTime fromDate,
        DateTime toDate,
        IReadOnlyList<StockTransaction> stockTransactions,
        IReadOnlyList<CurrencyLedger> ledgers,
        IReadOnlyList<CurrencyTransaction> currencyTransactions)
    {
        var boundLedgerId = portfolio.BoundCurrencyLedgerId;
        var boundLedgerCurrency = ledgers.FirstOrDefault(l => l.Id == boundLedgerId)?.CurrencyCode
                                  ?? portfolio.BaseCurrency;

        return currencyTransactions
            .Where(t => t.CurrencyLedgerId == boundLedgerId
                        && !t.IsDeleted
                        && t.TransactionDate.Date >= fromDate.Date
                        && t.TransactionDate.Date <= toDate.Date
                        && IsExplicitExternalCashFlow(t, boundLedgerCurrency))
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new ReturnCashFlowEvent(
                PortfolioId: portfolio.Id,
                TransactionId: t.Id,
                TransactionDate: t.TransactionDate,
                Amount: GetSignedAmount(t),
                CurrencyCode: boundLedgerCurrency,
                Source: ReturnCashFlowEventSource.CurrencyLedger))
            .ToList();
    }

    private static bool IsExplicitExternalCashFlow(
        CurrencyTransaction transaction,
        string ledgerCurrencyCode)
    {
        if (IsStockLinkedInternalEvent(transaction))
            return false;

        if (IsInternalFxTransferEffect(transaction))
            return false;

        return transaction.TransactionType switch
        {
            CurrencyTransactionType.InitialBalance => true,
            CurrencyTransactionType.Deposit => true,
            CurrencyTransactionType.Withdraw => true,
            CurrencyTransactionType.OtherIncome => true,
            CurrencyTransactionType.OtherExpense => true,
            CurrencyTransactionType.ExchangeBuy => !IsTwdLedger(ledgerCurrencyCode),
            CurrencyTransactionType.ExchangeSell => !IsTwdLedger(ledgerCurrencyCode),
            _ => false
        };
    }

    private static bool IsStockLinkedInternalEvent(CurrencyTransaction transaction)
    {
        if (!transaction.RelatedStockTransactionId.HasValue)
            return false;

        var categoryName = transaction.TransactionType.ToString();

        // 新版 enum：明確 stock-linked internal categories
        if (categoryName is "StockBuy" or "StockBuyLinked" or "StockSell" or "StockSellLinked")
            return true;

        // 舊版暫存語意：buy-linked 仍可能使用 Spend
        if (transaction.TransactionType == CurrencyTransactionType.Spend)
            return true;

        // 在 enum 尚未完成重命名前，sell-linked 可能暫時回退為 OtherIncome。
        // 只有「補足買入」top-up 才視為外部入金；其餘 related-stock 的 OtherIncome 視為 internal。
        if (transaction.TransactionType == CurrencyTransactionType.OtherIncome)
            return !IsStockTopUpEvent(transaction);

        return false;
    }

    private static bool IsInternalFxTransferEffect(CurrencyTransaction transaction)
    {
        if (!transaction.RelatedStockTransactionId.HasValue)
            return false;

        // Related-stock 的 ExchangeBuy/ExchangeSell 不一定是 internal：
        // 「補足買入」top-up（notes 前綴）屬於 external cash flow，必須保留。
        if (IsStockTopUpEvent(transaction))
            return false;

        return transaction.TransactionType is CurrencyTransactionType.ExchangeBuy or CurrencyTransactionType.ExchangeSell;
    }

    private static bool IsStockTopUpEvent(CurrencyTransaction transaction)
        => transaction.Notes?.StartsWith(StockTopUpNotePrefix, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsTwdLedger(string ledgerCurrencyCode)
        => string.Equals(ledgerCurrencyCode, TwdCurrencyCode, StringComparison.OrdinalIgnoreCase);

    private static decimal GetSignedAmount(CurrencyTransaction transaction)
    {
        var sign = transaction.TransactionType switch
        {
            CurrencyTransactionType.Withdraw => -1m,
            CurrencyTransactionType.OtherExpense => -1m,
            CurrencyTransactionType.ExchangeSell => -1m,
            _ => 1m
        };

        return sign * transaction.ForeignAmount;
    }
}

/// <summary>
/// 依投資組合狀態選擇 CF 策略。
/// </summary>
public class ReturnCashFlowStrategyProvider(
    StockTransactionCashFlowStrategy stockStrategy,
    CurrencyLedgerCashFlowStrategy ledgerStrategy)
{
    public IReturnCashFlowStrategy GetStrategy(
        Portfolio portfolio,
        IReadOnlyList<StockTransaction> stockTransactions,
        IReadOnlyList<CurrencyLedger> ledgers,
        IReadOnlyList<CurrencyTransaction> currencyTransactions)
    {
        if (ledgerStrategy.IsApplicable(portfolio, stockTransactions, ledgers))
        {
            return ledgerStrategy;
        }

        return stockStrategy;
    }
}
