using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.FixedDeposits;

/// <summary>
/// Close a fixed deposit.
/// </summary>
public class CloseFixedDepositUseCase(
    IFixedDepositRepository fixedDepositRepository,
    ICurrentUserService currentUserService)
{
    public async Task<FixedDepositResponse> ExecuteAsync(
        Guid id,
        CloseFixedDepositRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var fixedDeposit = await fixedDepositRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("FixedDeposit", id);

        if (fixedDeposit.Status is FixedDepositStatus.Closed or FixedDepositStatus.EarlyWithdrawal)
            throw new BusinessRuleException("Fixed deposit is already closed.");

        if (request.IsEarlyWithdrawal)
        {
            fixedDeposit.MarkAsEarlyWithdrawal(request.ActualInterest);
        }
        else if (DateTime.UtcNow.Date >= fixedDeposit.MaturityDate.Date)
        {
            fixedDeposit.SetActualInterest(request.ActualInterest);
            fixedDeposit.MarkAsMatured();
        }
        else
        {
            fixedDeposit.Close(request.ActualInterest);
        }

        await fixedDepositRepository.UpdateAsync(fixedDeposit, cancellationToken);

        return FixedDepositResponse.FromEntity(fixedDeposit);
    }
}
