using InvestmentTracker.Domain.Common;
using InvestmentTracker.Domain.Enums;

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

    public BankAccountType AccountType { get; private set; } = BankAccountType.Savings;
    public int? TermMonths { get; private set; }
    public DateTime? StartDate { get; private set; }
    public DateTime? MaturityDate { get; private set; }
    public decimal? ExpectedInterest { get; private set; }
    public decimal? ActualInterest { get; private set; }
    public FixedDepositStatus? FixedDepositStatus { get; private set; }

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
        string currency = "TWD",
        BankAccountType accountType = BankAccountType.Savings)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User ID is required", nameof(userId));

        UserId = userId;
        SetBankName(bankName);
        SetTotalAssets(totalAssets);
        SetInterestSettings(interestRate, interestCap);
        SetCurrency(currency);
        SetNote(note);
        SetAccountType(accountType);
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
        RecalculateFixedDepositComputedFields();
    }

    public void SetInterestSettings(decimal interestRate, decimal interestCap)
    {
        if (interestRate < 0)
            throw new ArgumentException("Interest rate cannot be negative", nameof(interestRate));

        if (interestCap < 0)
            throw new ArgumentException("Interest cap cannot be negative", nameof(interestCap));

        InterestRate = Math.Round(interestRate, 4);
        InterestCap = Math.Round(interestCap, 2);
        RecalculateFixedDepositComputedFields();
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

    public void SetAccountType(BankAccountType accountType)
    {
        if (!Enum.IsDefined(typeof(BankAccountType), accountType))
            throw new ArgumentException("Invalid bank account type", nameof(accountType));

        AccountType = accountType;

        if (AccountType != BankAccountType.FixedDeposit)
            ClearFixedDepositFields();
    }

    public void SetTermMonths(int? termMonths)
    {
        if (termMonths.HasValue && termMonths.Value <= 0)
            throw new ArgumentException("Term months must be greater than 0", nameof(termMonths));

        TermMonths = termMonths;
        RecalculateFixedDepositComputedFields();
    }

    public void SetStartDate(DateTime? startDate)
    {
        if (startDate.HasValue && startDate.Value > DateTime.UtcNow.AddDays(1))
            throw new ArgumentException("Start date cannot be in the future", nameof(startDate));

        StartDate = startDate.HasValue
            ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc)
            : null;

        RecalculateFixedDepositComputedFields();
    }

    public void SetMaturityDate(DateTime? maturityDate)
    {
        if (maturityDate.HasValue && StartDate.HasValue && maturityDate.Value.Date < StartDate.Value.Date)
            throw new ArgumentException("Maturity date cannot be earlier than start date", nameof(maturityDate));

        MaturityDate = maturityDate.HasValue
            ? DateTime.SpecifyKind(maturityDate.Value.Date, DateTimeKind.Utc)
            : null;
    }

    public void SetExpectedInterest(decimal? expectedInterest)
    {
        if (expectedInterest < 0)
            throw new ArgumentException("Expected interest cannot be negative", nameof(expectedInterest));

        ExpectedInterest = expectedInterest.HasValue
            ? Math.Round(expectedInterest.Value, 2)
            : null;
    }

    public void SetActualInterest(decimal? actualInterest)
    {
        if (actualInterest < 0)
            throw new ArgumentException("Actual interest cannot be negative", nameof(actualInterest));

        ActualInterest = actualInterest.HasValue
            ? Math.Round(actualInterest.Value, 2)
            : null;
    }

    public void SetFixedDepositStatus(FixedDepositStatus? fixedDepositStatus)
    {
        if (fixedDepositStatus.HasValue && !Enum.IsDefined(typeof(FixedDepositStatus), fixedDepositStatus.Value))
            throw new ArgumentException("Invalid fixed deposit status", nameof(fixedDepositStatus));

        FixedDepositStatus = fixedDepositStatus;
    }

    public void ConfigureFixedDeposit(int termMonths, DateTime startDate)
    {
        SetAccountType(BankAccountType.FixedDeposit);
        SetTermMonths(termMonths);
        SetStartDate(startDate);
        SetFixedDepositStatus(global::InvestmentTracker.Domain.Enums.FixedDepositStatus.Active);
        SetActualInterest(null);
        RecalculateFixedDepositComputedFields();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    private void RecalculateFixedDepositComputedFields()
    {
        if (AccountType != BankAccountType.FixedDeposit)
            return;

        if (!StartDate.HasValue || !TermMonths.HasValue || TermMonths.Value <= 0)
        {
            MaturityDate = null;
            ExpectedInterest = null;
            return;
        }

        MaturityDate = DateTime.SpecifyKind(StartDate.Value.AddMonths(TermMonths.Value), DateTimeKind.Utc);

        // For fixed deposit accounts, TotalAssets represents principal amount.
        var annualRateDecimal = InterestRate / 100m;
        ExpectedInterest = Math.Round(TotalAssets * annualRateDecimal * (TermMonths.Value / 12m), 2);
    }

    private void ClearFixedDepositFields()
    {
        TermMonths = null;
        StartDate = null;
        MaturityDate = null;
        ExpectedInterest = null;
        ActualInterest = null;
        FixedDepositStatus = null;
    }
}
