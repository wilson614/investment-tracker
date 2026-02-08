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

        var creditCardNameMap = creditCards
            .ToDictionary(c => c.Id, c => c.CardName);

        return installments
            .Select(i =>
            {
                var creditCardName = creditCardNameMap.TryGetValue(i.CreditCardId, out var cardName)
                    ? cardName
                    : string.Empty;

                return MapToResponse(i, creditCardName);
            })
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
