using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Installments;

/// <summary>
/// Update installment description and note.
/// </summary>
public class UpdateInstallmentUseCase(
    IInstallmentRepository installmentRepository,
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<InstallmentResponse> ExecuteAsync(
        Guid id,
        UpdateInstallmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var installment = await installmentRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("Installment", id);

        installment.SetDescription(request.Description);
        installment.SetNote(request.Note);

        await installmentRepository.UpdateAsync(installment, cancellationToken);

        var creditCard = await creditCardRepository.GetByIdAsync(installment.CreditCardId, userId, cancellationToken)
            ?? throw new EntityNotFoundException("CreditCard", installment.CreditCardId);

        return MapToResponse(installment, creditCard.CardName);
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
