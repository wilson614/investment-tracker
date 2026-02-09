using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.Installments;

/// <summary>
/// Delete an installment.
/// </summary>
public class DeleteInstallmentUseCase(
    IInstallmentRepository installmentRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var installment = await installmentRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("Installment", id);

        if (installment.CreditCard.UserId != userId)
            throw new AccessDeniedException();

        await installmentRepository.DeleteAsync(installment, cancellationToken);
    }
}
