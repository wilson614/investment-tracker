namespace InvestmentTracker.Domain.Common;

/// <summary>
/// Base class for all domain entities with common properties.
/// </summary>
public abstract class BaseEntity : IHasTimestamps
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
