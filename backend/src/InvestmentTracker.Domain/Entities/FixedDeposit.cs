using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Domain.Entities;

/// <summary>
/// Fixed deposit entity, representing locked principal with a fixed term and expected interest.
/// </summary>
public class FixedDeposit : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid BankAccountId { get; private set; }
    public decimal Principal { get; private set; }
    public decimal AnnualInterestRate { get; private set; }
    public int TermMonths { get; private set; }
    public DateTime StartDate { get; private set; }
    public DateTime MaturityDate { get; private set; }
    public decimal ExpectedInterest { get; private set; }
    public decimal? ActualInterest { get; private set; }
    public string Currency { get; private set; } = "TWD";
    public FixedDepositStatus Status { get; private set; } = FixedDepositStatus.Active;
    public string? Note { get; private set; }

    // Navigation property
    public BankAccount BankAccount { get; private set; } = null!;

    // EF Core required parameterless constructor
    private FixedDeposit() { }

    public FixedDeposit(
        Guid userId,
        Guid bankAccountId,
        decimal principal,
        decimal annualInterestRate,
        int termMonths,
        DateTime startDate,
        string? note = null,
        string currency = "TWD")
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        if (bankAccountId == Guid.Empty)
            throw new ArgumentException("Bank account ID is required", nameof(bankAccountId));

        UserId = userId;
        BankAccountId = bankAccountId;
        SetPrincipal(principal);
        SetAnnualInterestRate(annualInterestRate);
        SetTermMonths(termMonths);
        SetStartDate(startDate);
        SetCurrency(currency);
        SetNote(note);

        RecalculateComputedFields();
    }

    public void SetPrincipal(decimal principal)
    {
        if (principal < 0)
            throw new ArgumentException("Principal cannot be negative", nameof(principal));

        Principal = Math.Round(principal, 2);
        RecalculateComputedFields();
    }

    public void SetAnnualInterestRate(decimal annualInterestRate)
    {
        if (annualInterestRate < 0)
            throw new ArgumentException("Annual interest rate cannot be negative", nameof(annualInterestRate));

        AnnualInterestRate = Math.Round(annualInterestRate, 6);
        RecalculateComputedFields();
    }

    public void SetTermMonths(int termMonths)
    {
        if (termMonths <= 0)
            throw new ArgumentException("Term months must be greater than 0", nameof(termMonths));

        TermMonths = termMonths;
        RecalculateComputedFields();
    }

    public void SetStartDate(DateTime startDate)
    {
        if (startDate > DateTime.UtcNow.AddDays(1))
            throw new ArgumentException("Start date cannot be in the future", nameof(startDate));

        StartDate = DateTime.SpecifyKind(startDate.Date, DateTimeKind.Utc);
        RecalculateComputedFields();
    }

    public void SetCurrency(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3)
            throw new ArgumentException("Currency code must be a 3-letter ISO code", nameof(currency));

        Currency = currency.ToUpperInvariant();
    }

    public void SetStatus(FixedDepositStatus status)
    {
        if (!Enum.IsDefined(typeof(FixedDepositStatus), status))
            throw new ArgumentException("Invalid fixed deposit status", nameof(status));

        Status = status;
    }

    public void SetActualInterest(decimal? actualInterest)
    {
        if (actualInterest < 0)
            throw new ArgumentException("Actual interest cannot be negative", nameof(actualInterest));

        ActualInterest = actualInterest.HasValue ? Math.Round(actualInterest.Value, 2) : null;
    }

    public void SetNote(string? note)
    {
        if (note?.Length > 500)
            throw new ArgumentException("Note cannot exceed 500 characters", nameof(note));

        Note = note?.Trim();
    }

    public void MarkAsMatured() => Status = FixedDepositStatus.Matured;

    public void Close(decimal actualInterest)
    {
        SetActualInterest(actualInterest);
        Status = FixedDepositStatus.Closed;
    }

    public void MarkAsEarlyWithdrawal(decimal? actualInterest = null)
    {
        SetActualInterest(actualInterest);
        Status = FixedDepositStatus.EarlyWithdrawal;
    }

    private void RecalculateComputedFields()
    {
        if (StartDate == default || TermMonths <= 0)
            return;

        MaturityDate = DateTime.SpecifyKind(StartDate.AddMonths(TermMonths), DateTimeKind.Utc);

        var annualRateDecimal = AnnualInterestRate / 100m;
        ExpectedInterest = Math.Round(Principal * annualRateDecimal * (TermMonths / 12m), 2);
    }
}
