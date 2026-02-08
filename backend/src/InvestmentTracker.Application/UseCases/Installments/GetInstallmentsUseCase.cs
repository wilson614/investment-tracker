using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Installments;

/// <summary>
/// Get installments for a specific credit card.
/// </summary>
public class GetInstallmentsUseCase(
    IInstallmentRepository installmentRepository,
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<IReadOnlyList<InstallmentResponse>> ExecuteAsync(
        Guid creditCardId,
        InstallmentStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var creditCard = await creditCardRepository.GetByIdAsync(creditCardId, userId, cancellationToken)
            ?? throw new EntityNotFoundException("CreditCard", creditCardId);

        var installments = await installmentRepository.GetByCreditCardIdAsync(creditCardId, cancellationToken);

        var filteredInstallments = status.HasValue
            ? installments.Where(i => i.Status == status.Value).ToList()
            : installments;

        return filteredInstallments
            .Select(i => MapToResponse(i, creditCard.CardName))
            .ToList();
    }

    private static InstallmentResponse MapToResponse(Installment installment, string creditCardName)
    {
        var unpaidBalance = Math.Round(installment.MonthlyPayment * installment.RemainingInstallments, 2);
        var paidAmount = Math.Round(installment.TotalAmount - unpaidBalance, 2);

        var progressPercentage = installment.NumberOfInstallments == 0
            ? 0m
            : Math.Round(
                (decimal)(installment.NumberOfInstallments - installment.RemainingInstallments)
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
            installment.RemainingInstallments,
            installment.MonthlyPayment,
            installment.StartDate,
            installment.Status.ToString(),
            installment.Note,
            unpaidBalance,
            paidAmount,
            progressPercentage,
            installment.CreatedAt,
            installment.UpdatedAt
        );
    }
}
