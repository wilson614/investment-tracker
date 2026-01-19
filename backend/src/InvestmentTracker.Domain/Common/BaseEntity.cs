namespace InvestmentTracker.Domain.Common;

/// <summary>
/// 所有領域 Entity 的基底類別，提供共通欄位（Id、建立/更新時間）。
/// </summary>
public abstract class BaseEntity : IHasTimestamps
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
