namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 外幣台帳（Currency Ledger）的交易類型。
/// </summary>
public enum CurrencyTransactionType
{
    /// <summary>用本位幣買入外幣（例如：TWD → USD）</summary>
    ExchangeBuy = 1,

    /// <summary>賣出外幣換回本位幣（例如：USD → TWD）</summary>
    ExchangeSell = 2,

    /// <summary>銀行利息入帳（以 0 成本增加餘額）</summary>
    Interest = 3,

    /// <summary>支出外幣（例如：用於買股）</summary>
    Spend = 4,

    /// <summary>期初餘額（開戶／起始餘額，含已知成本）</summary>
    InitialBalance = 5,

    /// <summary>其他收入（例如：券商回饋、股利）</summary>
    OtherIncome = 6,

    /// <summary>其他支出（例如：費用）</summary>
    OtherExpense = 7,

    /// <summary>外部存入外幣（入金）</summary>
    Deposit = 8,

    /// <summary>外部提領外幣（出金）</summary>
    Withdraw = 9
}
