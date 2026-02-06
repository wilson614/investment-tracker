using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.FundAllocation;

/// <summary>
/// Create a fund allocation.
/// </summary>
public class CreateFundAllocationUseCase(
    IFundAllocationRepository fundAllocationRepository,
    IBankAccountRepository bankAccountRepository,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    TotalAssetsService totalAssetsService,
    ICurrentUserService currentUserService)
{
    public async Task<FundAllocationResponse> ExecuteAsync(
        CreateFundAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var existingAllocations = await fundAllocationRepository.GetByUserIdAsync(userId, cancellationToken);
        var totalAllocated = existingAllocations.Sum(a => a.Amount);

        var totalBankAssets = await FundAllocationBankAssetsCalculator.CalculateTotalBankAssetsInTwdAsync(
            userId,
            bankAccountRepository,
            yahooHistoricalPriceService,
            totalAssetsService,
            cancellationToken);

        if (totalAllocated + request.Amount > totalBankAssets)
            throw new BusinessRuleException("Total allocations cannot exceed total bank assets.");

        var allocation = new Domain.Entities.FundAllocation(
            userId,
            request.Purpose,
            request.Amount,
            request.Note);

        await fundAllocationRepository.AddAsync(allocation, cancellationToken);

        return FundAllocationResponse.FromEntity(allocation);
    }
}
