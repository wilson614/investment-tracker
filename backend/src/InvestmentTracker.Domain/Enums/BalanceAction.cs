namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// Specifies how to handle insufficient currency ledger balance during a stock purchase.
/// </summary>
public enum BalanceAction
{
    /// <summary>Default: no special handling. Transaction is rejected if balance insufficient.</summary>
    None = 0,

    /// <summary>Allow negative balance (margin). Proceed with purchase, balance goes negative.</summary>
    Margin = 1,

    /// <summary>Create a currency transaction to cover the shortfall before proceeding.</summary>
    TopUp = 2
}
