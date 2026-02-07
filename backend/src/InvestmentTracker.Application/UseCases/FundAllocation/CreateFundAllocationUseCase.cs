using InvestmentTracker.Application.DTOs;
using InvestmentTracker.Application.Interfaces;
using InvestmentTracker.Domain.Exceptions;
using InvestmentTracker.Domain.Interfaces;
using InvestmentTracker.Domain.Services;
using System.Linq;

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
    private static readonly string[] NonDisposablePurposeKeywords = ["緊急預備金", "家庭存款", "emergency", "family"];

    public async Task<FundAllocationResponse> ExecuteAsync(
        CreateFundAllocationRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = currentUserService.UserId
            ?? throw new AccessDeniedException("使用者尚未登入");

        var existingAllocations = await fundAllocationRepository.GetByUserIdAsync(userId, cancellationToken);
        var totalAllocated = existingAllocations.Sum(a => a.Amount);

        var totalBankAssets = await FundAllocationBankAssetsCalculator.CalculateTotalBankAssetsInTwdAsync(
            userId,
            bankAccountRepository,
            yahooHistoricalPriceService,
            totalAssetsService,
            cancellationToken);

        if (totalAllocated + request.Amount > totalBankAssets)
            throw new BusinessRuleException("資金配置總額不得超過銀行資產總額。");

        var isDisposable = request.IsDisposable ?? !IsNonDisposablePurpose(request.Purpose);

        var allocation = new Domain.Entities.FundAllocation(
            userId,
            request.Purpose,
            request.Amount,
            request.Note,
            isDisposable);

        await fundAllocationRepository.AddAsync(allocation, cancellationToken);

        return FundAllocationResponse.FromEntity(allocation);
    }

    private static bool IsNonDisposablePurpose(string purpose)
    {
        var normalizedPurpose = purpose.Trim();

        return NonDisposablePurposeKeywords.Any(keyword =>
            normalizedPurpose.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
