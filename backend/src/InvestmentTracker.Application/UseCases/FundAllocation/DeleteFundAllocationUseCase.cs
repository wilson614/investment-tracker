using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;

namespace InvestmentTracker.Application.UseCases.FundAllocation;

/// <summary>
/// Delete a fund allocation.
/// </summary>
public class DeleteFundAllocationUseCase(
    IFundAllocationRepository fundAllocationRepository,
    ICurrentUserService currentUserService)
{
    public async Task ExecuteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var allocation = await fundAllocationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("FundAllocation", id);

        if (allocation.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        await fundAllocationRepository.DeleteAsync(allocation, cancellationToken);
    }
}
