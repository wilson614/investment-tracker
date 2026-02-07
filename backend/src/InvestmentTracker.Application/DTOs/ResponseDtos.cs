using InvestmentTracker.Domain.Entities;
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
    public string Purpose { get; init; } = string.Empty;
    public string PurposeDisplayName { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public bool IsDisposable { get; init; }
    public string? Note { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public static FundAllocationResponse FromEntity(FundAllocation entity)
    {
        return new FundAllocationResponse
        {
            Id = entity.Id,
            Purpose = entity.Purpose,
            PurposeDisplayName = entity.Purpose,
            Amount = entity.Amount,
            IsDisposable = entity.IsDisposable,
            Note = entity.Note,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }
}

public record AllocationSummary
{
    public decimal TotalAllocated { get; init; }
    public decimal Unallocated { get; init; }
    public IReadOnlyList<FundAllocationResponse> Allocations { get; init; } = [];
}

public record AllocationBreakdownResponse(
    string Purpose,
    string PurposeDisplayName,
    decimal Amount);

public record TotalAssetsSummaryResponse(
    decimal InvestmentTotal,      // 投資總額 (股票市值)
    decimal BankTotal,            // 銀行總額
    decimal GrandTotal,           // 總資產
    decimal InvestmentPercentage, // 投資佔比 %
    decimal BankPercentage,       // 銀行佔比 %
    decimal TotalMonthlyInterest, // 銀行總月利息
    decimal TotalYearlyInterest,  // 銀行總年利息
    decimal PortfolioValue,       // 投資組合總值 (股票市值 + 帳本現金)
    decimal CashBalance,          // 帳本現金餘額
    decimal DisposableDeposit,    // 可動用存款
    decimal NonDisposableDeposit, // 不可動用存款
    decimal InvestmentRatio,      // 投資比率
    decimal StockRatio,           // 股票部位比率
    decimal TotalAllocated,       // 資產配置總額
    decimal Unallocated,          // 未配置餘額
    IReadOnlyList<AllocationBreakdownResponse>? AllocationBreakdown // 配置明細
);
