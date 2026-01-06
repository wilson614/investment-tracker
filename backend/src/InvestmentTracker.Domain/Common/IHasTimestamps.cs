namespace InvestmentTracker.Domain.Common;

/// <summary>
/// Interface for entities that track creation and modification timestamps.
/// </summary>
public interface IHasTimestamps
{
    DateTime CreatedAt { get; set; }
    DateTime UpdatedAt { get; set; }
}
