namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 銀行帳戶資產配置用途分類。
/// </summary>
public enum AllocationPurpose
{
    /// <summary>緊急預備金</summary>
    EmergencyFund = 1,

    /// <summary>家庭存款</summary>
    FamilyDeposit = 2,

    /// <summary>一般用途</summary>
    General = 3,

    /// <summary>儲蓄</summary>
    Savings = 4,

    /// <summary>投資準備金</summary>
    Investment = 5,

    /// <summary>其他</summary>
    Other = 6
}
