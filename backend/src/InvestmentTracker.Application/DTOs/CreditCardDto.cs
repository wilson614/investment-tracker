using System.ComponentModel.DataAnnotations;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;

namespace InvestmentTracker.Application.DTOs;

public record CreditCardResponse(
    Guid Id,
    string BankName,
    string CardName,
    int BillingCycleDay,
    string? Note,
    int ActiveInstallmentsCount,
    decimal TotalUnpaidBalance,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static CreditCardResponse FromEntity(CreditCard creditCard)
    {
        var utcNow = DateTime.UtcNow;

        var activeInstallments = creditCard.Installments
            .Select(i => new
            {
                Installment = i,
                EffectiveStatus = i.GetEffectiveStatus(creditCard.BillingCycleDay, utcNow),
                RemainingInstallments = i.GetRemainingInstallments(creditCard.BillingCycleDay, utcNow)
            })
            .Where(x => x.EffectiveStatus == InstallmentStatus.Active)
            .ToList();

        var activeInstallmentsCount = activeInstallments.Count;
        var totalUnpaidBalance = activeInstallments
            .Sum(x => Math.Round(x.Installment.MonthlyPayment * x.RemainingInstallments, 2));

        return new CreditCardResponse(
            creditCard.Id,
            creditCard.BankName,
            creditCard.CardName,
            creditCard.BillingCycleDay,
            creditCard.Note,
            activeInstallmentsCount,
            totalUnpaidBalance,
            creditCard.CreatedAt,
            creditCard.UpdatedAt
        );
    }
}

public record CreateCreditCardRequest(
    [Required]
    [StringLength(100)]
    string BankName,

    [Required]
    [StringLength(100)]
    string CardName,

    [Range(1, 31)]
    int BillingCycleDay,

    [StringLength(500)]
    string? Note
);

public record UpdateCreditCardRequest(
    [Required]
    [StringLength(100)]
    string BankName,

    [Required]
    [StringLength(100)]
    string CardName,

    [Range(1, 31)]
    int BillingCycleDay,

    [StringLength(500)]
    string? Note
);
