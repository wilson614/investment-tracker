namespace InvestmentTracker.Domain.Enums;

/// <summary>
/// 分期狀態。
/// </summary>
public enum InstallmentStatus
{
    /// <summary>進行中</summary>
    Active = 0,

    /// <summary>已完成</summary>
    Completed = 1,

    /// <summary>已取消</summary>
    Cancelled = 2
}
