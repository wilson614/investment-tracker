using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;

namespace InvestmentTracker.Application.UseCases.StockTransactions;

/// <summary>
/// Stock Transaction 與 CurrencyLedger 連動的共享驗證與規格產生器。
/// </summary>
internal static class StockTransactionLinking
{
    // T007: 優先使用 011 規格定義的專用 stock-linked internal categories。
    // 為了將變更範圍限制在本檔，若 enum 尚未完成 T003 重新命名，
    // 會回退到既有類型值，避免跨 task 修改。
    private static readonly CurrencyTransactionType StockBuyInternalCategory =
        ResolveStockLinkedInternalCategory(
            CurrencyTransactionType.Spend,
            "StockBuy",
            "StockBuyLinked");

    private static readonly CurrencyTransactionType StockSellInternalCategory =
        ResolveStockLinkedInternalCategory(
            CurrencyTransactionType.OtherIncome,
            "StockSell",
            "StockSellLinked");

    /// <summary>
    /// FR-005: 驗證股票交易幣別與綁定帳本幣別一致。
    /// </summary>
    /// <exception cref="BusinessRuleException">幣別不符時拋出。</exception>
    internal static void ValidateCurrencyMatchesBoundLedger(
        Currency stockCurrency,
        Domain.Entities.CurrencyLedger boundLedger)
    {
        if (!string.Equals(stockCurrency.ToString(), boundLedger.CurrencyCode, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                $"股票幣別 ({stockCurrency}) 與帳本綁定幣別 ({boundLedger.CurrencyCode}) 不符");
        }
    }

    /// <summary>
    /// 產生連動 CurrencyTransaction 的規格。
    /// Buy → StockBuy(Internal)；Sell → StockSell(Internal)（若淨收入 > 0）。
    /// </summary>
    internal static LinkedCurrencyTransactionSpec? BuildLinkedCurrencyTransactionSpec(
        TransactionType stockTransactionType,
        StockTransaction stockTransaction,
        Domain.Entities.CurrencyLedger boundLedger)
    {
        if (stockTransactionType == TransactionType.Buy)
        {
            return new LinkedCurrencyTransactionSpec(
                StockBuyInternalCategory,
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
                    StockSellInternalCategory,
                    netProceeds,
                    boundLedger);
            }
        }

        return null;
    }

    private static CurrencyTransactionType ResolveStockLinkedInternalCategory(
        CurrencyTransactionType numericFallback,
        params string[] candidateNames)
    {
        foreach (var candidateName in candidateNames)
        {
            if (Enum.TryParse(candidateName, ignoreCase: false, out CurrencyTransactionType resolved)
                && Enum.IsDefined(resolved))
            {
                return resolved;
            }
        }

        if (Enum.IsDefined(numericFallback))
        {
            return numericFallback;
        }

        throw new InvalidOperationException(
            $"Unable to resolve stock-linked internal currency transaction category. Candidates: {string.Join(", ", candidateNames)}");
    }
}

/// <summary>
/// 描述要建立的連動 CurrencyTransaction。
/// </summary>
internal record LinkedCurrencyTransactionSpec(
    CurrencyTransactionType TransactionType,
    decimal Amount,
    Domain.Entities.CurrencyLedger BoundLedger);
