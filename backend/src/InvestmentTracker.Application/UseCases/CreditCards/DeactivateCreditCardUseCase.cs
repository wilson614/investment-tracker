using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.CreditCards;

/// <summary>
/// Deactivate (soft delete) a credit card.
/// </summary>
public class DeactivateCreditCardUseCase(
    ICreditCardRepository creditCardRepository,
    IInstallmentRepository installmentRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var creditCard = await creditCardRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("CreditCard", id);

        var installments = await installmentRepository.GetByCreditCardIdAsync(id, cancellationToken);
        var hasActiveInstallments = installments.Any(i => i.Status == InstallmentStatus.Active);

        if (hasActiveInstallments)
            throw new BusinessRuleException("Cannot deactivate credit card with active installments.");

        await creditCardRepository.DeleteAsync(creditCard, cancellationToken);
    }
}
