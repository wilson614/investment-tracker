namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// Types of stock/ETF transactions.
/// </summary>
public enum TransactionType
{
    /// <summary>Purchase shares</summary>
    Buy = 1,

    /// <summary>Sell shares</summary>
    Sell = 2,

    /// <summary>Stock split adjustment (changes share count, not cost basis)</summary>
    Split = 3,

    /// <summary>Manual correction/adjustment</summary>
    Adjustment = 4
}
