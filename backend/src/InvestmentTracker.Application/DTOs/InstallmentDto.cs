using InvestmentTracker.Domain.Entities;

namespace InvestmentTracker.Application.DTOs;

public record InstallmentResponse(
    Guid Id,
    Guid CreditCardId,
    string CreditCardName,
    string Description,
    decimal TotalAmount,
    int NumberOfInstallments,
    int RemainingInstallments,
    decimal MonthlyPayment,
    DateTime StartDate,
    string Status,
    string? Note,
    decimal UnpaidBalance,
    decimal PaidAmount,
    decimal ProgressPercentage,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static InstallmentResponse FromEntity(Installment entity)
    {
        var utcNow = DateTime.UtcNow;
        var remainingInstallments = entity.GetRemainingInstallments(entity.CreditCard.PaymentDueDay, utcNow);
        var effectiveStatus = entity.GetEffectiveStatus(entity.CreditCard.PaymentDueDay, utcNow);
        var unpaidBalance = Math.Round(entity.MonthlyPayment * remainingInstallments, 2);
        var paidAmount = Math.Round(entity.TotalAmount - unpaidBalance, 2);

        var progressPercentage = entity.NumberOfInstallments == 0
            ? 0m
            : Math.Round(
                (decimal)(entity.NumberOfInstallments - remainingInstallments)
                / entity.NumberOfInstallments
                * 100m,
                2);

        return new InstallmentResponse(
            entity.Id,
            entity.CreditCardId,
            entity.CreditCard.CardName,
            entity.Description,
            entity.TotalAmount,
            entity.NumberOfInstallments,
            remainingInstallments,
            entity.MonthlyPayment,
            entity.StartDate,
            effectiveStatus.ToString(),
            entity.Note,
            unpaidBalance,
            paidAmount,
            progressPercentage,
            entity.CreatedAt,
            entity.UpdatedAt
        );
    }
}

public record CreateInstallmentRequest(
    Guid CreditCardId,
    string Description,
    decimal TotalAmount,
    int NumberOfInstallments,
    DateTime StartDate,
    string? Note
);

public record UpdateInstallmentRequest(
    string Description,
    string? Note
);
