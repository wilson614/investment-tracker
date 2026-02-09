using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Installment entity representing a credit card purchase paid over multiple months.
/// </summary>
public class Installment : BaseEntity
{
    public Guid CreditCardId { get; private set; }
    public Guid UserId { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public decimal TotalAmount { get; private set; }
    public int NumberOfInstallments { get; private set; }
    public int RemainingInstallments { get; private set; }
    public decimal MonthlyPayment { get; private set; }
    public DateTime StartDate { get; private set; }
    public InstallmentStatus Status { get; private set; } = InstallmentStatus.Active;
    public string? Note { get; private set; }

    // Navigation property
    public CreditCard CreditCard { get; private set; } = null!;

    // EF Core required parameterless constructor
    private Installment() { }

    public Installment(
        Guid creditCardId,
        Guid userId,
        string description,
        decimal totalAmount,
        int numberOfInstallments,
        int remainingInstallments,
        DateTime startDate,
        string? note = null)
    {
        if (creditCardId == Guid.Empty)
            throw new ArgumentException("Credit card ID is required", nameof(creditCardId));

        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        CreditCardId = creditCardId;
        UserId = userId;
        SetDescription(description);
        SetPaymentPlan(totalAmount, numberOfInstallments);
        SetRemainingInstallments(remainingInstallments);
        SetStartDate(startDate);
        SetNote(note);
    }

    public void SetDescription(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required", nameof(description));

        if (description.Length > 200)
            throw new ArgumentException("Description cannot exceed 200 characters", nameof(description));

        Description = description.Trim();
    }

    public void SetPaymentPlan(decimal totalAmount, int numberOfInstallments)
    {
        if (totalAmount <= 0)
            throw new ArgumentException("Total amount must be positive", nameof(totalAmount));

        if (numberOfInstallments <= 0)
            throw new ArgumentException("Number of installments must be greater than 0", nameof(numberOfInstallments));

        TotalAmount = Math.Round(totalAmount, 2);
        NumberOfInstallments = numberOfInstallments;
        MonthlyPayment = Math.Round(TotalAmount / NumberOfInstallments, 2);

        if (RemainingInstallments > NumberOfInstallments)
            RemainingInstallments = NumberOfInstallments;

        SyncStatusFromRemainingInstallments();
    }

    public void SetRemainingInstallments(int remainingInstallments)
    {
        if (remainingInstallments < 0)
            throw new ArgumentException("Remaining installments cannot be negative", nameof(remainingInstallments));

        if (remainingInstallments > NumberOfInstallments)
            throw new ArgumentException("Remaining installments cannot exceed total installments", nameof(remainingInstallments));

        RemainingInstallments = remainingInstallments;
        SyncStatusFromRemainingInstallments();
    }

    public void SetStartDate(DateTime startDate)
    {
        if (startDate > DateTime.UtcNow.AddDays(1))
            throw new ArgumentException("Start date cannot be in the future", nameof(startDate));

        StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
    }

    public void SetStatus(InstallmentStatus status)
    {
        if (!Enum.IsDefined(typeof(InstallmentStatus), status))
            throw new ArgumentException("Invalid installment status", nameof(status));

        Status = status;

        if (status == InstallmentStatus.Completed)
            RemainingInstallments = 0;
    }

    public void SetNote(string? note)
    {
        if (note?.Length > 500)
            throw new ArgumentException("Note cannot exceed 500 characters", nameof(note));

        Note = note?.Trim();
    }

    public int GetPaidInstallments(int billingCycleDay, DateTime? utcNow = null)
    {
        ValidatePaymentDueDay(billingCycleDay);

        var currentDate = (utcNow ?? DateTime.UtcNow).Date;
        var startDate = StartDate.Date;

        var elapsedMonths =
            (currentDate.Year - startDate.Year) * 12
            + currentDate.Month
            - startDate.Month;

        var billingDayInCurrentMonth = Math.Min(
            billingCycleDay,
            DateTime.DaysInMonth(currentDate.Year, currentDate.Month));

        if (currentDate.Day < billingDayInCurrentMonth)
            elapsedMonths--;

        return Math.Clamp(elapsedMonths, 0, NumberOfInstallments);
    }

    public int GetRemainingInstallments(int billingCycleDay, DateTime? utcNow = null)
    {
        if (Status == InstallmentStatus.Cancelled)
            return RemainingInstallments;

        if (Status == InstallmentStatus.Completed)
            return 0;

        var paidInstallments = GetPaidInstallments(billingCycleDay, utcNow);
        return Math.Max(NumberOfInstallments - paidInstallments, 0);
    }

    public InstallmentStatus GetEffectiveStatus(int billingCycleDay, DateTime? utcNow = null)
    {
        if (Status == InstallmentStatus.Cancelled)
            return InstallmentStatus.Cancelled;

        return GetRemainingInstallments(billingCycleDay, utcNow) <= 0
            ? InstallmentStatus.Completed
            : InstallmentStatus.Active;
    }

    public void Cancel() => Status = InstallmentStatus.Cancelled;

    private static void ValidatePaymentDueDay(int billingCycleDay)
    {
        if (billingCycleDay < 1 || billingCycleDay > 31)
            throw new ArgumentException("Billing cycle day must be between 1 and 31", nameof(billingCycleDay));
    }

    private void SyncStatusFromRemainingInstallments()
    {
        if (Status == InstallmentStatus.Cancelled)
            return;

        Status = RemainingInstallments == 0
            ? InstallmentStatus.Completed
            : InstallmentStatus.Active;
    }
}
