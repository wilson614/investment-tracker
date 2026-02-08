namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 定存狀態。
/// </summary>
public enum FixedDepositStatus
{
    /// <summary>進行中</summary>
    Active = 0,

    /// <summary>已到期</summary>
    Matured = 1,

    /// <summary>已結清</summary>
    Closed = 2,

    /// <summary>提前解約</summary>
    EarlyWithdrawal = 3
}
