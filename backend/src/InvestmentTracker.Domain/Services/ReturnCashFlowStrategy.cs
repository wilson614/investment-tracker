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
/// Ledger 策略：當投資組合存在「有效的外部入出金」時，改以 CurrencyTransaction 的外部入出金作為 CF，
/// 並套用混合規則：若 StockTransaction 未由綁定 Ledger 支援，則仍視為外部現金流。
/// </summary>
public class CurrencyLedgerCashFlowStrategy : IReturnCashFlowStrategy
{
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

        var explicitLedgerEvents = currencyTransactions
            .Where(t => t.CurrencyLedgerId == boundLedgerId
                        && !t.IsDeleted
                        && t.TransactionDate.Date >= fromDate.Date
                        && t.TransactionDate.Date <= toDate.Date
                        && t.TransactionType is CurrencyTransactionType.InitialBalance
                            or CurrencyTransactionType.Deposit
                            or CurrencyTransactionType.Withdraw)
            .OrderBy(t => t.TransactionDate)
            .ThenBy(t => t.CreatedAt)
            .Select(t => new ReturnCashFlowEvent(
                PortfolioId: portfolio.Id,
                TransactionId: t.Id,
                TransactionDate: t.TransactionDate,
                Amount: t.TransactionType == CurrencyTransactionType.Withdraw ? -t.ForeignAmount : t.ForeignAmount,
                CurrencyCode: boundLedgerCurrency,
                Source: ReturnCashFlowEventSource.CurrencyLedger));

        var implicitStockEvents = stockTransactions
            .Where(t => t.PortfolioId == portfolio.Id
                        && !t.IsDeleted
                        && t.TransactionDate.Date >= fromDate.Date
                        && t.TransactionDate.Date <= toDate.Date
                        && t.TransactionType is TransactionType.Buy or TransactionType.Sell
                        && t.CurrencyLedgerId != boundLedgerId)
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
            });

        return explicitLedgerEvents
            .Concat(implicitStockEvents)
            .OrderBy(e => e.TransactionDate)
            .ToList();
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
        var boundLedgerId = portfolio.BoundCurrencyLedgerId;

        var hasExternalInOut = currencyTransactions.Any(t =>
            t.CurrencyLedgerId == boundLedgerId
            && !t.IsDeleted
            && t.TransactionType is CurrencyTransactionType.InitialBalance
                or CurrencyTransactionType.Deposit
                or CurrencyTransactionType.Withdraw);

        if (hasExternalInOut && ledgerStrategy.IsApplicable(portfolio, stockTransactions, ledgers))
        {
            return ledgerStrategy;
        }

        return stockStrategy;
    }
}
