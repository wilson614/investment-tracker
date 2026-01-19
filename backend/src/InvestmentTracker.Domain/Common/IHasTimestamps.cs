namespace InvestmentTracker.Domain.Common;

/// <summary>
/// 提供建立與更新時間戳記的 Entity 介面。
/// </summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
