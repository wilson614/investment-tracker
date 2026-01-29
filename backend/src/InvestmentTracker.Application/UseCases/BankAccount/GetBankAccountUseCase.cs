using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Get a bank account by ID.
/// </summary>
public class GetBankAccountUseCase(
    IBankAccountRepository bankAccountRepository,
    InterestEstimationService interestEstimationService,
    ICurrentUserService currentUserService)
{
    public async Task<BankAccountResponse> ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("BankAccount", id);

        if (bankAccount.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        return BankAccountResponse.FromEntity(bankAccount, interestEstimationService);
    }
}
