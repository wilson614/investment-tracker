namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// Types of currency ledger transactions.
/// </summary>
public enum CurrencyTransactionType
{
    /// <summary>Buy foreign currency with home currency (e.g., TWD → USD)</summary>
    ExchangeBuy = 1,

    /// <summary>Sell foreign currency for home currency (e.g., USD → TWD)</summary>
    ExchangeSell = 2,

    /// <summary>Bank interest received (increases balance with 0 cost basis)</summary>
    Interest = 3,

    /// <summary>Spend foreign currency (e.g., for stock purchase)</summary>
    Spend = 4
}
