using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Get bank accounts for current user.
/// </summary>
public class GetBankAccountsUseCase(
    IBankAccountRepository bankAccountRepository,
    InterestEstimationService interestEstimationService,
    ICurrentUserService currentUserService)
{
    public async Task<IReadOnlyList<BankAccountResponse>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var accounts = await bankAccountRepository.GetByUserIdAsync(userId, cancellationToken);

        return accounts
            .Select(a => BankAccountResponse.FromEntity(a, interestEstimationService))
            .ToList();
    }
}
