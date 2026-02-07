using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.FundAllocation;

/// <summary>
/// Update a fund allocation.
/// </summary>
public class UpdateFundAllocationUseCase(
    IFundAllocationRepository fundAllocationRepository,
    IBankAccountRepository bankAccountRepository,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    TotalAssetsService totalAssetsService,
    ICurrentUserService currentUserService)
{
    public async Task<FundAllocationResponse> ExecuteAsync(
        Guid id,
        UpdateFundAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var allocation = await fundAllocationRepository.GetByIdAsync(id, cancellationToken)
            ?? throw new EntityNotFoundException("FundAllocation", id);

        if (allocation.UserId != currentUserService.UserId)
            throw new AccessDeniedException();

        var userAllocations = await fundAllocationRepository.GetByUserIdAsync(allocation.UserId, cancellationToken);
        var updatedAmount = request.Amount ?? allocation.Amount;

        var totalAllocatedExcludingCurrent = userAllocations
            .Where(a => a.Id != allocation.Id)
            .Sum(a => a.Amount);

        var totalBankAssets = await FundAllocationBankAssetsCalculator.CalculateTotalBankAssetsInTwdAsync(
            allocation.UserId,
            bankAccountRepository,
            yahooHistoricalPriceService,
            totalAssetsService,
            cancellationToken);

        if (totalAllocatedExcludingCurrent + updatedAmount > totalBankAssets)
            throw new BusinessRuleException("資金配置總額不得超過銀行資產總額。");

        if (request.Amount.HasValue)
            allocation.SetAmount(request.Amount.Value);

        if (!string.IsNullOrWhiteSpace(request.Purpose))
            allocation.SetPurpose(request.Purpose);

        if (request.IsDisposable.HasValue)
            allocation.SetIsDisposable(request.IsDisposable.Value);

        allocation.SetNote(request.Note);

        await fundAllocationRepository.UpdateAsync(allocation, cancellationToken);

        return FundAllocationResponse.FromEntity(allocation);
    }
}
