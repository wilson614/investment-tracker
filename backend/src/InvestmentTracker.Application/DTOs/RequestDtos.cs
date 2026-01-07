using InvestmentTracker.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.Application.DTOs;

/// <summary>
/// Request DTO for creating a portfolio.
/// </summary>
public record CreatePortfolioRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    [StringLength(3, MinimumLength = 3)]
    public string BaseCurrency { get; init; } = "USD";

    [StringLength(3, MinimumLength = 3)]
    public string HomeCurrency { get; init; } = "TWD";
}

/// <summary>
/// Request DTO for updating a portfolio.
/// </summary>
public record UpdatePortfolioRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }
}

/// <summary>
/// Request DTO for creating a stock transaction.
/// </summary>
public record CreateStockTransactionRequest
{
    [Required]
    public Guid PortfolioId { get; init; }

    [Required]
    public DateTime TransactionDate { get; init; }

    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Ticker { get; init; } = string.Empty;

    [Required]
    public TransactionType TransactionType { get; init; }

    [Required]
    [Range(0.0001, double.MaxValue)]
    public decimal Shares { get; init; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal PricePerShare { get; init; }

    /// <summary>
    /// Exchange rate for conversion to home currency.
    /// Optional when FundSource is CurrencyLedger - will be calculated from ledger's weighted average cost.
    /// </summary>
    [Range(0, double.MaxValue)]
    public decimal? ExchangeRate { get; init; }

    [Range(0, double.MaxValue)]
    public decimal Fees { get; init; }

    public FundSource FundSource { get; init; } = FundSource.None;

    public Guid? CurrencyLedgerId { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

/// <summary>
/// Request DTO for updating a stock transaction.
/// </summary>
public record UpdateStockTransactionRequest
{
    [Required]
    public DateTime TransactionDate { get; init; }

    [Required]
    [Range(0.0001, double.MaxValue)]
    public decimal Shares { get; init; }

    [Required]
    [Range(0, double.MaxValue)]
    public decimal PricePerShare { get; init; }

    [Required]
    [Range(0.000001, double.MaxValue)]
    public decimal ExchangeRate { get; init; }

    [Range(0, double.MaxValue)]
    public decimal Fees { get; init; }

    public FundSource FundSource { get; init; } = FundSource.None;

    public Guid? CurrencyLedgerId { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }
}

/// <summary>
/// Request for calculating portfolio performance with current prices.
/// </summary>
public record CalculatePerformanceRequest
{
    public Dictionary<string, CurrentPriceInfo> CurrentPrices { get; init; } = new();
}

/// <summary>
/// Request for calculating XIRR.
/// </summary>
public record CalculateXirrRequest
{
    public Dictionary<string, CurrentPriceInfo>? CurrentPrices { get; init; }
    public DateTime? AsOfDate { get; init; }
}

/// <summary>
/// Result of XIRR calculation.
/// </summary>
public record XirrResultDto
{
    public double? Xirr { get; init; }
    public double? XirrPercentage { get; init; }
    public int CashFlowCount { get; init; }
    public DateTime AsOfDate { get; init; }
}

/// <summary>
/// Current price information for a ticker.
/// </summary>
public record CurrentPriceInfo
{
    public decimal Price { get; init; }
    public decimal ExchangeRate { get; init; }
}
