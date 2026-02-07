using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Fund allocation entity for virtual allocation of bank assets by purpose.
/// </summary>
public class FundAllocation : BaseEntity
{
    public Guid UserId { get; private set; }
    public string Purpose { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public bool IsDisposable { get; private set; }
    public string? Note { get; private set; }

    // Navigation property
    public User User { get; private set; } = null!;

    // EF Core required parameterless constructor
    private FundAllocation() { }

    public FundAllocation(
        Guid userId,
        string purpose,
        decimal amount,
        string? note = null,
        bool isDisposable = false)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("使用者 ID 為必填", nameof(userId));

        UserId = userId;
        SetPurpose(purpose);
        SetAmount(amount);
        SetNote(note);
        SetIsDisposable(isDisposable);
    }

    public void SetAmount(decimal amount)
    {
        if (amount < 0)
            throw new ArgumentException("金額不可為負數", nameof(amount));

        Amount = Math.Round(amount, 2);
    }

    public void SetPurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("配置用途不可為空白", nameof(purpose));

        Purpose = purpose.Trim();
    }

    public void SetIsDisposable(bool value)
    {
        IsDisposable = value;
    }

    public void SetNote(string? note)
    {
        if (note?.Length > 500)
            throw new ArgumentException("備註不可超過 500 個字元", nameof(note));

        Note = note?.Trim();
    }
}
