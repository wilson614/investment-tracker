using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.FixedDeposits;

/// <summary>
/// Update a fixed deposit.
/// </summary>
public class UpdateFixedDepositUseCase(
    IFixedDepositRepository fixedDepositRepository,
    ICurrentUserService currentUserService)
{
    public async Task<FixedDepositResponse> ExecuteAsync(
        Guid id,
        UpdateFixedDepositRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var fixedDeposit = await fixedDepositRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("FixedDeposit", id);

        fixedDeposit.SetActualInterest(request.ActualInterest);
        fixedDeposit.SetNote(request.Note);

        await fixedDepositRepository.UpdateAsync(fixedDeposit, cancellationToken);

        return FixedDepositResponse.FromEntity(fixedDeposit);
    }
}
