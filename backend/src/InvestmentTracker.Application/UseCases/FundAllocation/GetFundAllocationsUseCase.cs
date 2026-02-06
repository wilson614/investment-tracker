using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.UseCases.FundAllocation;

/// <summary>
/// Get fund allocations for current user with allocation summary.
/// </summary>
public class GetFundAllocationsUseCase(
    IFundAllocationRepository fundAllocationRepository,
    IBankAccountRepository bankAccountRepository,
    IYahooHistoricalPriceService yahooHistoricalPriceService,
    TotalAssetsService totalAssetsService,
    ICurrentUserService currentUserService)
{
    public async Task<AllocationSummary> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("User not authenticated");

        var allocations = await fundAllocationRepository.GetByUserIdAsync(userId, cancellationToken);
        var responses = allocations
            .Select(FundAllocationResponse.FromEntity)
            .ToList();

        var totalAllocated = responses.Sum(a => a.Amount);

        var totalBankAssets = await FundAllocationBankAssetsCalculator.CalculateTotalBankAssetsInTwdAsync(
            userId,
            bankAccountRepository,
            yahooHistoricalPriceService,
            totalAssetsService,
            cancellationToken);

        return new AllocationSummary
        {
            TotalAllocated = totalAllocated,
            Unallocated = totalBankAssets - totalAllocated,
            Allocations = responses
        };
    }
}
