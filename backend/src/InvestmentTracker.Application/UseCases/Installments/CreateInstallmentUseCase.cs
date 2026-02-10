using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Installments;

/// <summary>
/// Create a new installment.
/// </summary>
public class CreateInstallmentUseCase(
    IInstallmentRepository installmentRepository,
    ICreditCardRepository creditCardRepository,
    ICurrentUserService currentUserService)
{
    public async Task<InstallmentResponse> ExecuteAsync(
        CreateInstallmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var creditCard = await creditCardRepository.GetByIdAsync(request.CreditCardId, userId, cancellationToken)
            ?? throw new EntityNotFoundException("CreditCard", request.CreditCardId);

        var firstPaymentDate = request.FirstPaymentDate;
        var paymentDueDay = creditCard.PaymentDueDay;
        var adjustedDay = Math.Min(paymentDueDay, DateTime.DaysInMonth(firstPaymentDate.Year, firstPaymentDate.Month));
        var adjustedFirstPaymentDate = new DateTime(firstPaymentDate.Year, firstPaymentDate.Month, adjustedDay, 0, 0, 0, DateTimeKind.Utc);

        var installment = new Domain.Entities.Installment(
            request.CreditCardId,
            userId,
            request.Description,
            request.TotalAmount,
            request.NumberOfInstallments,
            request.NumberOfInstallments,
            adjustedFirstPaymentDate,
            request.Note);

        await installmentRepository.AddAsync(installment, cancellationToken);

        return MapToResponse(installment, creditCard.CardName, creditCard.PaymentDueDay, DateTime.UtcNow);
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
            installment.FirstPaymentDate,
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
