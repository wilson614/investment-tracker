using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.FixedDeposits;

/// <summary>
/// Get fixed deposits for current user.
/// </summary>
public class GetFixedDepositsUseCase(
    IFixedDepositRepository fixedDepositRepository,
    ICurrentUserService currentUserService)
{
    public async Task<IReadOnlyList<FixedDepositResponse>> ExecuteAsync(
        FixedDepositStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var fixedDeposits = await fixedDepositRepository.GetAllByUserIdAsync(userId, cancellationToken);

        var filteredDeposits = status.HasValue
            ? fixedDeposits.Where(fd => fd.Status == status.Value).ToList()
            : fixedDeposits;

        return filteredDeposits
            .Select(fd => FixedDepositResponse.FromEntity(fd))
            .ToList();
    }
}
