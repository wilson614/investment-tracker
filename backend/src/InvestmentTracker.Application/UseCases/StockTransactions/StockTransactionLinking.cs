using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// Stock Transaction 與 CurrencyLedger 連動的共享驗證與規格產生器。
/// </summary>
internal static class StockTransactionLinking
{
    /// <summary>
    /// FR-005: 驗證股票交易幣別與綁定帳本幣別一致。
    /// </summary>
    /// <exception cref="BusinessRuleException">幣別不符時拋出。</exception>
    internal static void ValidateCurrencyMatchesBoundLedger(
        Currency stockCurrency,
        Domain.Entities.CurrencyLedger boundLedger)
    {
        if (stockCurrency.ToString() != boundLedger.CurrencyCode)
        {
            throw new BusinessRuleException(
                $"股票幣別 ({stockCurrency}) 與帳本綁定幣別 ({boundLedger.CurrencyCode}) 不符");
        }
    }

    /// <summary>
    /// 產生連動 CurrencyTransaction 的規格。
    /// Buy → Spend；Sell → OtherIncome（若淨收入 > 0）。
    /// </summary>
    internal static LinkedCurrencyTransactionSpec? BuildLinkedCurrencyTransactionSpec(
        TransactionType stockTransactionType,
        StockTransaction stockTransaction,
        Domain.Entities.CurrencyLedger boundLedger)
    {
        if (stockTransactionType == TransactionType.Buy)
        {
            return new LinkedCurrencyTransactionSpec(
                CurrencyTransactionType.Spend,
                stockTransaction.TotalCostSource,
                boundLedger);
        }

        if (stockTransactionType == TransactionType.Sell)
        {
            var subtotal = stockTransaction.Shares * stockTransaction.PricePerShare;
            if (stockTransaction.IsTaiwanStock)
                subtotal = Math.Floor(subtotal);

            var netProceeds = subtotal - stockTransaction.Fees;
            if (netProceeds > 0)
            {
                return new LinkedCurrencyTransactionSpec(
                    CurrencyTransactionType.OtherIncome,
                    netProceeds,
                    boundLedger);
            }
        }

        return null;
    }
}

/// <summary>
/// 描述要建立的連動 CurrencyTransaction。
/// </summary>
internal record LinkedCurrencyTransactionSpec(
    CurrencyTransactionType TransactionType,
    decimal Amount,
    Domain.Entities.CurrencyLedger BoundLedger);
