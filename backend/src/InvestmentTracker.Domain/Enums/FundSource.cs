namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// Source of funds for stock transactions.
/// </summary>
public enum FundSource
{
    /// <summary>Not tracked / external funding</summary>
    None = 0,

    /// <summary>Funds from a linked Currency Ledger</summary>
    CurrencyLedger = 1
}
