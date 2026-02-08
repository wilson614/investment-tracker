using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.BankAccount;

/// <summary>
/// Close a fixed-deposit bank account.
/// </summary>
public class CloseBankAccountUseCase(
    IBankAccountRepository bankAccountRepository,
    InterestEstimationService interestEstimationService,
    ICurrentUserService currentUserService)
{
    public async Task<BankAccountResponse> ExecuteAsync(
        Guid id,
        CloseBankAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var bankAccount = await bankAccountRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("BankAccount", id);

        if (bankAccount.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        if (bankAccount.AccountType != BankAccountType.FixedDeposit)
            throw new BusinessRuleException("Only fixed-deposit bank accounts can be closed.");

        if (request.ActualInterest.HasValue)
            bankAccount.SetActualInterest(request.ActualInterest.Value);

        bankAccount.SetFixedDepositStatus(FixedDepositStatus.Closed);

        await bankAccountRepository.UpdateAsync(bankAccount, cancellationToken);

        return BankAccountResponse.FromEntity(bankAccount, interestEstimationService);
    }
}
