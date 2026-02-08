using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.FixedDeposits;

/// <summary>
/// Create a fixed deposit.
/// </summary>
public class CreateFixedDepositUseCase(
    IFixedDepositRepository fixedDepositRepository,
    IBankAccountRepository bankAccountRepository,
    ICurrentUserService currentUserService)
{
    public async Task<FixedDepositResponse> ExecuteAsync(
        CreateFixedDepositRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var bankAccount = await bankAccountRepository.GetByIdAsync(request.BankAccountId, cancellationToken)
            ?? throw new EntityNotFoundException("BankAccount", request.BankAccountId);

        if (bankAccount.UserId != userId)
            throw new AccessDeniedException("Cannot create fixed deposit for another user's bank account");

        var fixedDeposit = new Domain.Entities.FixedDeposit(
            userId,
            request.BankAccountId,
            request.Principal,
            request.AnnualInterestRate,
            request.TermMonths,
            request.StartDate,
            request.Note,
            bankAccount.Currency);

        await fixedDepositRepository.AddAsync(fixedDeposit, cancellationToken);

        var createdFixedDeposit = await fixedDepositRepository.GetByIdAsync(fixedDeposit.Id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("FixedDeposit", fixedDeposit.Id);

        return FixedDepositResponse.FromEntity(createdFixedDeposit);
    }
}
