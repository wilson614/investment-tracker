using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.FixedDeposits;

/// <summary>
/// Get a fixed deposit by ID.
/// </summary>
public class GetFixedDepositUseCase(
    IFixedDepositRepository fixedDepositRepository,
    ICurrentUserService currentUserService)
{
    public async Task<FixedDepositResponse> ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var fixedDeposit = await fixedDepositRepository.GetByIdAsync(id, userId, cancellationToken)
            ?? throw new EntityNotFoundException("FixedDeposit", id);

        return FixedDepositResponse.FromEntity(fixedDeposit);
    }
}
