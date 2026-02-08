using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Application.DTOs;

public record FixedDepositResponse(
    Guid Id,
    Guid BankAccountId,
    string BankAccountName,
    decimal Principal,
    decimal AnnualInterestRate,
    int TermMonths,
    DateTime StartDate,
    DateTime MaturityDate,
    decimal ExpectedInterest,
    decimal? ActualInterest,
    string Currency,
    string Status,
    string? Note,
    int DaysRemaining,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static FixedDepositResponse FromEntity(FixedDeposit entity, DateTime? utcToday = null)
    {
        var today = (utcToday ?? DateTime.UtcNow).Date;
        var daysRemaining = Math.Max((entity.MaturityDate.Date - today).Days, 0);

        return new FixedDepositResponse(
            entity.Id,
            entity.BankAccountId,
            entity.BankAccount.BankName,
            entity.Principal,
            entity.AnnualInterestRate,
            entity.TermMonths,
            entity.StartDate,
            entity.MaturityDate,
            entity.ExpectedInterest,
            entity.ActualInterest,
            entity.Currency,
            entity.Status.ToString(),
            entity.Note,
            daysRemaining,
            entity.CreatedAt,
            entity.UpdatedAt
        );
    }
}

public record CreateFixedDepositRequest(
    Guid BankAccountId,
    decimal Principal,
    decimal AnnualInterestRate,
    int TermMonths,
    DateTime StartDate,
    string? Note
);

public record UpdateFixedDepositRequest(
    decimal? ActualInterest,
    string? Note
);

public record CloseFixedDepositRequest(
    decimal ActualInterest,
    bool IsEarlyWithdrawal = false
);
