using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Installments;

/// <summary>
/// Get all installments across all credit cards for current user.
/// </summary>
public class GetAllUserInstallmentsUseCase(
    IInstallmentRepository installmentRepository,
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<IReadOnlyList<InstallmentResponse>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var installments = await installmentRepository.GetAllByUserIdAsync(userId, cancellationToken);
        var creditCards = await creditCardRepository.GetAllByUserIdAsync(userId, cancellationToken);

        var creditCardInfoMap = creditCards
            .ToDictionary(c => c.Id, c => new { c.CardName, c.PaymentDueDay });

        var utcNow = DateTime.UtcNow;

        return installments
            .Select(i =>
            {
                if (!creditCardInfoMap.TryGetValue(i.CreditCardId, out var cardInfo))
                {
                    return MapToResponse(i, string.Empty, 1, utcNow);
                }

                return MapToResponse(i, cardInfo.CardName, cardInfo.PaymentDueDay, utcNow);
            })
            .ToList();
    }

    private static InstallmentResponse MapToResponse(
        Installment installment,
        string creditCardName,
        int billingCycleDay,
        DateTime utcNow)
    {
        var remainingInstallments = installment.GetRemainingInstallments(billingCycleDay, utcNow);
        var effectiveStatus = installment.GetEffectiveStatus(billingCycleDay, utcNow);
        var unpaidBalance = Math.Round(installment.MonthlyPayment * remainingInstallments, 2);
        var paidAmount = Math.Round(installment.TotalAmount - unpaidBalance, 2);

        var progressPercentage = installment.NumberOfInstallments == 0
            ? 0m
            : Math.Round(
                (decimal)(installment.NumberOfInstallments - remainingInstallments)
                / installment.NumberOfInstallments
                * 100m,
                2);

        return new InstallmentResponse(
            installment.Id,
            installment.CreditCardId,
            creditCardName,
            installment.Description,
            installment.TotalAmount,
            installment.NumberOfInstallments,
            remainingInstallments,
            installment.MonthlyPayment,
            installment.StartDate,
            effectiveStatus.ToString(),
            installment.Note,
            unpaidBalance,
            paidAmount,
            progressPercentage,
            installment.CreatedAt,
            installment.UpdatedAt
        );
    }
}
