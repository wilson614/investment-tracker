using InvestmentTracker.Domain.Entities;
using InvestmentTracker.Domain.Enums;
using InvestmentTracker.Domain.Services;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Bank account response DTO.
/// </summary>
public record BankAccountResponse
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string BankName { get; init; } = string.Empty;
    public decimal TotalAssets { get; init; }
    public decimal InterestRate { get; init; }
    public decimal InterestCap { get; init; }
    public string Currency { get; init; } = string.Empty;
    public string? Note { get; init; }

    public decimal MonthlyInterest { get; init; }
    public decimal YearlyInterest { get; init; }

    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static BankAccountResponse FromEntity(BankAccount entity, InterestEstimationService interestEstimationService)
    {
        var estimation = interestEstimationService.Calculate(entity);

        return new BankAccountResponse
        {
            Id = entity.Id,
            UserId = entity.UserId,
            BankName = entity.BankName,
            TotalAssets = entity.TotalAssets,
            InterestRate = entity.InterestRate,
            InterestCap = entity.InterestCap,
            Currency = entity.Currency,
            Note = entity.Note,
            MonthlyInterest = estimation.MonthlyInterest,
            YearlyInterest = estimation.YearlyInterest,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}

public record FundAllocationResponse
{
    public Guid Id { get; init; }
    public AllocationPurpose Purpose { get; init; }
    public string PurposeDisplayName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public string? Note { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static FundAllocationResponse FromEntity(FundAllocation entity)
    {
        return new FundAllocationResponse
        {
            Id = entity.Id,
            Purpose = entity.Purpose,
            PurposeDisplayName = GetPurposeDisplayName(entity.Purpose),
            Amount = entity.Amount,
            Note = entity.Note,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    public static string GetPurposeDisplayName(AllocationPurpose purpose)
    {
        return purpose switch
        {
            AllocationPurpose.EmergencyFund => "緊急預備金",
            AllocationPurpose.FamilyDeposit => "家庭存款",
            AllocationPurpose.General => "一般",
            AllocationPurpose.Savings => "儲蓄",
            AllocationPurpose.Investment => "投資準備金",
            AllocationPurpose.Other => "其他",
            _ => purpose.ToString()
        };
    }
}

public record AllocationSummary
{
    public decimal TotalAllocated { get; init; }
    public decimal Unallocated { get; init; }
    public IReadOnlyList<FundAllocationResponse> Allocations { get; init; } = [];
}

public record TotalAssetsSummaryResponse(
    decimal InvestmentTotal,      // 投資總額 (股票市值)
    decimal BankTotal,            // 銀行總額
    decimal GrandTotal,           // 總資產
    decimal InvestmentPercentage, // 投資佔比 %
    decimal BankPercentage,       // 銀行佔比 %
    decimal TotalMonthlyInterest, // 銀行總月利息
    decimal TotalYearlyInterest   // 銀行總年利息
);
