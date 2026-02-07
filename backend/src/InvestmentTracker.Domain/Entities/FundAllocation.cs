using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Fund allocation entity for virtual allocation of bank assets by purpose.
/// </summary>
public class FundAllocation : BaseEntity
{
    public Guid UserId { get; private set; }
    public AllocationPurpose Purpose { get; private set; }
    public decimal Amount { get; private set; }
    public bool IsDisposable { get; private set; }
    public string? Note { get; private set; }

    // Navigation property
    public User User { get; private set; } = null!;

    // EF Core required parameterless constructor
    private FundAllocation() { }

    public FundAllocation(
        Guid userId,
        AllocationPurpose purpose,
        decimal amount,
        string? note = null,
        bool isDisposable = false)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetPurpose(purpose);
        SetAmount(amount);
        SetNote(note);
        SetIsDisposable(isDisposable);
    }

    public void SetAmount(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentException("Amount cannot be negative", nameof(amount));

        Amount = Math.Round(amount, 2);
    }

    public void SetPurpose(AllocationPurpose purpose)
    {
        if (!Enum.IsDefined(typeof(AllocationPurpose), purpose))
            throw new ArgumentException("Invalid allocation purpose", nameof(purpose));

        Purpose = purpose;
    }

    public void SetIsDisposable(bool value)
    {
        IsDisposable = value;
    }

    public void SetNote(string? note)
    {
        if (note?.Length > 500)
            throw new ArgumentException("Note cannot exceed 500 characters", nameof(note));

        Note = note?.Trim();
    }
}
