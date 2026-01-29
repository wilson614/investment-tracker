using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Update a bank account.
/// </summary>
public class UpdateBankAccountUseCase(
    IBankAccountRepository bankAccountRepository,
    InterestEstimationService interestEstimationService,
    ICurrentUserService currentUserService)
{
    public async Task<BankAccountResponse> ExecuteAsync(
        Guid id,
        UpdateBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("BankAccount", id);

        if (bankAccount.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        bankAccount.SetBankName(request.BankName);
        bankAccount.SetTotalAssets(request.TotalAssets);
        bankAccount.SetInterestSettings(request.InterestRate, request.InterestCap);
        bankAccount.SetNote(request.Note);

        await bankAccountRepository.UpdateAsync(bankAccount, cancellationToken);

        return BankAccountResponse.FromEntity(bankAccount, interestEstimationService);
    }
}
