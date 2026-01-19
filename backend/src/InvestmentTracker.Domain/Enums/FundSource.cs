namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 股票交易資金來源。
/// </summary>
public enum FundSource
{
    /// <summary>不追蹤／外部資金</summary>
    None = 0,

    /// <summary>來自關聯的外幣台帳（Currency Ledger）</summary>
    CurrencyLedger = 1
}
