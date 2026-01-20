using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// 外幣交易記錄實體，記錄換匯、利息及支出事件
/// </summary>
public class CurrencyTransaction : BaseEntity
{
    public Guid CurrencyLedgerId { get; private set; }
    public DateTime TransactionDate { get; private set; }
    public CurrencyTransactionType TransactionType { get; private set; }
    public decimal ForeignAmount { get; private set; }
    public decimal? HomeAmount { get; private set; }
    public decimal? ExchangeRate { get; private set; }
    public Guid? RelatedStockTransactionId { get; private set; }
    public string? Notes { get; private set; }
    public bool IsDeleted { get; private set; }

    // 導覽屬性
    public CurrencyLedger CurrencyLedger { get; private set; } = null!;
    public StockTransaction? RelatedStockTransaction { get; private set; }

    // EF Core 必要的無參數建構子
    private CurrencyTransaction() { }

    public CurrencyTransaction(
        Guid currencyLedgerId,
        DateTime transactionDate,
        CurrencyTransactionType transactionType,
        decimal foreignAmount,
        decimal? homeAmount = null,
        decimal? exchangeRate = null,
        Guid? relatedStockTransactionId = null,
        string? notes = null)
    {
        if (currencyLedgerId == Guid.Empty)
            throw new ArgumentException("Currency ledger ID is required", nameof(currencyLedgerId));

        CurrencyLedgerId = currencyLedgerId;
        SetTransactionDate(transactionDate);
        TransactionType = transactionType;
        SetAmounts(transactionType, foreignAmount, homeAmount, exchangeRate);
        RelatedStockTransactionId = relatedStockTransactionId;
        SetNotes(notes);
    }

    public void SetTransactionDate(DateTime date)
    {
        if (date > DateTime.UtcNow.AddDays(1))
            throw new ArgumentException("Transaction date cannot be in the future", nameof(date));

        // 確保 UTC Kind 以相容 PostgreSQL
        TransactionDate = DateTime.SpecifyKind(date.Date, DateTimeKind.Utc);
    }

    public void SetAmounts(
        CurrencyTransactionType transactionType,
        decimal foreignAmount,
        decimal? homeAmount = null,
        decimal? exchangeRate = null)
    {
        if (foreignAmount <= 0)
            throw new ArgumentException("Foreign amount must be positive", nameof(foreignAmount));

        // 根據交易類型驗證必填欄位
        switch (transactionType)
        {
            case CurrencyTransactionType.ExchangeBuy:
            case CurrencyTransactionType.ExchangeSell:
                if (homeAmount is null or <= 0)
                    throw new ArgumentException("Home amount is required for exchange transactions", nameof(homeAmount));
                if (exchangeRate is null or <= 0)
                    throw new ArgumentException("Exchange rate is required for exchange transactions", nameof(exchangeRate));
                break;

            case CurrencyTransactionType.Interest:
                // 利息交易的本國幣金額和匯率為選填
                break;

            case CurrencyTransactionType.Spend:
                // 支出交易不需要本國幣金額或匯率
                break;
        }

        TransactionType = transactionType;
        ForeignAmount = Math.Round(foreignAmount, 4);
        HomeAmount = homeAmount.HasValue ? Math.Round(homeAmount.Value, 2) : null;
        ExchangeRate = exchangeRate.HasValue ? Math.Round(exchangeRate.Value, 6) : null;
    }

    public void SetNotes(string? notes)
    {
        if (notes?.Length > 500)
            throw new ArgumentException("Notes cannot exceed 500 characters", nameof(notes));

        Notes = notes?.Trim();
    }

    public void MarkAsDeleted() => IsDeleted = true;
    public void Restore() => IsDeleted = false;
}
