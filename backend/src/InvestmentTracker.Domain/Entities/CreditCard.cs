using InvestmentTracker.Domain.Common;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Credit card entity containing installment purchases.
/// </summary>
public class CreditCard : BaseEntity
{
    public Guid UserId { get; private set; }
    public string BankName { get; private set; } = string.Empty;
    public string CardName { get; private set; } = string.Empty;
    public int PaymentDueDay { get; private set; }
    public string? Note { get; private set; }

    // Navigation property
    private readonly List<Installment> _installments = [];
    public ICollection<Installment> Installments => _installments;

    // EF Core required parameterless constructor
    private CreditCard() { }

    public CreditCard(
        Guid userId,
        string bankName,
        string cardName,
        int billingCycleDay,
        string? note = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetBankName(bankName);
        SetCardName(cardName);
        SetPaymentDueDay(billingCycleDay);
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

    public void SetCardName(string cardName)
    {
        if (string.IsNullOrWhiteSpace(cardName))
            throw new ArgumentException("Card name is required", nameof(cardName));

        if (cardName.Length > 100)
            throw new ArgumentException("Card name cannot exceed 100 characters", nameof(cardName));

        CardName = cardName.Trim();
    }

    public void SetPaymentDueDay(int paymentDueDay)
    {
        if (paymentDueDay < 1 || paymentDueDay > 31)
            throw new ArgumentException("Payment due day must be between 1 and 31", nameof(paymentDueDay));

        PaymentDueDay = paymentDueDay;
    }

    public void SetNote(string? note)
    {
        if (note?.Length > 500)
            throw new ArgumentException("Note cannot exceed 500 characters", nameof(note));

        Note = note?.Trim();
    }

}
