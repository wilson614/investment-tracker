namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 股票／ETF 的交易類型。
/// </summary>
public enum TransactionType
{
    /// <summary>買入</summary>
    Buy = 1,

    /// <summary>賣出</summary>
    Sell = 2,

    /// <summary>股票分割調整（只改股數，不改成本）</summary>
    Split = 3,

    /// <summary>手動修正／調整</summary>
    Adjustment = 4
}
