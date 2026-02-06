using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Bank account entity, tracking user deposits and interest settings.
/// </summary>
public class BankAccount : BaseEntity
{
    public Guid UserId { get; private set; }
    public string BankName { get; private set; } = string.Empty;
    public decimal TotalAssets { get; private set; }
    public decimal InterestRate { get; private set; }
    public decimal InterestCap { get; private set; }
    public string Currency { get; private set; } = "TWD";
    public string? Note { get; private set; }
    public bool IsActive { get; private set; } = true;

    // Navigation property
    public User User { get; private set; } = null!;

    // EF Core required parameterless constructor
    private BankAccount() { }

    public BankAccount(
        Guid userId,
        string bankName,
        decimal totalAssets = 0m,
        decimal interestRate = 0m,
        decimal interestCap = 0m,
        string? note = null,
        string currency = "TWD")
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetBankName(bankName);
        SetTotalAssets(totalAssets);
        SetInterestSettings(interestRate, interestCap);
        SetCurrency(currency);
        SetNote(note);
    }

    public void SetBankName(string bankName)
    {
        if (string.IsNullOrWhiteSpace(bankName))
            throw new ArgumentException("Bank name is required", nameof(bankName));

        if (bankName.Length > 100)
            throw new ArgumentException("Bank name cannot exceed 100 characters", nameof(bankName));

        BankName = bankName.Trim();
    }

    public void SetTotalAssets(decimal totalAssets)
    {
        if (totalAssets < 0)
            throw new ArgumentException("Total assets cannot be negative", nameof(totalAssets));

        TotalAssets = Math.Round(totalAssets, 2);
    }

    public void SetInterestSettings(decimal interestRate, decimal interestCap)
    {
        if (interestRate < 0)
            throw new ArgumentException("Interest rate cannot be negative", nameof(interestRate));

        if (interestCap < 0)
            throw new ArgumentException("Interest cap cannot be negative", nameof(interestCap));

        InterestRate = Math.Round(interestRate, 4);
        InterestCap = Math.Round(interestCap, 2);
    }

    public void SetCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency code must be a 3-letter ISO code", nameof(currency));

        Currency = currency.ToUpperInvariant();
    }

    public void SetNote(string? note)
    {
        if (note?.Length > 500)
            throw new ArgumentException("Note cannot exceed 500 characters", nameof(note));

        Note = note?.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
