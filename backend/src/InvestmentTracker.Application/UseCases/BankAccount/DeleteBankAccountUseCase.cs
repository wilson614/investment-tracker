using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Delete (soft delete) a bank account.
/// </summary>
public class DeleteBankAccountUseCase(
    IBankAccountRepository bankAccountRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("BankAccount", id);

        if (bankAccount.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        await bankAccountRepository.DeleteAsync(id, cancellationToken);
    }
}
