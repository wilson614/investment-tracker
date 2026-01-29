using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Create a bank account.
/// </summary>
public class CreateBankAccountUseCase(
    IBankAccountRepository bankAccountRepository,
    InterestEstimationService interestEstimationService,
    ICurrentUserService currentUserService)
{
    public async Task<BankAccountResponse> ExecuteAsync(
        CreateBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var bankAccount = new Domain.Entities.BankAccount(
            userId,
            request.BankName,
            request.TotalAssets,
            request.InterestRate,
            request.InterestCap,
            request.Note);

        await bankAccountRepository.AddAsync(bankAccount, cancellationToken);

        return BankAccountResponse.FromEntity(bankAccount, interestEstimationService);
    }
}
